using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Integrations;

public sealed class JellyfinClient(
    IConnectionSettingsService connectionSettingsService,
    ILogger<JellyfinClient> logger)
{
    private const int PageSize = 500;

    public async Task<ConnectionTestResult> TestAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await MergeSettingsAsync(request, cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                return ConnectionTestResult.Failure("Jellyfin base URL is required.");
            }

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                return ConnectionTestResult.Failure("Jellyfin API key is required.");
            }

            using var client = CreateHttpClient(settings);
            var response = await client.GetAsync("System/Info", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return ConnectionTestResult.Failure($"Jellyfin test failed: HTTP {(int)response.StatusCode}. {body}");
            }

            var info = await response.Content.ReadFromJsonAsync<JellyfinSystemInfo>(cancellationToken);
            return ConnectionTestResult.Success(string.IsNullOrWhiteSpace(info?.ServerName)
                ? "Connected to Jellyfin."
                : $"Connected to Jellyfin \"{info.ServerName}\" ({info.Version}).");
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Failure(ex.Message);
        }
    }

    public async Task<IReadOnlyCollection<MediaLibraryItem>> GetLibraryItemsAsync(Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var settings = await connectionSettingsService.GetJellyfinAsync(cancellationToken);
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return [];
        }

        using var client = CreateHttpClient(settings);

        // Watch state in Jellyfin is per-user, so the library is fetched once per user and the
        // watch data folded together: an item counts as watched if any user watched it, and its
        // last-viewed date is the most recent across users - the semantics staleness cares about.
        // The item metadata itself (paths, sizes) is identical between users.
        var users = await GetUsersAsync(client, cancellationToken);
        var itemsById = new Dictionary<string, (JellyfinItem Item, int ViewCount, DateTimeOffset? LastViewedAt)>(StringComparer.Ordinal);

        if (users.Count == 0)
        {
            // An API key can always list users, but guard anyway: fall back to one pass without a
            // user context, which still yields paths/sizes/added dates - just no watch data.
            logger.LogWarning("Jellyfin returned no users - scanning without watch data");
            await ScanItemsAsync(client, userId: null, userName: null, itemsById, onProgress, cancellationToken);
        }

        foreach (var user in users)
        {
            try
            {
                await ScanItemsAsync(client, user.Id, user.Name, itemsById, onProgress, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load Jellyfin library for user {UserName}", user.Name);
            }
        }

        var items = new List<MediaLibraryItem>();
        foreach (var (item, viewCount, lastViewedAt) in itemsById.Values)
        {
            items.AddRange(ToLibraryItems(item, viewCount, lastViewedAt));
        }

        return items;
    }

    private async Task ScanItemsAsync(
        HttpClient client,
        string? userId,
        string? userName,
        Dictionary<string, (JellyfinItem Item, int ViewCount, DateTimeOffset? LastViewedAt)> itemsById,
        Func<string, Task>? onProgress,
        CancellationToken cancellationToken)
    {
        var startIndex = 0;
        while (true)
        {
            var url = $"Items?recursive=true&includeItemTypes=Movie,Episode&fields=Path,MediaSources,DateCreated&enableUserData=true&enableImages=false&startIndex={startIndex}&limit={PageSize}";
            if (userId is not null)
            {
                url += $"&userId={userId}";
            }

            var page = await client.GetFromJsonAsync<JellyfinItemsResponse>(url, cancellationToken);
            var pageItems = page?.Items ?? [];
            if (pageItems.Count == 0)
            {
                return;
            }

            if (onProgress is not null)
            {
                var scope = userName is null ? "Jellyfin library" : $"Jellyfin library for \"{userName}\"";
                await onProgress($"Scanning {scope} ({startIndex + pageItems.Count}/{page?.TotalRecordCount ?? startIndex + pageItems.Count})...");
            }

            foreach (var item in pageItems)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    continue;
                }

                // Jellyfin can report Played=true with PlayCount 0 (e.g. imported watch state),
                // so count Played as at least one view.
                var viewCount = Math.Max(item.UserData?.PlayCount ?? 0, item.UserData?.Played == true ? 1 : 0);
                var lastViewedAt = item.UserData?.LastPlayedDate;

                if (itemsById.TryGetValue(item.Id, out var existing))
                {
                    itemsById[item.Id] = (
                        existing.Item,
                        Math.Max(existing.ViewCount, viewCount),
                        Max(existing.LastViewedAt, lastViewedAt));
                }
                else
                {
                    itemsById[item.Id] = (item, viewCount, lastViewedAt);
                }
            }

            startIndex += pageItems.Count;
            if (page?.TotalRecordCount is int total && startIndex >= total)
            {
                return;
            }
        }
    }

    private IEnumerable<MediaLibraryItem> ToLibraryItems(JellyfinItem item, int viewCount, DateTimeOffset? lastViewedAt)
    {
        var isEpisode = string.Equals(item.Type, "Episode", StringComparison.OrdinalIgnoreCase);
        var seriesTitle = isEpisode && !string.IsNullOrWhiteSpace(item.SeriesName) ? item.SeriesName : null;
        var title = seriesTitle is null ? item.Name ?? string.Empty : $"{seriesTitle} - {item.Name}";
        var mediaType = isEpisode ? "episode" : "movie";

        // Multiple MediaSources under one item are Jellyfin's grouped-versions signal - the same
        // shape as Plex's multiple Media entries, and consumed the same way by cleanup.
        var sources = (item.MediaSources ?? [])
            .Where(source => !string.IsNullOrWhiteSpace(source.Path))
            .ToList();

        if (sources.Count == 0)
        {
            logger.LogWarning("Jellyfin item {ItemId} ({Title}) had no MediaSources with a file path in this response - skipped", item.Id, title);
            yield break;
        }

        foreach (var source in sources)
        {
            yield return new MediaLibraryItem(
                $"jellyfin:{item.Id}", title, source.Path!, source.Size ?? 0, viewCount, lastViewedAt, item.DateCreated,
                mediaType, seriesTitle, isEpisode ? item.ParentIndexNumber : null, isEpisode ? item.IndexNumber : null);
        }
    }

    private async Task<IReadOnlyList<JellyfinUser>> GetUsersAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var users = await client.GetFromJsonAsync<List<JellyfinUser>>("Users", cancellationToken);
            return users?.Where(user => !string.IsNullOrWhiteSpace(user.Id)).ToList() ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list Jellyfin users");
            return [];
        }
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        return left > right ? left : right;
    }

    private static HttpClient CreateHttpClient(ArrConnectionSettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        // Jellyfin's current auth scheme; the value has commas and quotes, which the default
        // header parser rejects, hence TryAddWithoutValidation.
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization",
            $"MediaBrowser Token=\"{settings.ApiKey}\", Client=\"Flowarr\", Device=\"Flowarr\", DeviceId=\"Flowarr\", Version=\"1.0\"");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        return client;
    }

    private async Task<ArrConnectionSettings> MergeSettingsAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        var saved = await connectionSettingsService.GetJellyfinAsync(cancellationToken);
        SecretReusePolicy.EnsureSavedSecretStaysWithTarget(
            request.ApiKey,
            request.ClearApiKey,
            saved.HasApiKey,
            SecretReusePolicy.IsUrlChange(request.BaseUrl, saved.BaseUrl),
            "Jellyfin API key",
            "base URL");

        return saved with
        {
            Enabled = request.Enabled,
            BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? saved.BaseUrl : request.BaseUrl,
            ApiKey = string.IsNullOrWhiteSpace(request.ApiKey) && !request.ClearApiKey ? saved.ApiKey : request.ApiKey ?? string.Empty,
            HasApiKey = !string.IsNullOrWhiteSpace(request.ApiKey) || (!request.ClearApiKey && saved.HasApiKey)
        };
    }

    private sealed record JellyfinSystemInfo(
        [property: JsonPropertyName("ServerName")] string? ServerName,
        [property: JsonPropertyName("Version")] string? Version);

    private sealed record JellyfinUser(
        [property: JsonPropertyName("Id")] string? Id,
        [property: JsonPropertyName("Name")] string? Name);

    private sealed record JellyfinItemsResponse(
        [property: JsonPropertyName("Items")] List<JellyfinItem>? Items,
        [property: JsonPropertyName("TotalRecordCount")] int? TotalRecordCount);

    private sealed record JellyfinItem(
        [property: JsonPropertyName("Id")] string? Id,
        [property: JsonPropertyName("Name")] string? Name,
        [property: JsonPropertyName("Type")] string? Type,
        [property: JsonPropertyName("SeriesName")] string? SeriesName,
        [property: JsonPropertyName("ParentIndexNumber")] int? ParentIndexNumber,
        [property: JsonPropertyName("IndexNumber")] int? IndexNumber,
        [property: JsonPropertyName("DateCreated")] DateTimeOffset? DateCreated,
        [property: JsonPropertyName("MediaSources")] List<JellyfinMediaSource>? MediaSources,
        [property: JsonPropertyName("UserData")] JellyfinUserData? UserData);

    private sealed record JellyfinMediaSource(
        [property: JsonPropertyName("Path")] string? Path,
        [property: JsonPropertyName("Size")] long? Size);

    private sealed record JellyfinUserData(
        [property: JsonPropertyName("PlayCount")] int? PlayCount,
        [property: JsonPropertyName("Played")] bool? Played,
        [property: JsonPropertyName("LastPlayedDate")] DateTimeOffset? LastPlayedDate);
}
