using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoxLink.Api.Data;
using VoxLink.Api.Models;
using VoxLink.Api.Pdf;
using VoxLink.Api.Storage;

namespace VoxLink.Api.Billing;

/// <summary>
/// Records a company's chosen tier and, for tiers with a fixed price, creates
/// the upfront "platform fee" invoice they must pay and upload proof for
/// before a platform admin will approve them. Shared by the registration
/// form (pick a tier while signing up, no company context yet — pass the
/// service DbContext) and the post-login onboarding screen (pick or change a
/// tier before paying — pass the caller's regular tenant-scoped DbContext).
/// </summary>
public class SignupInvoiceService
{
    private readonly SupabaseStorageClient _storage;
    private readonly BillingOptions _billingOptions;

    public SignupInvoiceService(SupabaseStorageClient storage, IOptions<BillingOptions> billingOptions)
    {
        _storage = storage;
        _billingOptions = billingOptions.Value;
    }

    public async Task<string> SelectPlanAsync(IVoxLinkDbContext db, Company company, Plan plan, CancellationToken cancellationToken)
    {
        company.SelectedPlanId = plan.Id;
        company.UpdatedAt = DateTimeOffset.UtcNow;

        if (plan.IsCustomQuote)
        {
            await db.SaveChangesAsync(cancellationToken);
            return $"{plan.Name} selected. We'll follow up with a custom quote.";
        }

        // Reuse an existing unpaid signup invoice if the client is switching
        // tiers before paying; otherwise create the upfront platform-fee invoice.
        var signupInvoice = await db.Invoices
            .Where(i => i.CompanyId == company.Id && i.SubscriptionId == null && i.Status == "pending")
            .OrderByDescending(i => i.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (signupInvoice is null)
        {
            signupInvoice = new Invoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = await InvoiceNumbering.NextAsync(db.Database, now, cancellationToken),
                CompanyId = company.Id,
                SubscriptionId = null,
                AmountDue = plan.MonthlyPrice,
                AmountPaid = 0,
                Status = "pending",
                DueDate = DateOnly.FromDateTime(now.UtcDateTime),
                IssuedAt = now
            };
            db.Invoices.Add(signupInvoice);
        }
        else
        {
            signupInvoice.AmountDue = plan.MonthlyPrice;
        }

        var pdfBytes = InvoicePdfGenerator.Generate(
            company.Name, signupInvoice.InvoiceNumber, signupInvoice.IssuedAt, signupInvoice.DueDate,
            [new InvoiceLineItem($"{plan.Name} plan — platform fee (sign-up)", plan.MonthlyPrice)],
            plan.MonthlyPrice, _billingOptions);

        var storagePath = $"invoices/{company.Id}/{signupInvoice.Id}.pdf";
        await _storage.UploadAsync(storagePath, pdfBytes, "application/pdf", cancellationToken);
        signupInvoice.PdfStoragePath = storagePath;

        await db.SaveChangesAsync(cancellationToken);

        return $"{plan.Name} selected. Pay R{plan.MonthlyPrice:0.00} and upload proof of payment to continue.";
    }
}
