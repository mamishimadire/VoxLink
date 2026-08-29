namespace VoxLink.Api.Models;

public class InvoiceGenerationRequest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ProposedBy { get; set; }
    public DateTimeOffset ProposedAt { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public Guid? GeneratedInvoiceId { get; set; }

    public Company? Company { get; set; }
}
