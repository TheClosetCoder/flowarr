namespace Flowarr.Infrastructure.Options;

public sealed class IntegrationOptions
{
    public QBittorrentOptions QBittorrent { get; set; } = new();
    public RadarrOptions Radarr { get; set; } = new();
    public SonarrOptions Sonarr { get; set; } = new();
    public StorageScannerOptions Storage { get; set; } = new();
    public CleanupOptions Cleanup { get; set; } = new();
    public AutoDownloadOptions AutoDownload { get; set; } = new();
    public SftpSeedboxOptions SftpSeedbox { get; set; } = new();
}

public sealed class QBittorrentOptions
{
    public string? BaseUrl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class RadarrOptions
{
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
}

public sealed class SonarrOptions
{
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
}

public sealed class StorageScannerOptions
{
    public string LocalPath { get; set; } = "/media";
    public string SeedboxPathOrEndpoint { get; set; } = "/seedbox";
}

public sealed class CleanupOptions
{
    public bool DryRun { get; set; } = true;
    public bool AllowDelete { get; set; }
}

public sealed class AutoDownloadOptions
{
    public bool Enabled { get; set; }
    public string LocalImportPath { get; set; } = "/media/import";
    public bool OverwriteExisting { get; set; }
    public string[] AllowedExtensions { get; set; } = [".mkv", ".mp4", ".avi", ".mov", ".m4v", ".srt"];
}

public sealed class SftpSeedboxOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string RemoteRootPath { get; set; } = string.Empty;
}
