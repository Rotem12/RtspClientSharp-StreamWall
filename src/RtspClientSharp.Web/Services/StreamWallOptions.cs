namespace RtspClientSharp.Web.Services;

public sealed class StreamWallOptions
{
    public string AccessToken { get; set; } = string.Empty;
    public bool ReadOnly { get; set; }
    public bool RedirectToHttps { get; set; }
    public bool EnableHsts { get; set; }
    public bool EnableForwardedHeaders { get; set; }
    public int MaxActiveSessions { get; set; } = 16;
    public int MaxViewersPerSession { get; set; } = 32;
    public int BackupRetentionCount { get; set; } = 20;
    public int MaxRequestBodyBytes { get; set; } = 256 * 1024;
    public string DataDirectory { get; set; } = "App_Data";
    public string BackupDirectory { get; set; } = "App_Data/backups";
    public string KeyDirectory { get; set; } = "App_Data/keys";
    public bool EnforceSourceNetworkPolicy { get; set; }
    public bool AllowLoopbackSources { get; set; } = true;
    public bool AllowLinkLocalSources { get; set; }
    public List<string> AllowedSourceNetworks { get; set; } = new();

    public void Normalize()
    {
        AccessToken = AccessToken?.Trim() ?? string.Empty;
        MaxActiveSessions = Math.Clamp(MaxActiveSessions, 1, 256);
        MaxViewersPerSession = Math.Clamp(MaxViewersPerSession, 1, 256);
        BackupRetentionCount = Math.Clamp(BackupRetentionCount, 1, 100);
        MaxRequestBodyBytes = Math.Clamp(MaxRequestBodyBytes, 16 * 1024, 4 * 1024 * 1024);
        DataDirectory = string.IsNullOrWhiteSpace(DataDirectory) ? "App_Data" : DataDirectory.Trim();
        BackupDirectory = string.IsNullOrWhiteSpace(BackupDirectory)
            ? Path.Combine(DataDirectory, "backups")
            : BackupDirectory.Trim();
        KeyDirectory = string.IsNullOrWhiteSpace(KeyDirectory) ? Path.Combine(DataDirectory, "keys") : KeyDirectory.Trim();
        AllowedSourceNetworks ??= new List<string>();
        AllowedSourceNetworks = AllowedSourceNetworks
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
