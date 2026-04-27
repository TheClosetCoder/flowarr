using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flowarr.Core.Enums;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Integrations;

public sealed class QBittorrentDownloadClient(
    IConnectionSettingsService connectionSettingsService,
    MockDownloadClient mockDownloadClient,
    ILogger<QBittorrentDownloadClient> logger) : IDownloadClient
{
    public async Task<IReadOnlyCollection<DownloadSnapshot>> GetDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await connectionSettingsService.GetQBittorrentAsync(cancellationToken);
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return await mockDownloadClient.GetDownloadsAsync(cancellationToken);
        }

        if (settings.ClientType.Equals("Deluge", StringComparison.OrdinalIgnoreCase))
        {
            return await GetDelugeDownloadsAsync(settings, cancellationToken);
        }

        return await GetQBittorrentDownloadsAsync(settings, cancellationToken);
    }

    private async Task<IReadOnlyCollection<DownloadSnapshot>> GetQBittorrentDownloadsAsync(
        QBittorrentConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler { CookieContainer = cookies };
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/")
        };

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            var loginContent = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("username", settings.Username),
                new KeyValuePair<string, string>("password", settings.Password)
            ]);

            var loginResponse = await httpClient.PostAsync("api/v2/auth/login", loginContent, cancellationToken);
            var loginBody = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!loginResponse.IsSuccessStatusCode || !loginBody.Contains("Ok.", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("qBittorrent login failed with status {StatusCode}", loginResponse.StatusCode);
                return [];
            }
        }

        var torrents = await httpClient.GetFromJsonAsync<List<QBittorrentTorrent>>("api/v2/torrents/info", cancellationToken)
            ?? [];

        return torrents.Select(torrent => new DownloadSnapshot(
                "qBittorrent",
                torrent.Name,
                torrent.Hash,
                MapState(torrent.State, torrent.Progress),
                Convert.ToDecimal(torrent.Progress, CultureInfo.InvariantCulture),
                torrent.Size,
                string.IsNullOrWhiteSpace(torrent.ContentPath) ? torrent.SavePath : torrent.ContentPath))
            .ToList();
    }

    private async Task<IReadOnlyCollection<DownloadSnapshot>> GetDelugeDownloadsAsync(
        QBittorrentConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler { CookieContainer = cookies };
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/")
        };

        var login = await PostDelugeAsync<JsonElement>(
            httpClient,
            "auth.login",
            [settings.Password],
            cancellationToken);

        if (login.ValueKind != JsonValueKind.True)
        {
            logger.LogWarning("Deluge login failed");
            return [];
        }

        var result = await PostDelugeAsync<JsonElement>(
            httpClient,
            "web.update_ui",
            [
                new[]
                {
                    "name",
                    "state",
                    "progress",
                    "total_size",
                    "download_location",
                    "save_path"
                },
                new Dictionary<string, string>()
            ],
            cancellationToken);

        if (!result.TryGetProperty("torrents", out var torrents) || torrents.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var downloads = new List<DownloadSnapshot>();
        foreach (var torrent in torrents.EnumerateObject())
        {
            var value = torrent.Value;
            var name = GetString(value, "name") ?? torrent.Name;
            var progress = GetDouble(value, "progress") / 100d;
            var state = GetString(value, "state");
            var savePath = GetString(value, "download_location") ?? GetString(value, "save_path");

            downloads.Add(new DownloadSnapshot(
                "Deluge",
                name,
                torrent.Name,
                MapDelugeState(state, progress),
                Convert.ToDecimal(progress, CultureInfo.InvariantCulture),
                GetLong(value, "total_size"),
                string.IsNullOrWhiteSpace(savePath) ? null : $"{savePath.TrimEnd('/')}/{name}"));
        }

        return downloads;
    }

    private static async Task<T> PostDelugeAsync<T>(
        HttpClient httpClient,
        string method,
        object[] parameters,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync(
            "json",
            new DelugeRequest(method, parameters, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken);
        response.EnsureSuccessStatusCode();

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

    private static DownloadStatus MapState(string? state, double progress)
    {
        if (progress >= 1 && state is "uploading" or "stalledUP" or "forcedUP" or "queuedUP")
        {
            return DownloadStatus.Seeding;
        }

        if (progress >= 1)
        {
            return DownloadStatus.Complete;
        }

        return state switch
        {
            "downloading" or "stalledDL" or "forcedDL" or "queuedDL" => DownloadStatus.Downloading,
            "pausedDL" or "pausedUP" => DownloadStatus.Paused,
            "error" or "missingFiles" => DownloadStatus.Error,
            _ => DownloadStatus.Unknown
        };
    }

    private static DownloadStatus MapDelugeState(string? state, double progress)
    {
        if (progress >= 1 && state is "Seeding")
        {
            return DownloadStatus.Seeding;
        }

        if (progress >= 1)
        {
            return DownloadStatus.Complete;
        }

        return state switch
        {
            "Downloading" => DownloadStatus.Downloading,
            "Paused" => DownloadStatus.Paused,
            "Error" => DownloadStatus.Error,
            _ => DownloadStatus.Unknown
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value) ? value : 0;
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value) ? value : 0;
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

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("progress")]
        public double Progress { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("save_path")]
        public string? SavePath { get; set; }

        [JsonPropertyName("content_path")]
        public string? ContentPath { get; set; }
    }
}
