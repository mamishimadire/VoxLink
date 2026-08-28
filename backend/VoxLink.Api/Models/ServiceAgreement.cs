namespace VoxLink.Api.Models;

public class ServiceAgreement
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TermsVersion { get; set; } = "";
    public string AgreedByName { get; set; } = "";
    public string AgreedByEmail { get; set; } = "";
    public DateTimeOffset AgreedAt { get; set; }
    public string? IpAddress { get; set; }
    public string PdfStoragePath { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
