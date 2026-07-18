using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.JellyNotify.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Typed HttpClient implementation for Sonarr API communication.
/// </summary>
public sealed class SonarrApiClient : ISonarrApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<SonarrApiClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="SonarrApiClient"/> class.</summary>
    public SonarrApiClient(HttpClient http, ILogger<SonarrApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ArrSystemStatus?> TestConnectionAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            return await client.GetFromJsonAsync<ArrSystemStatus>("api/v3/system/status", JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sonarr connection test failed for {Url}", serverUrl);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArrSeries>> GetAllSeriesAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var result = await client.GetFromJsonAsync<List<ArrSeries>>("api/v3/series", JsonOptions, cancellationToken).ConfigureAwait(false);
            return (result ?? new List<ArrSeries>()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching series from Sonarr {Url}", serverUrl);
            return Array.Empty<ArrSeries>();
        }
    }

    /// <inheritdoc />
    public async Task<ArrSeries?> GetSeriesByIdAsync(string serverUrl, string apiKey, int seriesId, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            return await client.GetFromJsonAsync<ArrSeries>($"api/v3/series/{seriesId}", JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Sonarr series {SeriesId}", seriesId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ArrQueueResponse?> GetQueueAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            return await GetPagedQueueAsync(client, "api/v3/queue?includeUnknownSeriesItems=false", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Sonarr queue from {Url}", serverUrl);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArrEpisodeFile>> GetEpisodeFilesAsync(string serverUrl, string apiKey, int seriesId, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var result = await client.GetFromJsonAsync<List<ArrEpisodeFile>>($"api/v3/episodefile?seriesId={seriesId}", JsonOptions, cancellationToken).ConfigureAwait(false);
            return result ?? new List<ArrEpisodeFile>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Sonarr episode files for series {SeriesId} from {Url}", seriesId, serverUrl);
            return new List<ArrEpisodeFile>();
        }
    }

    /// <inheritdoc />
    public async Task<ArrHistoryResponse?> GetHistoryAsync(string serverUrl, string apiKey, int page = 1, int pageSize = 50, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            return await client.GetFromJsonAsync<ArrHistoryResponse>(
                $"api/v3/history?page={page}&pageSize={pageSize}&sortKey=date&sortDir=desc",
                JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Sonarr history from {Url}", serverUrl);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArrNotificationResource>> GetNotificationsAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var result = await client.GetFromJsonAsync<List<ArrNotificationResource>>("api/v3/notification", JsonOptions, cancellationToken).ConfigureAwait(false);
            return (result ?? new List<ArrNotificationResource>()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching notifications from Sonarr {Url}", serverUrl);
            return Array.Empty<ArrNotificationResource>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArrNotificationResource>> GetNotificationSchemasAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var result = await client.GetFromJsonAsync<List<ArrNotificationResource>>("api/v3/notification/schema", JsonOptions, cancellationToken).ConfigureAwait(false);
            return (result ?? new List<ArrNotificationResource>()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching notification schemas from Sonarr {Url}", serverUrl);
            return Array.Empty<ArrNotificationResource>();
        }
    }

    /// <inheritdoc />
    public async Task<(ArrNotificationResource? Created, string? Error)> CreateNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource notification, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var response = await client.PostAsJsonAsync("api/v3/notification", notification, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, await ExtractValidationError(response, cancellationToken).ConfigureAwait(false));
            }

            var created = await response.Content.ReadFromJsonAsync<ArrNotificationResource>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return (created, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Sonarr notification on {Url}", serverUrl);
            return (null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<(ArrNotificationResource? Updated, string? Error)> UpdateNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource notification, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var response = await client.PutAsJsonAsync($"api/v3/notification/{notification.Id}", notification, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, await ExtractValidationError(response, cancellationToken).ConfigureAwait(false));
            }

            var updated = await response.Content.ReadFromJsonAsync<ArrNotificationResource>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return (updated, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Sonarr notification {Id} on {Url}", notification.Id, serverUrl);
            return (null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> TestNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource candidate, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var response = await client.PostAsJsonAsync("api/v3/notification/test", candidate, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return (false, await ExtractValidationError(response, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Sonarr notification on {Url}", serverUrl);
            return (false, ex.Message);
        }
    }

    private static async Task<string?> ExtractValidationError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var failures = JsonSerializer.Deserialize<List<ArrValidationFailure>>(body, JsonOptions);
            var messages = failures?.Select(f => f.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            if (messages is { Count: > 0 })
            {
                return string.Join("; ", messages);
            }
        }
        catch
        {
            // Not the expected validation-failure array shape — fall through to the raw body,
            // which still beats a generic "no detail" message.
        }

        return body.Length > 300 ? body[..300] : body;
    }

    private static HttpClient CreateClient(string serverUrl, string apiKey, bool ignoreSsl)
    {
        var handler = new HttpClientHandler();
        if (ignoreSsl)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"),
            // A hanging/unreachable instance must not consume the whole shared
            // background-cycle budget (see JellyNotifyBackgroundService's 3-minute
            // cycle timeout) — cap each instance's own calls well below that.
            Timeout = TimeSpan.FromSeconds(20)
        };

        const string header = "X-Api-Key";
        client.DefaultRequestHeaders.Add(header, apiKey);
        return client;
    }

    private static async Task<ArrQueueResponse?> GetPagedQueueAsync(HttpClient client, string basePath, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var page = 1;
        var combined = new ArrQueueResponse { Page = 1, PageSize = pageSize };

        while (true)
        {
            var separator = basePath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var response = await client.GetFromJsonAsync<ArrQueueResponse>(
                $"{basePath}{separator}page={page}&pageSize={pageSize}",
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (response?.Records is null || response.Records.Count == 0)
            {
                break;
            }

            combined.TotalRecords = response.TotalRecords;
            combined.Records.AddRange(response.Records);

            if (combined.Records.Count >= response.TotalRecords || response.Records.Count < pageSize)
            {
                break;
            }

            page++;
        }

        return combined;
    }
}

/// <summary>
/// Typed HttpClient implementation for Radarr API communication.
/// </summary>
public sealed class RadarrApiClient : IRadarrApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<RadarrApiClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="RadarrApiClient"/> class.</summary>
    public RadarrApiClient(HttpClient http, ILogger<RadarrApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ArrSystemStatus?> TestConnectionAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            return await client.GetFromJsonAsync<ArrSystemStatus>("api/v3/system/status", JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Radarr connection test failed for {Url}", serverUrl);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArrMovie>> GetAllMoviesAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var result = await client.GetFromJsonAsync<List<ArrMovie>>("api/v3/movie", JsonOptions, cancellationToken).ConfigureAwait(false);
            return (result ?? new List<ArrMovie>()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching movies from Radarr {Url}", serverUrl);
            return Array.Empty<ArrMovie>();
        }
    }

    /// <inheritdoc />
    public async Task<ArrMovie?> GetMovieByIdAsync(string serverUrl, string apiKey, int movieId, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            return await client.GetFromJsonAsync<ArrMovie>($"api/v3/movie/{movieId}", JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Radarr movie {MovieId}", movieId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ArrQueueResponse?> GetQueueAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            return await GetPagedQueueAsync(client, "api/v3/queue?includeUnknownMovieItems=false", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Radarr queue from {Url}", serverUrl);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArrMovieFile>> GetMovieFilesAsync(string serverUrl, string apiKey, int movieId, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var result = await client.GetFromJsonAsync<List<ArrMovieFile>>($"api/v3/moviefile?movieId={movieId}", JsonOptions, cancellationToken).ConfigureAwait(false);
            return result ?? new List<ArrMovieFile>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Radarr movie files for movie {MovieId} from {Url}", movieId, serverUrl);
            return new List<ArrMovieFile>();
        }
    }

    /// <inheritdoc />
    public async Task<ArrHistoryResponse?> GetHistoryAsync(string serverUrl, string apiKey, int page = 1, int pageSize = 50, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            return await client.GetFromJsonAsync<ArrHistoryResponse>(
                $"api/v3/history?page={page}&pageSize={pageSize}&sortKey=date&sortDir=desc",
                JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Radarr history from {Url}", serverUrl);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArrNotificationResource>> GetNotificationsAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var result = await client.GetFromJsonAsync<List<ArrNotificationResource>>("api/v3/notification", JsonOptions, cancellationToken).ConfigureAwait(false);
            return (result ?? new List<ArrNotificationResource>()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching notifications from Radarr {Url}", serverUrl);
            return Array.Empty<ArrNotificationResource>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArrNotificationResource>> GetNotificationSchemasAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var result = await client.GetFromJsonAsync<List<ArrNotificationResource>>("api/v3/notification/schema", JsonOptions, cancellationToken).ConfigureAwait(false);
            return (result ?? new List<ArrNotificationResource>()).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching notification schemas from Radarr {Url}", serverUrl);
            return Array.Empty<ArrNotificationResource>();
        }
    }

    /// <inheritdoc />
    public async Task<(ArrNotificationResource? Created, string? Error)> CreateNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource notification, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var response = await client.PostAsJsonAsync("api/v3/notification", notification, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, await ExtractValidationError(response, cancellationToken).ConfigureAwait(false));
            }

            var created = await response.Content.ReadFromJsonAsync<ArrNotificationResource>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return (created, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Radarr notification on {Url}", serverUrl);
            return (null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<(ArrNotificationResource? Updated, string? Error)> UpdateNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource notification, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var response = await client.PutAsJsonAsync($"api/v3/notification/{notification.Id}", notification, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, await ExtractValidationError(response, cancellationToken).ConfigureAwait(false));
            }

            var updated = await response.Content.ReadFromJsonAsync<ArrNotificationResource>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return (updated, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Radarr notification {Id} on {Url}", notification.Id, serverUrl);
            return (null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> TestNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource candidate, bool ignoreSsl = false, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(serverUrl, apiKey, ignoreSsl);
            var response = await client.PostAsJsonAsync("api/v3/notification/test", candidate, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return (false, await ExtractValidationError(response, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Radarr notification on {Url}", serverUrl);
            return (false, ex.Message);
        }
    }

    private static async Task<string?> ExtractValidationError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var failures = JsonSerializer.Deserialize<List<ArrValidationFailure>>(body, JsonOptions);
            var messages = failures?.Select(f => f.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            if (messages is { Count: > 0 })
            {
                return string.Join("; ", messages);
            }
        }
        catch
        {
            // Not the expected validation-failure array shape — fall through to the raw body,
            // which still beats a generic "no detail" message.
        }

        return body.Length > 300 ? body[..300] : body;
    }

    private static HttpClient CreateClient(string serverUrl, string apiKey, bool ignoreSsl)
    {
        var handler = new HttpClientHandler();
        if (ignoreSsl)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/"),
            // A hanging/unreachable instance must not consume the whole shared
            // background-cycle budget (see JellyNotifyBackgroundService's 3-minute
            // cycle timeout) — cap each instance's own calls well below that.
            Timeout = TimeSpan.FromSeconds(20)
        };

        const string header = "X-Api-Key";
        client.DefaultRequestHeaders.Add(header, apiKey);
        return client;
    }

    private static async Task<ArrQueueResponse?> GetPagedQueueAsync(HttpClient client, string basePath, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var page = 1;
        var combined = new ArrQueueResponse { Page = 1, PageSize = pageSize };

        while (true)
        {
            var separator = basePath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var response = await client.GetFromJsonAsync<ArrQueueResponse>(
                $"{basePath}{separator}page={page}&pageSize={pageSize}",
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (response?.Records is null || response.Records.Count == 0)
            {
                break;
            }

            combined.TotalRecords = response.TotalRecords;
            combined.Records.AddRange(response.Records);

            if (combined.Records.Count >= response.TotalRecords || response.Records.Count < pageSize)
            {
                break;
            }

            page++;
        }

        return combined;
    }
}
