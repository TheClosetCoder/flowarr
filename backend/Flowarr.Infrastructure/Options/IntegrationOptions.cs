namespace Flowarr.Infrastructure.Options;

public sealed class IntegrationOptions
{
    public QBittorrentOptions QBittorrent { get; set; } = new();
    public RadarrOptions Radarr { get; set; } = new();
    public StorageScannerOptions Storage { get; set; } = new();
    public CleanupOptions Cleanup { get; set; } = new();
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

public sealed class StorageScannerOptions
{
    public string LocalPath { get; set; } = "/media";
    public string SeedboxPathOrEndpoint { get; set; } = "/seedbox";
}

public sealed class CleanupOptions
{
    public bool DryRun { get; set; } = true;
}
