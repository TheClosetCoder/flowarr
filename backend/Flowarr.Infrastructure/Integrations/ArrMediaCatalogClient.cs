using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flowarr.Core.Integrations;
using Flowarr.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Flowarr.Infrastructure.Integrations;

public sealed class ArrMediaCatalogClient(
    IConnectionSettingsService connectionSettingsService,
    ILogger<ArrMediaCatalogClient> logger) : IMediaCatalogClient
{
    public async Task<IReadOnlyCollection<MediaItemSnapshot>> GetMediaItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<MediaItemSnapshot>();
        items.AddRange(await GetRadarrItemsAsync(cancellationToken));
        items.AddRange(await GetSonarrItemsAsync(cancellationToken));
        return items;
    }

    public async Task<ConnectionTestResult> TestAsync(string system, SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await MergeSettingsAsync(system, request, cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                return ConnectionTestResult.Failure($"{system} base URL is required.");
            }

            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                return ConnectionTestResult.Failure($"{system} API key is required.");
            }

            using var client = CreateHttpClient(settings);
            var response = await client.GetAsync("api/v3/system/status", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return ConnectionTestResult.Failure($"{system} test failed: HTTP {(int)response.StatusCode}. {body}");
            }

            var status = await response.Content.ReadFromJsonAsync<ArrStatus>(cancellationToken);
            return ConnectionTestResult.Success($"Connected to {system}. Version {status?.Version ?? "unknown"}.");
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Failure(ex.Message);
        }
    }

    public async Task<IReadOnlyCollection<ArrRootFolder>> GetRootFoldersAsync(string system, CancellationToken cancellationToken)
    {
        var settings = system.Equals("Sonarr", StringComparison.OrdinalIgnoreCase)
            ? await connectionSettingsService.GetSonarrAsync(cancellationToken)
            : await connectionSettingsService.GetRadarrAsync(cancellationToken);

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return [];
        }

        using var client = CreateHttpClient(settings);
        return await client.GetFromJsonAsync<List<ArrRootFolder>>("api/v3/rootfolder", cancellationToken) ?? [];
    }

    private async Task<IReadOnlyCollection<MediaItemSnapshot>> GetRadarrItemsAsync(CancellationToken cancellationToken)
    {
        var settings = await connectionSettingsService.GetRadarrAsync(cancellationToken);
        if (!IsConfigured(settings))
        {
            return [];
        }

        try
        {
            using var client = CreateHttpClient(settings);
            var movies = await client.GetFromJsonAsync<List<RadarrMovie>>("api/v3/movie", cancellationToken) ?? [];
            return movies.Select(movie => new MediaItemSnapshot(
                movie.Title,
                movie.Id.ToString(),
                "Radarr",
                movie.HasFile,
                movie.MovieFile is null
                    ? []
                    : [new MediaFileSnapshot(movie.MovieFile.Path ?? movie.Path, movie.MovieFile.Path, movie.MovieFile.Size, File.Exists(movie.MovieFile.Path ?? string.Empty), true, movie.HasFile)]))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Radarr sync failed.");
            return [];
        }
    }

    private async Task<IReadOnlyCollection<MediaItemSnapshot>> GetSonarrItemsAsync(CancellationToken cancellationToken)
    {
        var settings = await connectionSettingsService.GetSonarrAsync(cancellationToken);
        if (!IsConfigured(settings))
        {
            return [];
        }

        try
        {
            using var client = CreateHttpClient(settings);
            var series = await client.GetFromJsonAsync<List<SonarrSeries>>("api/v3/series", cancellationToken) ?? [];
            var items = new List<MediaItemSnapshot>();
            foreach (var item in series)
            {
                var episodes = await client.GetFromJsonAsync<List<SonarrEpisode>>($"api/v3/episode?seriesId={item.Id}", cancellationToken) ?? [];
                var files = episodes
                    .Where(episode => episode.HasFile && episode.EpisodeFile is not null)
                    .Select(episode => new MediaFileSnapshot(
                        episode.EpisodeFile!.Path ?? item.Path,
                        episode.EpisodeFile.Path,
                        episode.EpisodeFile.Size,
                        File.Exists(episode.EpisodeFile.Path ?? string.Empty),
                        true,
                        true))
                    .ToList();

                items.Add(new MediaItemSnapshot(item.Title, item.Id.ToString(), "Sonarr", files.Count > 0, files));
            }

            return items;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sonarr sync failed.");
            return [];
        }
    }

    private async Task<ArrConnectionSettings> MergeSettingsAsync(string system, SaveArrConnectionRequest request, CancellationToken cancellationToken)
    {
        var saved = system.Equals("Sonarr", StringComparison.OrdinalIgnoreCase)
            ? await connectionSettingsService.GetSonarrAsync(cancellationToken)
            : await connectionSettingsService.GetRadarrAsync(cancellationToken);

        SecretReusePolicy.EnsureSavedSecretStaysWithTarget(
            request.ApiKey,
            request.ClearApiKey,
            saved.HasApiKey,
            SecretReusePolicy.IsUrlChange(request.BaseUrl, saved.BaseUrl),
            $"{system} API key",
            "base URL");

        return saved with
        {
            Enabled = request.Enabled,
            BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? saved.BaseUrl : request.BaseUrl,
            ApiKey = string.IsNullOrWhiteSpace(request.ApiKey) && !request.ClearApiKey ? saved.ApiKey : request.ApiKey ?? string.Empty,
            HasApiKey = !string.IsNullOrWhiteSpace(request.ApiKey) || (!request.ClearApiKey && saved.HasApiKey)
        };
    }

    private static bool IsConfigured(ArrConnectionSettings settings)
    {
        return settings.Enabled && !string.IsNullOrWhiteSpace(settings.BaseUrl) && !string.IsNullOrWhiteSpace(settings.ApiKey);
    }

    private static HttpClient CreateHttpClient(ArrConnectionSettings settings)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
        return client;
    }

    private sealed record ArrStatus([property: JsonPropertyName("version")] string? Version);
    public sealed record ArrRootFolder([property: JsonPropertyName("path")] string Path, [property: JsonPropertyName("freeSpace")] long FreeSpace);

    private sealed record RadarrMovie(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("hasFile")] bool HasFile,
        [property: JsonPropertyName("movieFile")] ArrFile? MovieFile);

    private sealed record SonarrSeries(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("path")] string Path);

    private sealed record SonarrEpisode(
        [property: JsonPropertyName("hasFile")] bool HasFile,
        [property: JsonPropertyName("episodeFile")] ArrFile? EpisodeFile);

    private sealed record ArrFile(
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("size")] long Size);
}
