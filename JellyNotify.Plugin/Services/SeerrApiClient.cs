using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.JellyNotify.Configuration;
using Jellyfin.Plugin.JellyNotify.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Typed HttpClient implementation for communicating with Overseerr/Jellyseerr.
/// API keys are handled server-side only — never exposed to the frontend.
/// </summary>
public sealed class SeerrApiClient : ISeerrApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<SeerrApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeerrApiClient"/> class.
    /// </summary>
    public SeerrApiClient(HttpClient http, ILogger<SeerrApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    private SeerrSettings Settings => Plugin.Instance!.Configuration.SeerrSettings;

    /// <inheritdoc />
    public async Task<SeerrTestResponse?> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            return await client.GetFromJsonAsync<SeerrTestResponse>(
                "api/v1/status", JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seerr connection test failed");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SeerrRequest>> GetAllRequestsAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        var all = new List<SeerrRequest>();
        var page = 1;
        const int pageSize = 50;

        while (true)
        {
            try
            {
                var result = await client.GetFromJsonAsync<SeerrPagedResult<SeerrRequest>>(
                    $"api/v1/request?take={pageSize}&skip={(page - 1) * pageSize}&sort=added",
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);

                if (result?.Results is null || result.Results.Count == 0)
                {
                    break;
                }

                all.AddRange(result.Results);

                if (result.Results.Count < pageSize)
                {
                    break;
                }

                page++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching requests from Seerr (page {Page})", page);
                break;
            }
        }

        return all.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<SeerrRequest?> GetRequestByIdAsync(int requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            return await client.GetFromJsonAsync<SeerrRequest>(
                $"api/v1/request/{requestId}", JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Seerr request {RequestId}", requestId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SeerrUser?> GetUserByIdAsync(int seerrUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            return await client.GetFromJsonAsync<SeerrUser>(
                $"api/v1/user/{seerrUserId}", JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Seerr user {UserId}", seerrUserId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SeerrUser>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        var all = new List<SeerrUser>();
        var page = 1;
        const int pageSize = 50;

        while (true)
        {
            try
            {
                var result = await client.GetFromJsonAsync<SeerrPagedResult<SeerrUser>>(
                    $"api/v1/user?take={pageSize}&skip={(page - 1) * pageSize}",
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);

                if (result?.Results is null || result.Results.Count == 0)
                {
                    break;
                }

                all.AddRange(result.Results);

                if (result.Results.Count < pageSize)
                {
                    break;
                }

                page++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Seerr users (page {Page})", page);
                break;
            }
        }

        return all.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<SeerrMediaDetails?> GetMediaDetailsAsync(string mediaType, int tmdbId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            var endpoint = string.Equals(mediaType, "movie", StringComparison.OrdinalIgnoreCase)
                ? $"api/v1/movie/{tmdbId}"
                : $"api/v1/tv/{tmdbId}";

            return await client.GetFromJsonAsync<SeerrMediaDetails>(endpoint, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching media details for {MediaType} {TmdbId}", mediaType, tmdbId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SeerrWebhookSettings?> GetWebhookSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            return await client.GetFromJsonAsync<SeerrWebhookSettings>(
                "api/v1/settings/notifications/webhook", JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Seerr webhook settings");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> SetWebhookSettingsAsync(SeerrWebhookSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            var response = await client.PostAsJsonAsync("api/v1/settings/notifications/webhook", settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return (false, await ExtractSeerrError(response, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Seerr webhook settings");
            return (false, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> TestWebhookSettingsAsync(SeerrWebhookSettings candidate, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient();
            var response = await client.PostAsJsonAsync("api/v1/settings/notifications/webhook/test", candidate, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return (false, await ExtractSeerrError(response, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Seerr webhook settings");
            return (false, ex.Message);
        }
    }

    private static async Task<string?> ExtractSeerrError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<SeerrErrorResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return body?.Message;
        }
        catch
        {
            // Response body wasn't the expected shape — no message available.
            return null;
        }
    }

    private sealed class SeerrErrorResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    /// <summary>
    /// Creates an HTTP client from the current plugin configuration.
    /// Called before each request to pick up configuration and SSL validation changes.
    /// </summary>
    private static HttpClient CreateClient()
    {
        var settings = Plugin.Instance!.Configuration.SeerrSettings;
        var handler = new HttpClientHandler();
        if (settings.IgnoreSslErrors)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.ServerUrl.TrimEnd('/') + "/"),
            // See ArrApiClients.CreateClient for why this is capped well below the
            // shared background-cycle timeout.
            Timeout = TimeSpan.FromSeconds(20)
        };

        const string headerName = "X-Api-Key";
        client.DefaultRequestHeaders.Add(headerName, settings.ApiKey);
        return client;
    }
}
