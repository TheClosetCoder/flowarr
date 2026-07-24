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
    Task<ArrConnectionSettings> GetRadarrAsync(CancellationToken cancellationToken = default);
    Task<ArrConnectionSettings> GetSonarrAsync(CancellationToken cancellationToken = default);
    Task<ArrConnectionSettings> GetPlexAsync(CancellationToken cancellationToken = default);
    Task<ArrConnectionSettings> GetJellyfinAsync(CancellationToken cancellationToken = default);
    Task<CleanupSettings> GetCleanupSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveQBittorrentAsync(SaveQBittorrentConnectionRequest request, CancellationToken cancellationToken = default);
    Task SaveSftpSeedboxAsync(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken = default);
    Task SaveAutoDownloadAsync(SaveAutoDownloadConnectionRequest request, CancellationToken cancellationToken = default);
    Task SaveRadarrAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken = default);
    Task SaveSonarrAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken = default);
    Task SavePlexAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken = default);
    Task SaveJellyfinAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken = default);
    Task SaveCleanupSettingsAsync(SaveCleanupSettingsRequest request, CancellationToken cancellationToken = default);
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
        var radarr = await GetRadarrAsync(cancellationToken);
        var sonarr = await GetSonarrAsync(cancellationToken);
        var plex = await GetPlexAsync(cancellationToken);
        var jellyfin = await GetJellyfinAsync(cancellationToken);
        return new ConnectionSettingsSnapshot(qbittorrent, sftpSeedbox, autoDownload, radarr, sonarr, plex, jellyfin);
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
            GetSeedboxRoots(settings),
            !string.IsNullOrWhiteSpace(Get(secrets, "password", options.Value.SftpSeedbox.Password)),
            !string.IsNullOrWhiteSpace(Get(secrets, "privateKeyPassphrase", string.Empty)),
            GetLong(settings, "accountQuotaBytes", 0));
    }

    public async Task<AutoDownloadConnectionSettings> GetAutoDownloadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await GetConnectionAsync("autoDownload", cancellationToken);
        var settings = ReadSettings(saved);
        return new AutoDownloadConnectionSettings(
            GetBool(settings, "enabled", options.Value.AutoDownload.Enabled),
            Get(settings, "localImportPath", options.Value.AutoDownload.LocalImportPath),
            GetBool(settings, "overwriteExisting", options.Value.AutoDownload.OverwriteExisting),
            GetStringArray(settings, "allowedExtensions", options.Value.AutoDownload.AllowedExtensions),
            GetLocalMediaPaths(settings, options.Value.Storage.LocalPath),
            GetTransferRules(settings, options.Value.AutoDownload.LocalImportPath));
    }

    public Task<ArrConnectionSettings> GetRadarrAsync(CancellationToken cancellationToken = default)
    {
        return GetArrAsync("radarr", "Radarr", options.Value.Radarr.BaseUrl, options.Value.Radarr.ApiKey, cancellationToken);
    }

    public Task<ArrConnectionSettings> GetPlexAsync(CancellationToken cancellationToken = default)
    {
        return GetArrAsync("plex", "Plex", string.Empty, string.Empty, cancellationToken);
    }

    public Task<ArrConnectionSettings> GetJellyfinAsync(CancellationToken cancellationToken = default)
    {
        return GetArrAsync("jellyfin", "Jellyfin", string.Empty, string.Empty, cancellationToken);
    }

    public async Task<CleanupSettings> GetCleanupSettingsAsync(CancellationToken cancellationToken = default)
    {
        var saved = await GetConnectionAsync("cleanupSettings", cancellationToken);
        var settings = ReadSettings(saved);
        return new CleanupSettings(
            GetInt(settings, "staleAfterDays", 90),
            GetBool(settings, "autoGenerateEnabled", false),
            GetInt(settings, "autoGenerateIntervalHours", 24));
    }

    public Task<ArrConnectionSettings> GetSonarrAsync(CancellationToken cancellationToken = default)
    {
        return GetArrAsync("sonarr", "Sonarr", options.Value.Sonarr.BaseUrl, options.Value.Sonarr.ApiKey, cancellationToken);
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
                ["baseUrl"] = request.BaseUrl ?? string.Empty,
                ["username"] = request.Username ?? string.Empty
            },
            secrets,
            cancellationToken);
    }

    public async Task SaveSftpSeedboxAsync(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await GetConnectionAsync("sftpSeedbox", cancellationToken);
        var previousRoots = GetSeedboxRoots(ReadSettings(existing));
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
                ["host"] = request.Host ?? string.Empty,
                ["port"] = request.Port.ToString(),
                ["username"] = request.Username ?? string.Empty,
                ["privateKeyPath"] = request.PrivateKeyPath ?? string.Empty,
                ["seedboxRoots"] = JsonSerializer.Serialize(request.SeedboxRoots ?? [], JsonOptions),
                ["accountQuotaBytes"] = request.AccountQuotaBytes.ToString()
            },
            secrets,
            cancellationToken);

        await MigrateTransferRuleRootPathsAsync(previousRoots, request.SeedboxRoots ?? [], cancellationToken);
    }

    /// <summary>
    /// Transfer rules pin themselves to a seedbox root by its literal Path at the time it was picked,
    /// not by a stable reference. If a root's Path is edited afterward (e.g. broadened for the account
    /// quota feature), every rule pointing at the old Path would otherwise silently stop matching
    /// anything, with no indication why - roots are matched across the edit by (Name, Kind) since Path
    /// is the thing that can change, and any rule pinned to an old Path is carried forward to the new one.
    /// </summary>
    private async Task MigrateTransferRuleRootPathsAsync(
        SeedboxRootSettings[] previousRoots,
        SeedboxRootSettings[] newRoots,
        CancellationToken cancellationToken)
    {
        var pathChanges = previousRoots
            .Join(newRoots,
                previous => (previous.Name, previous.Kind),
                current => (current.Name, current.Kind),
                (previous, current) => (OldPath: previous.Path, NewPath: current.Path))
            .Where(change => !string.IsNullOrWhiteSpace(change.OldPath) && !change.OldPath.Equals(change.NewPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pathChanges.Count == 0)
        {
            return;
        }

        var autoDownload = await GetAutoDownloadAsync(cancellationToken);
        var changed = false;
        var updatedRules = autoDownload.TransferRules
            .Select(rule =>
            {
                var change = pathChanges.FirstOrDefault(item => item.OldPath.Equals(rule.SourceRootPath, StringComparison.OrdinalIgnoreCase));
                if (change.OldPath is null)
                {
                    return rule;
                }

                changed = true;
                return rule with { SourceRootPath = change.NewPath };
            })
            .ToArray();

        if (!changed)
        {
            return;
        }

        await SaveAutoDownloadAsync(
            new SaveAutoDownloadConnectionRequest(
                autoDownload.Enabled,
                autoDownload.LocalImportPath,
                autoDownload.OverwriteExisting,
                autoDownload.AllowedExtensions,
                autoDownload.LocalMediaPaths,
                updatedRules),
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
                ["allowedExtensions"] = string.Join(',', request.AllowedExtensions ?? []),
                ["localMediaPaths"] = JsonSerializer.Serialize(request.LocalMediaPaths ?? [], JsonOptions),
                ["transferRules"] = JsonSerializer.Serialize(request.TransferRules ?? [], JsonOptions)
            },
            [],
            cancellationToken);
    }

    public Task SaveRadarrAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken = default)
    {
        return SaveArrAsync("radarr", "Radarr", request, cancellationToken);
    }

    public Task SavePlexAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken = default)
    {
        return SaveArrAsync("plex", "Plex", request, cancellationToken);
    }

    public Task SaveJellyfinAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken = default)
    {
        return SaveArrAsync("jellyfin", "Jellyfin", request, cancellationToken);
    }

    public Task SaveCleanupSettingsAsync(SaveCleanupSettingsRequest request, CancellationToken cancellationToken = default)
    {
        return SaveConnectionAsync(
            "cleanupSettings",
            "Cleanup",
            true,
            new Dictionary<string, string?>
            {
                ["staleAfterDays"] = request.StaleAfterDays.ToString(),
                ["autoGenerateEnabled"] = request.AutoGenerateEnabled.ToString(),
                ["autoGenerateIntervalHours"] = request.AutoGenerateIntervalHours.ToString()
            },
            [],
            cancellationToken);
    }

    public Task SaveSonarrAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken = default)
    {
        return SaveArrAsync("sonarr", "Sonarr", request, cancellationToken);
    }

    private async Task<ArrConnectionSettings> GetArrAsync(
        string key,
        string displayName,
        string? fallbackBaseUrl,
        string? fallbackApiKey,
        CancellationToken cancellationToken)
    {
        var saved = await GetConnectionAsync(key, cancellationToken);
        var settings = ReadSettings(saved);
        var secrets = ReadSecrets(saved);
        var configured = saved?.Enabled ?? !string.IsNullOrWhiteSpace(fallbackBaseUrl);
        var apiKey = Get(secrets, "apiKey", fallbackApiKey);
        return new ArrConnectionSettings(
            configured,
            displayName,
            Get(settings, "baseUrl", fallbackBaseUrl),
            !string.IsNullOrWhiteSpace(apiKey),
            apiKey);
    }

    private async Task SaveArrAsync(string key, string displayName, SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        var existing = await GetConnectionAsync(key, cancellationToken);
        var secrets = ReadSecrets(existing);
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            secrets["apiKey"] = request.ApiKey;
        }
        else if (request.ClearApiKey)
        {
            secrets.Remove("apiKey");
        }

        await SaveConnectionAsync(
            key,
            displayName,
            request.Enabled,
            new Dictionary<string, string?>
            {
                ["baseUrl"] = request.BaseUrl ?? string.Empty
            },
            secrets,
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

    private static long GetLong(IReadOnlyDictionary<string, string?> values, string key, long fallback)
    {
        return values.TryGetValue(key, out var value) && long.TryParse(value, out var parsed) ? parsed : fallback;
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

    private static TransferRuleSettings[] GetTransferRules(IReadOnlyDictionary<string, string?> values, string fallbackLocalImportPath)
    {
        if (values.TryGetValue("transferRules", out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return JsonSerializer.Deserialize<TransferRuleSettings[]>(value, JsonOptions) ?? [];
        }

        return string.IsNullOrWhiteSpace(fallbackLocalImportPath)
            ? []
            : [new TransferRuleSettings(true, "Default", string.Empty, fallbackLocalImportPath, "Download", string.Empty)];
    }

    private static LocalMediaPathSettings[] GetLocalMediaPaths(IReadOnlyDictionary<string, string?> values, string fallbackLocalPath)
    {
        if (values.TryGetValue("localMediaPaths", out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return JsonSerializer.Deserialize<LocalMediaPathSettings[]>(value, JsonOptions) ?? [];
        }

        return string.IsNullOrWhiteSpace(fallbackLocalPath)
            ? []
            : [new LocalMediaPathSettings(true, "Media", fallbackLocalPath, "Mixed")];
    }

    private static SeedboxRootSettings[] GetSeedboxRoots(IReadOnlyDictionary<string, string?> values)
    {
        if (values.TryGetValue("seedboxRoots", out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return JsonSerializer.Deserialize<SeedboxRootSettings[]>(value, JsonOptions) ?? [];
        }

        return [];
    }
}

public sealed record ConnectionSettingsSnapshot(
    QBittorrentConnectionSettings QBittorrent,
    SftpSeedboxConnectionSettings SftpSeedbox,
    AutoDownloadConnectionSettings AutoDownload,
    ArrConnectionSettings Radarr,
    ArrConnectionSettings Sonarr,
    ArrConnectionSettings Plex,
    ArrConnectionSettings Jellyfin);

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
    SeedboxRootSettings[] SeedboxRoots,
    bool HasPassword,
    bool HasPrivateKeyPassphrase,
    long AccountQuotaBytes);

public sealed record ArrConnectionSettings(
    bool Enabled,
    string DisplayName,
    string BaseUrl,
    bool HasApiKey,
    string ApiKey);

public sealed record AutoDownloadConnectionSettings(
    bool Enabled,
    string LocalImportPath,
    bool OverwriteExisting,
    string[] AllowedExtensions,
    LocalMediaPathSettings[] LocalMediaPaths,
    TransferRuleSettings[] TransferRules);

public sealed record LocalMediaPathSettings(
    bool Enabled,
    string Name,
    string Path,
    string Role);

public sealed record TransferRuleSettings(
    bool Enabled,
    string Name,
    string RemotePathPrefix,
    string LocalPath,
    string SourceRootKind = "Download",
    string SourceRootPath = "",
    bool DeleteRemoteAfterDownload = false);

public sealed record SeedboxRootSettings(
    bool Enabled,
    string Name,
    string Kind,
    string Path);

public sealed record SaveQBittorrentConnectionRequest(
    bool Enabled,
    string? ClientType,
    string? BaseUrl,
    string? Username,
    string? Password,
    bool ClearPassword);

public sealed record SaveSftpSeedboxConnectionRequest(
    bool Enabled,
    string? Host,
    int Port,
    string? Username,
    string? Password,
    bool ClearPassword,
    string? PrivateKeyPath,
    string? PrivateKeyPassphrase,
    bool ClearPrivateKeyPassphrase,
    SeedboxRootSettings[]? SeedboxRoots,
    long AccountQuotaBytes);

public sealed record SaveAutoDownloadConnectionRequest(
    bool Enabled,
    string? LocalImportPath,
    bool OverwriteExisting,
    string[]? AllowedExtensions,
    LocalMediaPathSettings[]? LocalMediaPaths,
    TransferRuleSettings[]? TransferRules);

public sealed record SaveArrConnectionRequest(
    bool Enabled,
    string? BaseUrl,
    string? ApiKey,
    bool ClearApiKey);

public sealed record CleanupSettings(int StaleAfterDays, bool AutoGenerateEnabled, int AutoGenerateIntervalHours);

public sealed record SaveCleanupSettingsRequest(int StaleAfterDays, bool AutoGenerateEnabled = false, int AutoGenerateIntervalHours = 24);
