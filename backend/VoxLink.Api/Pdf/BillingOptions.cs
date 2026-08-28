namespace VoxLink.Api.Pdf;

public class BillingOptions
{
    public const string SectionName = "Billing";

    public string PayeeName { get; set; } = "";
    public string BankName { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountType { get; set; } = "";

    // E.164 country calling code (with leading +) treated as "local" for
    // billing purposes — anything else is billed at the international rate.
    public string LocalCountryCode { get; set; } = "+27";
}
