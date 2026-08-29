namespace VoxLink.Api.Storage;

/// <summary>
/// Allowlists what an upload endpoint accepts by BOTH extension and the
/// browser-declared Content-Type, requiring them to agree — stops the two
/// most common ways an upload endpoint gets abused: a disguised executable
/// (e.g. "invoice.pdf.exe") or a mismatched declaration (a ".jpg" filename
/// with a "text/html" Content-Type, which some storage/CDN setups will
/// still serve as HTML). This does not inspect the file's actual bytes
/// (magic-number sniffing) — a determined attacker who gets both the
/// extension and Content-Type to agree could still smuggle a same-type
/// polyglot past this, so treat it as a floor, not a complete guarantee.
/// </summary>
public static class FileValidation
{
    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    private static readonly Dictionary<string, string> ProofOfPaymentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
    };

    public static bool IsAllowedImage(string fileName, string? contentType) =>
        IsAllowed(fileName, contentType, ImageTypes);

    public static bool IsAllowedProofOfPayment(string fileName, string? contentType) =>
        IsAllowed(fileName, contentType, ProofOfPaymentTypes);

    private static bool IsAllowed(string fileName, string? contentType, Dictionary<string, string> allowed)
    {
        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension)
            && allowed.TryGetValue(extension, out var expectedContentType)
            && string.Equals(expectedContentType, contentType, StringComparison.OrdinalIgnoreCase);
    }
}
