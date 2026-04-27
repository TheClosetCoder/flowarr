using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Renci.SshNet;

namespace Flowarr.Infrastructure.Services;

public interface IConnectionTestService
{
    Task<ConnectionTestResult> TestTorrentClientAsync(SaveQBittorrentConnectionRequest request, CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestSftpSeedboxAsync(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken = default);
}

public sealed class ConnectionTestService(IConnectionSettingsService connectionSettingsService) : IConnectionTestService
{
    public async Task<ConnectionTestResult> TestTorrentClientAsync(SaveQBittorrentConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await MergeTorrentSettingsAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return ConnectionTestResult.Failure("Torrent client base URL is required.");
        }

        try
        {
            return settings.ClientType.Equals("Deluge", StringComparison.OrdinalIgnoreCase)
                ? await TestDelugeAsync(settings, cancellationToken)
                : await TestQBittorrentAsync(settings, cancellationToken);
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Failure(ex.Message);
        }
    }

    public async Task<ConnectionTestResult> TestSftpSeedboxAsync(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await MergeSftpSettingsAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            return ConnectionTestResult.Failure("SFTP host is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.Username))
        {
            return ConnectionTestResult.Failure("SFTP username is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.Password) && string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
        {
            return ConnectionTestResult.Failure("SFTP password or private key path is required.");
        }

        try
        {
            return await Task.Run(() =>
            {
                using var client = CreateSftpClient(settings);
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
                client.Connect();

                var remotePath = string.IsNullOrWhiteSpace(settings.RemoteRootPath) ? "." : settings.RemoteRootPath;
                if (!client.Exists(remotePath))
                {
                    client.Disconnect();
                    return ConnectionTestResult.Failure($"Connected, but remote root path was not found: {remotePath}");
                }

                client.ListDirectory(remotePath).Take(1).ToList();
                client.Disconnect();
                return ConnectionTestResult.Success($"Connected to SFTP and listed {remotePath}.");
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Failure(ex.Message);
        }
    }

    private async Task<ConnectionTestResult> TestQBittorrentAsync(QBittorrentConnectionSettings settings, CancellationToken cancellationToken)
    {
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler { CookieContainer = cookies };
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        if (!string.IsNullOrWhiteSpace(settings.Username) || !string.IsNullOrWhiteSpace(settings.Password))
        {
            var loginContent = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("username", settings.Username),
                new KeyValuePair<string, string>("password", settings.Password)
            ]);

            var loginResponse = await httpClient.PostAsync("api/v2/auth/login", loginContent, cancellationToken);
            var loginBody = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!loginResponse.IsSuccessStatusCode || !loginBody.Contains("Ok.", StringComparison.OrdinalIgnoreCase))
            {
                return ConnectionTestResult.Failure($"qBittorrent login failed with status {(int)loginResponse.StatusCode}.");
            }
        }

        var torrentsResponse = await httpClient.GetAsync("api/v2/torrents/info", cancellationToken);
        if (!torrentsResponse.IsSuccessStatusCode)
        {
            return ConnectionTestResult.Failure(await FormatHttpFailureAsync("qBittorrent torrent list request failed", torrentsResponse, cancellationToken));
        }

        var torrents = await torrentsResponse.Content.ReadFromJsonAsync<List<QBittorrentTorrent>>(cancellationToken) ?? [];
        return ConnectionTestResult.Success($"Connected to qBittorrent. Found {torrents.Count} torrent(s).");
    }

    private static async Task<ConnectionTestResult> TestDelugeAsync(QBittorrentConnectionSettings settings, CancellationToken cancellationToken)
    {
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler { CookieContainer = cookies };
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        var login = await PostDelugeAsync<JsonElement>(httpClient, "auth.login", [settings.Password], cancellationToken);
        if (login.ValueKind != JsonValueKind.True)
        {
            return ConnectionTestResult.Failure("Deluge login failed.");
        }

        var result = await PostDelugeAsync<JsonElement>(
            httpClient,
            "web.update_ui",
            [new[] { "name" }, new Dictionary<string, string>()],
            cancellationToken);
        var count = result.TryGetProperty("torrents", out var torrents) && torrents.ValueKind == JsonValueKind.Object
            ? torrents.EnumerateObject().Count()
            : 0;

        return ConnectionTestResult.Success($"Connected to Deluge. Found {count} torrent(s).");
    }

    private static async Task<T> PostDelugeAsync<T>(HttpClient httpClient, string method, object[] parameters, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "json",
            new DelugeRequest(method, parameters, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await FormatHttpFailureAsync($"Deluge {method} failed", response, cancellationToken));
        }

        var payload = await response.Content.ReadFromJsonAsync<DelugeResponse<T>>(cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException("Deluge returned an empty response.");
        }

        if (payload.Error is not null)
        {
            throw new InvalidOperationException($"Deluge API error: {payload.Error}");
        }

        return payload.Result;
    }

    private static async Task<string> FormatHttpFailureAsync(string prefix, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"{prefix}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
        }

        return $"{prefix}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {body}";
    }

    private async Task<QBittorrentConnectionSettings> MergeTorrentSettingsAsync(SaveQBittorrentConnectionRequest request, CancellationToken cancellationToken)
    {
        var saved = await connectionSettingsService.GetQBittorrentAsync(cancellationToken);
        return new QBittorrentConnectionSettings(
            request.Enabled,
            string.IsNullOrWhiteSpace(request.ClientType) ? saved.ClientType : request.ClientType,
            string.IsNullOrWhiteSpace(request.BaseUrl) ? saved.BaseUrl : request.BaseUrl,
            request.Username ?? saved.Username,
            string.IsNullOrWhiteSpace(request.Password) && !request.ClearPassword ? saved.Password : request.Password ?? string.Empty,
            !string.IsNullOrWhiteSpace(request.Password) || (!request.ClearPassword && saved.HasPassword));
    }

    private async Task<SftpSeedboxConnectionSettings> MergeSftpSettingsAsync(SaveSftpSeedboxConnectionRequest request, CancellationToken cancellationToken)
    {
        var saved = await connectionSettingsService.GetSftpSeedboxAsync(cancellationToken);
        return new SftpSeedboxConnectionSettings(
            request.Enabled,
            string.IsNullOrWhiteSpace(request.Host) ? saved.Host : request.Host,
            request.Port <= 0 ? saved.Port : request.Port,
            string.IsNullOrWhiteSpace(request.Username) ? saved.Username : request.Username,
            string.IsNullOrWhiteSpace(request.Password) && !request.ClearPassword ? saved.Password : request.Password ?? string.Empty,
            string.IsNullOrWhiteSpace(request.PrivateKeyPath) ? saved.PrivateKeyPath : request.PrivateKeyPath,
            string.IsNullOrWhiteSpace(request.PrivateKeyPassphrase) && !request.ClearPrivateKeyPassphrase ? saved.PrivateKeyPassphrase : request.PrivateKeyPassphrase ?? string.Empty,
            string.IsNullOrWhiteSpace(request.RemoteRootPath) ? saved.RemoteRootPath : request.RemoteRootPath,
            !string.IsNullOrWhiteSpace(request.Password) || (!request.ClearPassword && saved.HasPassword),
            !string.IsNullOrWhiteSpace(request.PrivateKeyPassphrase) || (!request.ClearPrivateKeyPassphrase && saved.HasPrivateKeyPassphrase));
    }

    private static SftpClient CreateSftpClient(SftpSeedboxConnectionSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrWhiteSpace(settings.PrivateKeyPassphrase)
                ? new PrivateKeyFile(settings.PrivateKeyPath)
                : new PrivateKeyFile(settings.PrivateKeyPath, settings.PrivateKeyPassphrase);

            return new SftpClient(settings.Host, settings.Port, settings.Username, keyFile);
        }

        return new SftpClient(settings.Host, settings.Port, settings.Username, settings.Password);
    }

    private sealed record DelugeRequest(
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("params")] object[] Params,
        [property: JsonPropertyName("id")] long Id);

    private sealed record DelugeResponse<T>(
        [property: JsonPropertyName("result")] T Result,
        [property: JsonPropertyName("error")] JsonElement? Error,
        [property: JsonPropertyName("id")] long Id);

    private sealed class QBittorrentTorrent
    {
        [JsonPropertyName("hash")]
        public string? Hash { get; set; }
    }
}

public sealed record ConnectionTestResult(bool Ok, string Message)
{
    public static ConnectionTestResult Success(string message) => new(true, message);
    public static ConnectionTestResult Failure(string message) => new(false, message);
}
