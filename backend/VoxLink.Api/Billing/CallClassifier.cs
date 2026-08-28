namespace VoxLink.Api.Billing;

public static class CallClassifier
{
    public static bool IsLocal(string destinationNumber, string localCountryCode) =>
        destinationNumber.StartsWith(localCountryCode, StringComparison.Ordinal);

    /// <summary>
    /// Converts a locally-dialed number (e.g. "0660070210", the way people
    /// actually type numbers) into E.164 (e.g. "+27660070210") — the only
    /// format Twilio can actually route. Real phones do this translation
    /// against the SIM's home network automatically; Twilio has no such
    /// concept, so the app has to do it before ever reaching Twilio.
    /// </summary>
    public static string NormalizeNumber(string number, string localCountryCode)
    {
        var trimmed = number.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("+", StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("0", StringComparison.Ordinal))
        {
            return localCountryCode + trimmed[1..];
        }

        return trimmed;
    }
}
