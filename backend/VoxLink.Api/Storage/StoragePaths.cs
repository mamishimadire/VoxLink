using System.Text.RegularExpressions;

namespace VoxLink.Api.Storage;

public static class StoragePaths
{
    /// <summary>
    /// Returns a safe file extension (".jpg", ".pdf", etc.) for use in a
    /// storage path, or "" if the original extension isn't a plain
    /// alphanumeric one. Never embed a user-supplied filename directly in a
    /// storage path/URL — spaces, parentheses, and other characters common
    /// in real filenames (e.g. "Tonny 01 (1).jpg") break the signed-URL
    /// round trip, unlike every other upload in this app, which only ever
    /// uses a generated GUID + a fixed literal extension.
    /// </summary>
    public static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return Regex.IsMatch(extension, @"^\.[a-zA-Z0-9]{1,5}$") ? extension : "";
    }
}
