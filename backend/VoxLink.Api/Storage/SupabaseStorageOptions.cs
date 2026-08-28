namespace VoxLink.Api.Storage;

public class SupabaseStorageOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = "";
    public string ServiceRoleKey { get; set; } = "";
    public string Bucket { get; set; } = "voxlink-files";
}
