using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flowarr.Core.Enums;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flowarr.Infrastructure.Integrations;

public sealed class QBittorrentDownloadClient(
    IOptions<IntegrationOptions> options,
    ILogger<QBittorrentDownloadClient> logger) : IDownloadClient
{
    public async Task<IReadOnlyCollection<DownloadSnapshot>> GetDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value.QBittorrent;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return [];
        }

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
                new KeyValuePair<string, string>("password", settings.Password ?? string.Empty)
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
