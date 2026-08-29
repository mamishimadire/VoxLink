using System.Security.Claims;

namespace VoxLink.Api.Auth;

public static class CurrentUserExtensions
{
    public static Guid GetCompanyId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst("company_id")!.Value);

    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)!.Value);

    public static string GetEmail(this ClaimsPrincipal user) =>
        user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)!.Value;

    public static bool IsPlatformAdmin(this ClaimsPrincipal user) =>
        user.FindFirst("is_platform_admin")?.Value == "true";

    public static bool IsBusinessOwner(this ClaimsPrincipal user) =>
        user.FindFirst("is_business_owner")?.Value == "true";
}
