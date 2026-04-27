using System.Text.Json;
using Flowarr.Core.Entities;
using Flowarr.Infrastructure.Data;
using Flowarr.Infrastructure.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flowarr.Infrastructure.Services;

public interface IConnectionSettingsService
{
    Task<ConnectionSettingsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<QBittorrentConnectionSettings> GetQBittorrentAsync(CancellationToken cancellationToken = default);
    Task<SftpSeedboxConnectionSettings> GetSftpSeedboxAsync(CancellationToken cancellationToken = default);
    Task<AutoDownloadConnectionSettings> GetAutoDownloadAsync(CancellationToken cancellationToken = default);
    Task SaveQBittorrentAsync(SaveQBittorrentConnectionRequest request, CancellationToken cancellationToken = default);
    Task SaveSftpSeedboxAsync(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken = default);
    Task SaveAutoDownloadAsync(SaveAutoDownloadConnectionRequest request, CancellationToken cancellationToken = default);
}

public sealed class ConnectionSettingsService(
    FlowarrDbContext dbContext,
    IOptions<IntegrationOptions> options,
    IDataProtectionProvider dataProtectionProvider) : IConnectionSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("Flowarr.ConnectionSecrets.v1");

    public async Task<ConnectionSettingsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var qbittorrent = await GetQBittorrentAsync(cancellationToken);
        var sftpSeedbox = await GetSftpSeedboxAsync(cancellationToken);
        var autoDownload = await GetAutoDownloadAsync(cancellationToken);
        return new ConnectionSettingsSnapshot(qbittorrent, sftpSeedbox, autoDownload);
    }

    public async Task<QBittorrentConnectionSettings> GetQBittorrentAsync(CancellationToken cancellationToken = default)
    {
        var saved = await GetConnectionAsync("qbittorrent", cancellationToken);
        var settings = ReadSettings(saved);
        var secrets = ReadSecrets(saved);
        var configured = saved?.Enabled ?? !string.IsNullOrWhiteSpace(options.Value.QBittorrent.BaseUrl);

        return new QBittorrentConnectionSettings(
            configured,
            Get(settings, "clientType", "qBittorrent"),
            Get(settings, "baseUrl", options.Value.QBittorrent.BaseUrl),
            Get(settings, "username", options.Value.QBittorrent.Username),
            Get(secrets, "password", options.Value.QBittorrent.Password),
            !string.IsNullOrWhiteSpace(Get(secrets, "password", options.Value.QBittorrent.Password)));
    }

    public async Task<SftpSeedboxConnectionSettings> GetSftpSeedboxAsync(CancellationToken cancellationToken = default)
    {
        var saved = await GetConnectionAsync("sftpSeedbox", cancellationToken);
        var settings = ReadSettings(saved);
        var secrets = ReadSecrets(saved);
        var configured = saved?.Enabled ?? !string.IsNullOrWhiteSpace(options.Value.SftpSeedbox.Host);

        return new SftpSeedboxConnectionSettings(
            configured,
            Get(settings, "host", options.Value.SftpSeedbox.Host),
            GetInt(settings, "port", options.Value.SftpSeedbox.Port),
            Get(settings, "username", options.Value.SftpSeedbox.Username),
            Get(secrets, "password", options.Value.SftpSeedbox.Password),
            Get(settings, "privateKeyPath", options.Value.SftpSeedbox.PrivateKeyPath),
            Get(secrets, "privateKeyPassphrase", string.Empty),
            Get(settings, "remoteRootPath", options.Value.SftpSeedbox.RemoteRootPath),
            !string.IsNullOrWhiteSpace(Get(secrets, "password", options.Value.SftpSeedbox.Password)),
            !string.IsNullOrWhiteSpace(Get(secrets, "privateKeyPassphrase", string.Empty)));
    }

    public async Task<AutoDownloadConnectionSettings> GetAutoDownloadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await GetConnectionAsync("autoDownload", cancellationToken);
        var settings = ReadSettings(saved);
        return new AutoDownloadConnectionSettings(
            GetBool(settings, "enabled", options.Value.AutoDownload.Enabled),
            Get(settings, "localImportPath", options.Value.AutoDownload.LocalImportPath),
            GetBool(settings, "overwriteExisting", options.Value.AutoDownload.OverwriteExisting),
            GetStringArray(settings, "allowedExtensions", options.Value.AutoDownload.AllowedExtensions));
    }

    public async Task SaveQBittorrentAsync(SaveQBittorrentConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await GetConnectionAsync("qbittorrent", cancellationToken);
        var secrets = ReadSecrets(existing);
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            secrets["password"] = request.Password;
        }
        else if (request.ClearPassword)
        {
            secrets.Remove("password");
        }

        await SaveConnectionAsync(
            "qbittorrent",
            "Torrent Client",
            request.Enabled,
            new Dictionary<string, string?>
            {
                ["clientType"] = string.IsNullOrWhiteSpace(request.ClientType) ? "qBittorrent" : request.ClientType,
                ["baseUrl"] = request.BaseUrl,
                ["username"] = request.Username ?? string.Empty
            },
            secrets,
            cancellationToken);
    }

    public async Task SaveSftpSeedboxAsync(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await GetConnectionAsync("sftpSeedbox", cancellationToken);
        var secrets = ReadSecrets(existing);
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            secrets["password"] = request.Password;
        }
        else if (request.ClearPassword)
        {
            secrets.Remove("password");
        }

        if (!string.IsNullOrWhiteSpace(request.PrivateKeyPassphrase))
        {
            secrets["privateKeyPassphrase"] = request.PrivateKeyPassphrase;
        }
        else if (request.ClearPrivateKeyPassphrase)
        {
            secrets.Remove("privateKeyPassphrase");
        }

        await SaveConnectionAsync(
            "sftpSeedbox",
            "SFTP Seedbox",
            request.Enabled,
            new Dictionary<string, string?>
            {
                ["host"] = request.Host,
                ["port"] = request.Port.ToString(),
                ["username"] = request.Username,
                ["privateKeyPath"] = request.PrivateKeyPath,
                ["remoteRootPath"] = request.RemoteRootPath
            },
            secrets,
            cancellationToken);
    }

    public Task SaveAutoDownloadAsync(SaveAutoDownloadConnectionRequest request, CancellationToken cancellationToken = default)
    {
        return SaveConnectionAsync(
            "autoDownload",
            "Auto Download",
            true,
            new Dictionary<string, string?>
            {
                ["enabled"] = request.Enabled.ToString(),
                ["localImportPath"] = request.LocalImportPath,
                ["overwriteExisting"] = request.OverwriteExisting.ToString(),
                ["allowedExtensions"] = string.Join(',', request.AllowedExtensions ?? [])
            },
            [],
            cancellationToken);
    }

    private Task<IntegrationConnection?> GetConnectionAsync(string key, CancellationToken cancellationToken)
    {
        return dbContext.IntegrationConnections.FirstOrDefaultAsync(connection => connection.Key == key, cancellationToken);
    }

    private async Task SaveConnectionAsync(
        string key,
        string displayName,
        bool enabled,
        Dictionary<string, string?> settings,
        Dictionary<string, string?> secrets,
        CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(key, cancellationToken);
        if (connection is null)
        {
            connection = new IntegrationConnection { Key = key, DisplayName = displayName };
            dbContext.IntegrationConnections.Add(connection);
        }

        connection.Enabled = enabled;
        connection.DisplayName = displayName;
        connection.SettingsJson = JsonSerializer.Serialize(settings, JsonOptions);
        connection.SecretsJson = _protector.Protect(JsonSerializer.Serialize(secrets, JsonOptions));
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Dictionary<string, string?> ReadSettings(IntegrationConnection? connection)
    {
        if (connection is null || string.IsNullOrWhiteSpace(connection.SettingsJson))
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<string, string?>>(connection.SettingsJson, JsonOptions) ?? [];
    }

    private Dictionary<string, string?> ReadSecrets(IntegrationConnection? connection)
    {
        if (connection is null || string.IsNullOrWhiteSpace(connection.SecretsJson))
        {
            return [];
        }

        try
        {
            var json = _protector.Unprotect(connection.SecretsJson);
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string Get(IReadOnlyDictionary<string, string?> values, string key, string? fallback)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback ?? string.Empty;
    }

    private static int GetInt(IReadOnlyDictionary<string, string?> values, string key, int fallback)
    {
        return values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static bool GetBool(IReadOnlyDictionary<string, string?> values, string key, bool fallback)
    {
        return values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string[] GetStringArray(IReadOnlyDictionary<string, string?> values, string key, string[] fallback)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : fallback;
    }
}

public sealed record ConnectionSettingsSnapshot(
    QBittorrentConnectionSettings QBittorrent,
    SftpSeedboxConnectionSettings SftpSeedbox,
    AutoDownloadConnectionSettings AutoDownload);

public sealed record QBittorrentConnectionSettings(
    bool Enabled,
    string ClientType,
    string BaseUrl,
    string Username,
    string Password,
    bool HasPassword);

public sealed record SftpSeedboxConnectionSettings(
    bool Enabled,
    string Host,
    int Port,
    string Username,
    string Password,
    string PrivateKeyPath,
    string PrivateKeyPassphrase,
    string RemoteRootPath,
    bool HasPassword,
    bool HasPrivateKeyPassphrase);

public sealed record AutoDownloadConnectionSettings(
    bool Enabled,
    string LocalImportPath,
    bool OverwriteExisting,
    string[] AllowedExtensions);

public sealed record SaveQBittorrentConnectionRequest(
    bool Enabled,
    string ClientType,
    string BaseUrl,
    string? Username,
    string? Password,
    bool ClearPassword);

public sealed record SaveSftpSeedboxConnectionRequest(
    bool Enabled,
    string Host,
    int Port,
    string Username,
    string? Password,
    bool ClearPassword,
    string PrivateKeyPath,
    string? PrivateKeyPassphrase,
    bool ClearPrivateKeyPassphrase,
    string RemoteRootPath);

public sealed record SaveAutoDownloadConnectionRequest(
    bool Enabled,
    string LocalImportPath,
    bool OverwriteExisting,
    string[] AllowedExtensions);
