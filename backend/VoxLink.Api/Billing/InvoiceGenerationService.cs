using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoxLink.Api.Data;
using VoxLink.Api.Email;
using VoxLink.Api.Models;
using VoxLink.Api.Pdf;
using VoxLink.Api.Storage;

namespace VoxLink.Api.Billing;

public class InvoiceGenerationService
{
    // Cross-tenant by design (iterates every company's subscriptions), so it
    // uses the service context, which bypasses RLS.
    private readonly VoxLinkServiceDbContext _db;
    private readonly SupabaseStorageClient _storage;
    private readonly IEmailSender _emailSender;
    private readonly BillingOptions _billingOptions;
    private readonly ILogger<InvoiceGenerationService> _logger;

    public InvoiceGenerationService(
        VoxLinkServiceDbContext db,
        SupabaseStorageClient storage,
        IEmailSender emailSender,
        IOptions<BillingOptions> billingOptions,
        ILogger<InvoiceGenerationService> logger)
    {
        _db = db;
        _storage = storage;
        _emailSender = emailSender;
        _billingOptions = billingOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Finds every subscription whose current billing period has ended and no
    /// invoice has been issued for that period yet, generates and emails an
    /// invoice for it, then rolls the subscription into its next period.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dueSubscriptions = await _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.Status == "active" && s.CurrentPeriodEnd <= now)
            .ToListAsync(cancellationToken);

        var generated = 0;

        foreach (var subscription in dueSubscriptions)
        {
            var alreadyInvoiced = await _db.Invoices.AnyAsync(
                i => i.SubscriptionId == subscription.Id && i.IssuedAt >= subscription.CurrentPeriodStart,
                cancellationToken);

            if (alreadyInvoiced)
            {
                RollSubscriptionPeriod(subscription);
                continue;
            }

            await GenerateInvoiceAsync(subscription, cancellationToken);
            RollSubscriptionPeriod(subscription);
            generated++;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var suspended = await SuspendOverdueAccountsAsync(cancellationToken);
        return generated + suspended;
    }

    /// <summary>
    /// Grace period enforcement: any active company with an unpaid invoice past
    /// its due date gets suspended (blocks login) until it's paid.
    /// </summary>
    private async Task<int> SuspendOverdueAccountsAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdueCompanyIds = await _db.Invoices
            .Where(i => i.Status == "pending" && i.DueDate != null && i.DueDate < today)
            .Select(i => i.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (overdueCompanyIds.Count == 0) return 0;

        var companies = await _db.Companies
            .Where(c => overdueCompanyIds.Contains(c.Id) && c.Status == "active")
            .ToListAsync(cancellationToken);

        foreach (var company in companies)
        {
            company.Status = "suspended";
            company.UpdatedAt = DateTimeOffset.UtcNow;

            var recipient = company.AdminContactEmail ?? company.BillingContactEmail ?? company.Email;
            if (!string.IsNullOrWhiteSpace(recipient))
            {
                try
                {
                    await _emailSender.SendAsync(
                        recipient,
                        "VoxLink access suspended — overdue invoice",
                        $"<p>Access for {company.Name} has been suspended due to an unpaid invoice past its due date. " +
                        "Log in, pay the outstanding invoice, and upload proof of payment to restore access.</p>",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to email suspension notice for company {CompanyId}", company.Id);
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return companies.Count;
    }

    private static void RollSubscriptionPeriod(Subscription subscription)
    {
        subscription.CurrentPeriodStart = subscription.CurrentPeriodEnd;
        subscription.CurrentPeriodEnd = subscription.CurrentPeriodEnd.AddMonths(1);
    }

    private async Task GenerateInvoiceAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstAsync(c => c.Id == subscription.CompanyId, cancellationToken);
        var plan = subscription.Plan!;

        var calls = await _db.Calls
            .Where(c => c.CompanyId == company.Id
                && c.CreatedAt >= subscription.CurrentPeriodStart
                && c.CreatedAt < subscription.CurrentPeriodEnd)
            .Select(c => new { c.DestinationNumber, c.DurationSeconds })
            .ToListAsync(cancellationToken);

        var localCalls = calls.Where(c => CallClassifier.IsLocal(c.DestinationNumber, _billingOptions.LocalCountryCode)).ToList();
        var internationalCalls = calls.Except(localCalls).ToList();

        var localMinutes = Math.Ceiling(localCalls.Sum(c => c.DurationSeconds) / 60m);
        var internationalMinutes = Math.Ceiling(internationalCalls.Sum(c => c.DurationSeconds) / 60m);

        // Included minutes only ever pool local usage — international calls
        // are billed from the first minute, never covered by the plan.
        var localOverageMinutes = Math.Max(0, localMinutes - plan.IncludedMinutes);
        var localOverageAmount = localOverageMinutes * plan.LocalRatePerMin;
        var internationalAmount = internationalMinutes * plan.InternationalRatePerMin;
        var amountDue = plan.MonthlyPrice + localOverageAmount + internationalAmount;

        var lineItems = new List<InvoiceLineItem>
        {
            new($"{plan.Name} plan — base fee ({plan.IncludedMinutes} local min included)", plan.MonthlyPrice)
        };
        if (localOverageMinutes > 0)
        {
            lineItems.Add(new($"Local usage overage — {localOverageMinutes} min @ R{plan.LocalRatePerMin:0.00}/min", localOverageAmount));
        }
        if (internationalMinutes > 0)
        {
            lineItems.Add(new($"International usage — {internationalMinutes} min @ R{plan.InternationalRatePerMin:0.00}/min", internationalAmount));
        }

        var now = DateTimeOffset.UtcNow;
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            SubscriptionId = subscription.Id,
            AmountDue = amountDue,
            AmountPaid = 0,
            Status = "pending",
            DueDate = DateOnly.FromDateTime((now + TimeSpan.FromDays(7)).UtcDateTime),
            IssuedAt = now
        };

        var pdfBytes = InvoicePdfGenerator.Generate(
            company.Name, invoice.Id, invoice.IssuedAt, invoice.DueDate, lineItems, amountDue, _billingOptions);

        var storagePath = $"invoices/{company.Id}/{invoice.Id}.pdf";
        try
        {
            await _storage.UploadAsync(storagePath, pdfBytes, "application/pdf", cancellationToken);
            invoice.PdfStoragePath = storagePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload invoice PDF for company {CompanyId}", company.Id);
        }

        _db.Invoices.Add(invoice);

        var recipient = company.AdminContactEmail ?? company.BillingContactEmail ?? company.Email;
        if (!string.IsNullOrWhiteSpace(recipient))
        {
            try
            {
                var html = $"""
                    <p>Hi,</p>
                    <p>Your VoxLink invoice for {company.Name} is ready: <strong>R {amountDue:0.00}</strong>, due {invoice.DueDate:yyyy-MM-dd}.</p>
                    <p>Log in to VoxLink to view the invoice and upload proof of payment once paid.</p>
                    """;
                await _emailSender.SendAsync(recipient, $"VoxLink invoice — R {amountDue:0.00} due {invoice.DueDate:yyyy-MM-dd}", html, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to email invoice for company {CompanyId}", company.Id);
            }
        }
    }
}
