namespace VoxLink.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "VoxLink";
    public string Audience { get; set; } = "VoxLink";
    public int ExpiryMinutes { get; set; } = 60 * 12;
}
