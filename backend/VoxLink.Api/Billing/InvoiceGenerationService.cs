using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoxLink.Api.Data;
using VoxLink.Api.Email;
using VoxLink.Api.Models;
using VoxLink.Api.Pdf;
using VoxLink.Api.Storage;

namespace VoxLink.Api.Billing;

public record InvoicePreview(
    Guid CompanyId, string CompanyName, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd,
    List<InvoiceLineItem> LineItems, decimal AmountDue);

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
            // Keyed on the exact period-end boundary (not just "any invoice
            // since period start") so an earlier ad-hoc invoice — which only
            // covers part of the period and moves CurrentPeriodStart forward
            // without touching CurrentPeriodEnd — never gets mistaken for
            // the invoice covering the rest of the period through month-end.
            var alreadyInvoiced = await _db.Invoices.AnyAsync(
                i => i.SubscriptionId == subscription.Id && i.PeriodEnd == subscription.CurrentPeriodEnd,
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

            var recipients = await GetOwnerAdminEmailsAsync(company.Id, cancellationToken);
            foreach (var recipient in recipients)
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
        subscription.CurrentPeriodFeeBilled = false;
        subscription.CurrentPeriodLocalMinutesBilled = 0;
    }

    private Task GenerateInvoiceAsync(Subscription subscription, CancellationToken cancellationToken) =>
        GenerateInvoiceCoreAsync(subscription, subscription.CurrentPeriodStart, subscription.CurrentPeriodEnd, cancellationToken);

    /// <summary>
    /// Lets a platform admin trigger an invoice on demand instead of waiting
    /// for month-end — covers the current subscription's period-to-date
    /// usage. Safe to call more than once within the same period: the flat
    /// monthly fee and included-minutes pool are only ever billed once per
    /// period (tracked on the subscription), and each invoice only covers
    /// calls made since the last one, so nothing gets double-billed.
    /// </summary>
    public async Task<Invoice> GenerateAdHocInvoiceAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var subscription = await GetLatestSubscriptionAsync(companyId, cancellationToken);
        var periodEnd = DateTimeOffset.UtcNow;
        var invoice = await GenerateInvoiceCoreAsync(subscription, subscription.CurrentPeriodStart, periodEnd, cancellationToken);

        // The next auto/ad-hoc invoice should only bill usage from here on.
        subscription.CurrentPeriodStart = periodEnd;

        await _db.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    /// <summary>
    /// Computes what an ad-hoc invoice would look like — company, period,
    /// line items, amount due — without creating, storing, or emailing
    /// anything, or marking the fee/included-minutes pool as billed. Backs
    /// the "preview before you commit" step in the UI; matches exactly what
    /// GenerateAdHocInvoiceAsync would actually charge if committed right after.
    /// </summary>
    public async Task<InvoicePreview> PreviewAdHocInvoiceAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var subscription = await GetLatestSubscriptionAsync(companyId, cancellationToken);
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId, cancellationToken);
        var periodEnd = DateTimeOffset.UtcNow;

        var calls = await _db.Calls
            .Where(c => c.CompanyId == companyId && c.CreatedAt >= subscription.CurrentPeriodStart && c.CreatedAt < periodEnd)
            .Select(c => new CallUsageRow(c.DestinationNumber, c.DurationSeconds))
            .ToListAsync(cancellationToken);

        var usage = ComputeUsageForPeriod(calls, subscription);

        return new InvoicePreview(
            company.Id, company.Name, subscription.CurrentPeriodStart, periodEnd, usage.LineItems, usage.AmountDue);
    }

    private async Task<Subscription> GetLatestSubscriptionAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No plan/subscription to bill against yet.");

    /// <summary>
    /// The flat fee and included-minutes pool are only ever billed once per
    /// period — remaining allowance/fee state lives on the subscription so a
    /// later invoice in the same period (whether ad-hoc or the eventual
    /// month-end auto invoice) only bills what's new since the last one.
    /// </summary>
    private UsageBreakdown ComputeUsageForPeriod(IReadOnlyList<CallUsageRow> calls, Subscription subscription)
    {
        var includedMinutesRemaining = Math.Max(0, subscription.Plan!.IncludedMinutes - subscription.CurrentPeriodLocalMinutesBilled);
        return UsageCalculator.Compute(
            calls, subscription.Plan, _billingOptions.LocalCountryCode,
            includedMinutesRemaining, includeMonthlyFee: !subscription.CurrentPeriodFeeBilled);
    }

    private async Task<Invoice> GenerateInvoiceCoreAsync(
        Subscription subscription, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken)
    {
        var company = await _db.Companies.FirstAsync(c => c.Id == subscription.CompanyId, cancellationToken);

        var calls = await _db.Calls
            .Where(c => c.CompanyId == company.Id && c.CreatedAt >= periodStart && c.CreatedAt < periodEnd)
            .Select(c => new CallUsageRow(c.DestinationNumber, c.DurationSeconds))
            .ToListAsync(cancellationToken);

        var usage = ComputeUsageForPeriod(calls, subscription);
        subscription.CurrentPeriodFeeBilled = true;
        subscription.CurrentPeriodLocalMinutesBilled += usage.LocalMinutes;

        var now = DateTimeOffset.UtcNow;
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = await InvoiceNumbering.NextAsync(_db.Database, now, cancellationToken),
            CompanyId = company.Id,
            SubscriptionId = subscription.Id,
            AmountDue = usage.AmountDue,
            AmountPaid = 0,
            Status = "pending",
            PeriodEnd = periodEnd,
            DueDate = DateOnly.FromDateTime((now + TimeSpan.FromDays(7)).UtcDateTime),
            IssuedAt = now
        };

        var pdfBytes = InvoicePdfGenerator.Generate(
            company.Name, invoice.InvoiceNumber, invoice.IssuedAt, invoice.DueDate, usage.LineItems, usage.AmountDue, _billingOptions);

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

        var recipients = await GetOwnerAdminEmailsAsync(company.Id, cancellationToken);
        foreach (var recipient in recipients)
        {
            try
            {
                var html = $"""
                    <p>Hi,</p>
                    <p>Your VoxLink invoice {invoice.InvoiceNumber} for {company.Name} is ready: <strong>R {usage.AmountDue:0.00}</strong>, due {invoice.DueDate:yyyy-MM-dd}.</p>
                    <p>Log in to VoxLink to view the invoice and upload proof of payment once paid.</p>
                    """;
                await _emailSender.SendAsync(recipient, $"VoxLink invoice {invoice.InvoiceNumber} — R {usage.AmountDue:0.00} due {invoice.DueDate:yyyy-MM-dd}", html, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to email invoice for company {CompanyId}", company.Id);
            }
        }

        return invoice;
    }

    /// <summary>
    /// Billing communication (invoices, overdue-suspension notices) goes to
    /// the company's actual owner/admin user accounts — never a generic
    /// contact-field address and never regular employees.
    /// </summary>
    private async Task<List<string>> GetOwnerAdminEmailsAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _db.Users
            .Where(u => u.CompanyId == companyId && (u.Role == "owner" || u.Role == "admin") && u.Status != "suspended")
            .Select(u => u.Email)
            .ToListAsync(cancellationToken);
}
