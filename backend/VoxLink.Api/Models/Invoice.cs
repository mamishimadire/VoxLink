namespace VoxLink.Api.Models;

public class Invoice
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public Guid CompanyId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public decimal AmountDue { get; set; }
    public decimal AmountPaid { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset? PeriodEnd { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? PdfStoragePath { get; set; }

    public Company? Company { get; set; }
}
