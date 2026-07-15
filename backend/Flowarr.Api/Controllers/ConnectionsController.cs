using System.Text.Json.Serialization;
using Flowarr.Infrastructure.Integrations;
using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/connections")]
public sealed class ConnectionsController(
    IConnectionSettingsService connectionSettingsService,
    IConnectionTestService connectionTestService,
    ArrMediaCatalogClient arrMediaCatalogClient,
    PlexClient plexClient) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var value = await connectionSettingsService.GetSnapshotAsync(cancellationToken);
        return Ok(new
        {
            qBittorrent = new { configured = value.QBittorrent.Enabled && HasValue(value.QBittorrent.BaseUrl), value.QBittorrent.BaseUrl },
            sftpSeedbox = new
            {
                configured = value.SftpSeedbox.Enabled &&
                             HasValue(value.SftpSeedbox.Host) &&
                             HasValue(value.SftpSeedbox.Username) &&
                             (HasValue(value.SftpSeedbox.Password) || HasValue(value.SftpSeedbox.PrivateKeyPath)),
                value.SftpSeedbox.Host,
                value.SftpSeedbox.Port
            },
            radarr = new { configured = value.Radarr.Enabled && HasValue(value.Radarr.BaseUrl) && value.Radarr.HasApiKey, status = "configured" },
            sonarr = new { configured = value.Sonarr.Enabled && HasValue(value.Sonarr.BaseUrl) && value.Sonarr.HasApiKey, status = "configured" },
            plex = new { configured = value.Plex.Enabled && HasValue(value.Plex.BaseUrl) && value.Plex.HasApiKey, status = value.Plex.Enabled && HasValue(value.Plex.BaseUrl) && value.Plex.HasApiKey ? "configured" : "not_configured" },
            jellyfin = new { configured = false, status = "not_configured" }
        });
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ConnectionSettingsResponse>> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await connectionSettingsService.GetSnapshotAsync(cancellationToken);
        return new ConnectionSettingsResponse(
            new QBittorrentConnectionResponse(
                settings.QBittorrent.Enabled,
                settings.QBittorrent.ClientType,
                settings.QBittorrent.BaseUrl,
                settings.QBittorrent.Username,
                settings.QBittorrent.HasPassword),
            new SftpSeedboxConnectionResponse(
                settings.SftpSeedbox.Enabled,
                settings.SftpSeedbox.Host,
                settings.SftpSeedbox.Port,
                settings.SftpSeedbox.Username,
                settings.SftpSeedbox.PrivateKeyPath,
                settings.SftpSeedbox.SeedboxRoots,
                settings.SftpSeedbox.HasPassword,
                settings.SftpSeedbox.HasPrivateKeyPassphrase,
                settings.SftpSeedbox.AccountQuotaBytes),
            settings.AutoDownload,
            new ArrConnectionResponse(settings.Radarr.Enabled, settings.Radarr.BaseUrl, settings.Radarr.HasApiKey),
            new ArrConnectionResponse(settings.Sonarr.Enabled, settings.Sonarr.BaseUrl, settings.Sonarr.HasApiKey),
            new ArrConnectionResponse(settings.Plex.Enabled, settings.Plex.BaseUrl, settings.Plex.HasApiKey));
    }

    [HttpPut("qbittorrent")]
    public async Task<IActionResult> SaveQBittorrent(SaveQBittorrentConnectionRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SaveQBittorrentAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("qbittorrent/test")]
    public async Task<ActionResult<ConnectionTestResult>> TestQBittorrent(SaveQBittorrentConnectionRequest request, CancellationToken cancellationToken)
    {
        return await connectionTestService.TestTorrentClientAsync(request, cancellationToken);
    }

    [HttpPut("sftp-seedbox")]
    public async Task<IActionResult> SaveSftpSeedbox(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SaveSftpSeedboxAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("sftp-seedbox/test")]
    public async Task<ActionResult<ConnectionTestResult>> TestSftpSeedbox(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken)
    {
        return await connectionTestService.TestSftpSeedboxAsync(request, cancellationToken);
    }

    [HttpPut("auto-download")]
    public async Task<IActionResult> SaveAutoDownload(SaveAutoDownloadConnectionRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SaveAutoDownloadAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPut("radarr")]
    public async Task<IActionResult> SaveRadarr(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SaveRadarrAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPut("sonarr")]
    public async Task<IActionResult> SaveSonarr(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SaveSonarrAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("radarr/test")]
    public Task<ConnectionTestResult> TestRadarr(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        return arrMediaCatalogClient.TestAsync("Radarr", request, cancellationToken);
    }

    [HttpPost("sonarr/test")]
    public Task<ConnectionTestResult> TestSonarr(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        return arrMediaCatalogClient.TestAsync("Sonarr", request, cancellationToken);
    }

    [HttpPut("plex")]
    public async Task<IActionResult> SavePlex(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SavePlexAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("plex/test")]
    public Task<ConnectionTestResult> TestPlex(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        return plexClient.TestAsync(request, cancellationToken);
    }

    [HttpGet("{system:regex(^radarr|sonarr$)}/root-folders")]
    public async Task<IActionResult> GetArrRootFolders(string system, CancellationToken cancellationToken)
    {
        var folders = await arrMediaCatalogClient.GetRootFoldersAsync(system, cancellationToken);
        return Ok(folders);
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
}

public sealed record ConnectionSettingsResponse(
    [property: JsonPropertyName("qbittorrent")]
    QBittorrentConnectionResponse QBittorrent,
    [property: JsonPropertyName("sftpSeedbox")]
    SftpSeedboxConnectionResponse SftpSeedbox,
    [property: JsonPropertyName("autoDownload")]
    AutoDownloadConnectionSettings AutoDownload,
    [property: JsonPropertyName("radarr")]
    ArrConnectionResponse Radarr,
    [property: JsonPropertyName("sonarr")]
    ArrConnectionResponse Sonarr,
    [property: JsonPropertyName("plex")]
    ArrConnectionResponse Plex);

public sealed record QBittorrentConnectionResponse(
    bool Enabled,
    string ClientType,
    string BaseUrl,
    string Username,
    bool HasPassword);

public sealed record SftpSeedboxConnectionResponse(
    bool Enabled,
    string Host,
    int Port,
    string Username,
    string PrivateKeyPath,
    SeedboxRootSettings[] SeedboxRoots,
    bool HasPassword,
    bool HasPrivateKeyPassphrase,
    long AccountQuotaBytes);

public sealed record ArrConnectionResponse(
    bool Enabled,
    string BaseUrl,
    bool HasApiKey);
