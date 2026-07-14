using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Integrations;

public sealed class PlexClient(
    IConnectionSettingsService connectionSettingsService,
    ILogger<PlexClient> logger)
{
    public async Task<ConnectionTestResult> TestAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        var settings = await MergeSettingsAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return ConnectionTestResult.Failure("Plex base URL is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return ConnectionTestResult.Failure("Plex token is required.");
        }

        try
        {
            using var client = CreateHttpClient(settings);
            var response = await client.GetAsync("identity", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return ConnectionTestResult.Failure($"Plex test failed: HTTP {(int)response.StatusCode}. {body}");
            }

            return ConnectionTestResult.Success("Connected to Plex.");
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Failure(ex.Message);
        }
    }

    public async Task<IReadOnlyCollection<PlexLibraryItem>> GetLibraryItemsAsync(Func<string, Task>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var settings = await connectionSettingsService.GetPlexAsync(cancellationToken);
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return [];
        }

        using var client = CreateHttpClient(settings);
        var items = new List<PlexLibraryItem>();

        var sections = await client.GetFromJsonAsync<PlexSectionsResponse>("library/sections", cancellationToken);
        foreach (var section in sections?.MediaContainer?.Directory ?? [])
        {
            try
            {
                if (section.Type == "movie")
                {
                    if (onProgress is not null)
                    {
                        await onProgress($"Scanning movie library \"{section.Title}\"...");
                    }

                    var response = await client.GetFromJsonAsync<PlexMetadataResponse>($"library/sections/{section.Key}/all", cancellationToken);
                    foreach (var metadata in response?.MediaContainer?.Metadata ?? [])
                    {
                        items.AddRange(ToLibraryItems(metadata, metadata.Title, "movie", seriesTitle: null));
                    }
                }
                else if (section.Type == "show")
                {
                    if (onProgress is not null)
                    {
                        await onProgress($"Scanning TV library \"{section.Title}\"...");
                    }

                    var showResponse = await client.GetFromJsonAsync<PlexMetadataResponse>($"library/sections/{section.Key}/all", cancellationToken);
                    var shows = showResponse?.MediaContainer?.Metadata ?? [];
                    for (var showIndex = 0; showIndex < shows.Count; showIndex++)
                    {
                        var show = shows[showIndex];
                        try
                        {
                            if (onProgress is not null)
                            {
                                await onProgress($"Loading episodes for \"{show.Title}\" ({showIndex + 1}/{shows.Count})...");
                            }

                            var episodesResponse = await client.GetFromJsonAsync<PlexMetadataResponse>($"library/metadata/{show.RatingKey}/allLeaves", cancellationToken);
                            var leaves = episodesResponse?.MediaContainer?.Metadata ?? [];

                            foreach (var episode in leaves)
                            {
                                // Plex returns "" (not null) for grandparentTitle on some episodes -
                                // "??" doesn't catch that, so fall back to the show's own title
                                // (from the /all listing, always populated) via an explicit blank
                                // check. Derive the combined display title from the same
                                // fallback so it doesn't silently drop the show name too.
                                var seriesTitle = string.IsNullOrWhiteSpace(episode.GrandparentTitle) ? show.Title : episode.GrandparentTitle;
                                var title = string.IsNullOrWhiteSpace(seriesTitle) ? episode.Title : $"{seriesTitle} - {episode.Title}";
                                var episodeItems = ToLibraryItems(episode, title, "episode", seriesTitle).ToList();
                                if (episodeItems.Count == 0)
                                {
                                    logger.LogWarning(
                                        "Plex episode {RatingKey} ({Title}, S{Season}E{Episode}) under show {ShowTitle} had no Media/Part file info in this response - skipped",
                                        episode.RatingKey, episode.Title, episode.ParentIndex, episode.Index, show.Title);
                                }

                                items.AddRange(episodeItems);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to load Plex episodes for series {RatingKey}", show.RatingKey);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load Plex library section {SectionKey} ({SectionType})", section.Key, section.Type);
            }
        }

        return items;
    }

    private static IEnumerable<PlexLibraryItem> ToLibraryItems(PlexMetadataItem metadata, string title, string mediaType, string? seriesTitle)
    {
        var lastViewedAt = metadata.LastViewedAt is long lastViewed ? DateTimeOffset.FromUnixTimeSeconds(lastViewed) : (DateTimeOffset?)null;
        var addedAt = metadata.AddedAt is long added ? DateTimeOffset.FromUnixTimeSeconds(added) : (DateTimeOffset?)null;
        var viewCount = metadata.ViewCount ?? 0;

        foreach (var media in metadata.Media ?? [])
        {
            foreach (var part in media.Part ?? [])
            {
                if (string.IsNullOrWhiteSpace(part.File))
                {
                    continue;
                }

                yield return new PlexLibraryItem(
                    metadata.RatingKey, title, part.File, part.Size ?? 0, viewCount, lastViewedAt, addedAt,
                    mediaType, seriesTitle, metadata.ParentIndex, metadata.Index);
            }
        }
    }

    private static HttpClient CreateHttpClient(ArrConnectionSettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Add("X-Plex-Token", settings.ApiKey);
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        return client;
    }

    private async Task<ArrConnectionSettings> MergeSettingsAsync(SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        var saved = await connectionSettingsService.GetPlexAsync(cancellationToken);
        return saved with
        {
            Enabled = request.Enabled,
            BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? saved.BaseUrl : request.BaseUrl,
            ApiKey = string.IsNullOrWhiteSpace(request.ApiKey) && !request.ClearApiKey ? saved.ApiKey : request.ApiKey ?? string.Empty,
            HasApiKey = !string.IsNullOrWhiteSpace(request.ApiKey) || (!request.ClearApiKey && saved.HasApiKey)
        };
    }

    private sealed record PlexSectionsResponse([property: JsonPropertyName("MediaContainer")] PlexSectionsContainer? MediaContainer);
    private sealed record PlexSectionsContainer([property: JsonPropertyName("Directory")] List<PlexSection>? Directory);
    private sealed record PlexSection(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("title")] string Title);

    private sealed record PlexMetadataResponse([property: JsonPropertyName("MediaContainer")] PlexMetadataContainer? MediaContainer);
    private sealed record PlexMetadataContainer([property: JsonPropertyName("Metadata")] List<PlexMetadataItem>? Metadata);
    private sealed record PlexMetadataItem(
        [property: JsonPropertyName("ratingKey")] string RatingKey,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("grandparentTitle")] string? GrandparentTitle,
        [property: JsonPropertyName("parentIndex")] int? ParentIndex,
        [property: JsonPropertyName("index")] int? Index,
        [property: JsonPropertyName("viewCount")] int? ViewCount,
        [property: JsonPropertyName("lastViewedAt")] long? LastViewedAt,
        [property: JsonPropertyName("addedAt")] long? AddedAt,
        [property: JsonPropertyName("Media")] List<PlexMedia>? Media);
    private sealed record PlexMedia([property: JsonPropertyName("Part")] List<PlexPart>? Part);
    private sealed record PlexPart(
        [property: JsonPropertyName("file")] string? File,
        [property: JsonPropertyName("size")] long? Size);
}
