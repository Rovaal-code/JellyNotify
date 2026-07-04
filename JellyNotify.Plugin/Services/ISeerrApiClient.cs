using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Provides access to the Overseerr/Jellyseerr API.
/// </summary>
public interface ISeerrApiClient
{
    /// <summary>Tests the Seerr connection and returns the server version.</summary>
    Task<SeerrTestResponse?> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets all media requests, paging through all available results.</summary>
    Task<IReadOnlyList<SeerrRequest>> GetAllRequestsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a specific request by its ID.</summary>
    Task<SeerrRequest?> GetRequestByIdAsync(int requestId, CancellationToken cancellationToken = default);

    /// <summary>Gets a Seerr user by their ID and returns their linked Jellyfin user ID if available.</summary>
    Task<SeerrUser?> GetUserByIdAsync(int seerrUserId, CancellationToken cancellationToken = default);

    /// <summary>Returns all users from Seerr, paging as needed.</summary>
    Task<IReadOnlyList<SeerrUser>> GetAllUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets poster/title/year details for a movie or TV show via Seerr's own movie/tv
    /// detail endpoints (Seerr proxies TMDB, so this needs no separate TMDB API key).
    /// One call covers the poster, a real title fallback, and the release year — the
    /// request list endpoint alone doesn't always carry a resolvable title. Returns
    /// null if unavailable (no such media, request failed).
    /// </summary>
    Task<SeerrMediaDetails?> GetMediaDetailsAsync(string mediaType, int tmdbId, CancellationToken cancellationToken = default);

    /// <summary>Gets Seerr's current webhook notification agent settings.</summary>
    Task<SeerrWebhookSettings?> GetWebhookSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces Seerr's webhook notification agent settings.</summary>
    Task<(bool Success, string? Error)> SetWebhookSettingsAsync(SeerrWebhookSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Asks Seerr to send a real test call for a candidate (possibly unsaved) webhook configuration, so deliverability can be confirmed before actually saving it.</summary>
    Task<(bool Success, string? Error)> TestWebhookSettingsAsync(SeerrWebhookSettings candidate, CancellationToken cancellationToken = default);
}
