namespace VoxLink.Api.Models;

public class Payment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string? Method { get; set; }
    public string Status { get; set; } = "pending";
    public string? ProviderReference { get; set; }
    public string? ProofFilePath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Company? Company { get; set; }
    public Invoice? Invoice { get; set; }
}
