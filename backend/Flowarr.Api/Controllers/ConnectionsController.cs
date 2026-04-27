using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/connections")]
public sealed class ConnectionsController(IConnectionSettingsService connectionSettingsService) : ControllerBase
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
            radarr = new { configured = false, status = "placeholder" },
            sonarr = new { configured = false, status = "placeholder" },
            plex = new { configured = false, status = "placeholder" },
            jellyfin = new { configured = false, status = "placeholder" }
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
                settings.SftpSeedbox.RemoteRootPath,
                settings.SftpSeedbox.HasPassword,
                settings.SftpSeedbox.HasPrivateKeyPassphrase),
            settings.AutoDownload);
    }

    [HttpPut("qbittorrent")]
    public async Task<IActionResult> SaveQBittorrent(SaveQBittorrentConnectionRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SaveQBittorrentAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPut("sftp-seedbox")]
    public async Task<IActionResult> SaveSftpSeedbox(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SaveSftpSeedboxAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPut("auto-download")]
    public async Task<IActionResult> SaveAutoDownload(SaveAutoDownloadConnectionRequest request, CancellationToken cancellationToken)
    {
        await connectionSettingsService.SaveAutoDownloadAsync(request, cancellationToken);
        return NoContent();
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
}

public sealed record ConnectionSettingsResponse(
    QBittorrentConnectionResponse QBittorrent,
    SftpSeedboxConnectionResponse SftpSeedbox,
    AutoDownloadConnectionSettings AutoDownload);

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
    string RemoteRootPath,
    bool HasPassword,
    bool HasPrivateKeyPassphrase);
