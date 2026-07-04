using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Represents a user in Overseerr/Jellyseerr.
/// </summary>
public class SeerrUser
{
    /// <summary>Gets or sets the Seerr user ID.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the user's email address.</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the username.</summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the linked Jellyfin user ID.</summary>
    [JsonPropertyName("jellyfinUserId")]
    public string? JellyfinUserId { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets the user type (e.g., 1 = local, 2 = Jellyfin).</summary>
    [JsonPropertyName("userType")]
    public int UserType { get; set; }

    /// <summary>Gets or sets the user's permission bitmask.</summary>
    [JsonPropertyName("permissions")]
    public int Permissions { get; set; }
}

/// <summary>
/// Represents a media request in Overseerr/Jellyseerr.
/// </summary>
public class SeerrRequest
{
    /// <summary>Gets or sets the request ID.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the request status.</summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>Gets or sets the request type (e.g., "movie", "tv").</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets the movie title when present at the request root.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the series name when present at the request root.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the original movie title when present at the request root.</summary>
    [JsonPropertyName("originalTitle")]
    public string? OriginalTitle { get; set; }

    /// <summary>Gets or sets the original series name when present at the request root.</summary>
    [JsonPropertyName("originalName")]
    public string? OriginalName { get; set; }

    /// <summary>Gets or sets the TMDB identifier when present at the request root.</summary>
    [JsonPropertyName("tmdbId")]
    public int? TmdbId { get; set; }

    /// <summary>Gets or sets the TVDB identifier when present at the request root.</summary>
    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }

    /// <summary>Gets or sets the IMDb identifier when present at the request root.</summary>
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; }

    /// <summary>Gets or sets the associated media information.</summary>
    [JsonPropertyName("media")]
    public SeerrMedia Media { get; set; } = new();

    /// <summary>Gets or sets the user who made the request.</summary>
    [JsonPropertyName("requestedBy")]
    public SeerrUser RequestedBy { get; set; } = new();

    /// <summary>Gets or sets the creation timestamp.</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the last update timestamp.</summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>Gets or sets the user who last modified the request.</summary>
    [JsonPropertyName("modifiedBy")]
    public SeerrUser? ModifiedBy { get; set; }

    /// <summary>Gets or sets the requested seasons (for TV requests).</summary>
    [JsonPropertyName("seasons")]
    public List<SeerrSeason>? Seasons { get; set; }

    /// <summary>
    /// Gets or sets the embedded media detail info (title, name, etc.).
    /// Overseerr/Jellyseerr may embed this in the request response.
    /// </summary>
    [JsonPropertyName("mediaInfo")]
    public SeerrMediaInfo? MediaInfo { get; set; }
}

/// <summary>
/// Represents a requested season in a TV request.
/// </summary>
public class SeerrSeason
{
    /// <summary>Gets or sets the season number.</summary>
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the season request status.</summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }
}

/// <summary>
/// Represents media metadata in Overseerr/Jellyseerr.
/// </summary>
public class SeerrMedia
{
    /// <summary>Gets or sets the internal media ID.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the TMDB identifier.</summary>
    [JsonPropertyName("tmdbId")]
    public int? TmdbId { get; set; }

    /// <summary>Gets or sets the TVDB identifier.</summary>
    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }

    /// <summary>Gets or sets the IMDb identifier.</summary>
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; }

    /// <summary>Gets or sets the media type.</summary>
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the movie title when media details are embedded directly in media.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the TV series name when media details are embedded directly in media.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the original movie title when media details are embedded directly in media.</summary>
    [JsonPropertyName("originalTitle")]
    public string? OriginalTitle { get; set; }

    /// <summary>Gets or sets the original TV series name when media details are embedded directly in media.</summary>
    [JsonPropertyName("originalName")]
    public string? OriginalName { get; set; }

    /// <summary>Gets or sets the media availability status.</summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>Gets or sets the external service ID (e.g., Sonarr/Radarr ID).</summary>
    [JsonPropertyName("externalServiceId")]
    public int? ExternalServiceId { get; set; }

    /// <summary>Gets or sets the external service slug.</summary>
    [JsonPropertyName("externalServiceSlug")]
    public string? ExternalServiceSlug { get; set; }

    /// <summary>Gets or sets the service (server) ID in Seerr configuration.</summary>
    [JsonPropertyName("serviceId")]
    public int? ServiceId { get; set; }

    /// <summary>Gets or sets per-season availability status (TV only) — distinct from <see cref="SeerrRequest.Seasons"/>, which reflects request/approval status; this reflects actual download/availability state per season, as returned embedded in the request list's "media" object.</summary>
    [JsonPropertyName("seasons")]
    public List<SeerrMediaSeasonStatus>? Seasons { get; set; }
}

/// <summary>
/// A single season's availability status within <see cref="SeerrMedia"/>, using the same status codes as <see cref="SeerrMediaStatus"/>.
/// </summary>
public class SeerrMediaSeasonStatus
{
    /// <summary>Gets or sets the season number.</summary>
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the season's availability status (see <see cref="SeerrMediaStatus"/>).</summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }
}

/// <summary>
/// Represents detailed media info returned in request responses (movie/tv details object).
/// Overseerr/Jellyseerr embeds this inside the request payload under the "media" or nested key.
/// </summary>
public class SeerrMediaInfo
{
    /// <summary>Gets or sets the movie title (movies).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the TV show name (TV series).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the original title (movies).</summary>
    [JsonPropertyName("originalTitle")]
    public string? OriginalTitle { get; set; }

    /// <summary>Gets or sets the original name (TV).</summary>
    [JsonPropertyName("originalName")]
    public string? OriginalName { get; set; }

    /// <summary>Gets the best available title for display.</summary>
    [JsonIgnore]
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title :
        !string.IsNullOrWhiteSpace(Name) ? Name :
        !string.IsNullOrWhiteSpace(OriginalTitle) ? OriginalTitle :
        !string.IsNullOrWhiteSpace(OriginalName) ? OriginalName :
        string.Empty;
}

/// <summary>
/// Minimal shape of Seerr's <c>/api/v1/movie/{id}</c> and <c>/api/v1/tv/{id}</c>
/// responses — Seerr proxies these straight from TMDB. Fetched once per request
/// (see <see cref="Models.RequestSnapshot.PosterUrl"/>) and reused for the poster,
/// a real title when the request list didn't have one, and the release year —
/// all from the single call, not three.
/// </summary>
public class SeerrMediaDetails
{
    /// <summary>Gets or sets the TMDB-relative poster path (e.g. "/abc123.jpg"), or null if none.</summary>
    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; }

    /// <summary>Gets or sets the movie title (movies only).</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the TV show name (TV only).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the movie release date, "YYYY-MM-DD" (movies only).</summary>
    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    /// <summary>Gets or sets the TV show's first air date, "YYYY-MM-DD" (TV only).</summary>
    [JsonPropertyName("firstAirDate")]
    public string? FirstAirDate { get; set; }

    /// <summary>Gets the best available display title (movie title or TV name).</summary>
    [JsonIgnore]
    public string? DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : Name;

    /// <summary>Gets the release/air year parsed from whichever date field applies, or null.</summary>
    [JsonIgnore]
    public int? Year
    {
        get
        {
            var date = !string.IsNullOrWhiteSpace(ReleaseDate) ? ReleaseDate : FirstAirDate;
            return !string.IsNullOrWhiteSpace(date) && date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), out var year)
                ? year
                : null;
        }
    }
}

/// <summary>
/// Represents the media availability status in Overseerr/Jellyseerr.
/// </summary>
public enum SeerrMediaStatus
{
    /// <summary>Status is unknown.</summary>
    Unknown = 1,

    /// <summary>Media is pending processing.</summary>
    Pending = 2,

    /// <summary>Media is currently being processed/downloaded.</summary>
    Processing = 3,

    /// <summary>Media is partially available (e.g., some episodes).</summary>
    PartiallyAvailable = 4,

    /// <summary>Media is fully available.</summary>
    Available = 5,

    /// <summary>Media has been deleted.</summary>
    Deleted = 6,

    /// <summary>Media is blocklisted.</summary>
    Blocklisted = 7
}

/// <summary>
/// Represents the request status in Overseerr/Jellyseerr.
/// </summary>
public enum SeerrRequestStatus
{
    /// <summary>Request is pending approval.</summary>
    PendingApproval = 1,

    /// <summary>Request has been approved.</summary>
    Approved = 2,

    /// <summary>Request has been declined.</summary>
    Declined = 3,

    /// <summary>Request has failed processing.</summary>
    Failed = 4
}

/// <summary>
/// Response from the Seerr status/test endpoint.
/// </summary>
public class SeerrTestResponse
{
    /// <summary>Gets or sets the Seerr version string.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// A paged result set from the Seerr API.
/// </summary>
/// <typeparam name="T">The type of items in the result set.</typeparam>
public class SeerrPagedResult<T>
{
    /// <summary>Gets or sets the pagination metadata.</summary>
    [JsonPropertyName("pageInfo")]
    public SeerrPageInfo PageInfo { get; set; } = new();

    /// <summary>Gets or sets the list of result items.</summary>
    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = new();
}

/// <summary>
/// Pagination metadata for Seerr API responses.
/// </summary>
public class SeerrPageInfo
{
    /// <summary>Gets or sets the total number of pages.</summary>
    [JsonPropertyName("pages")]
    public int Pages { get; set; }

    /// <summary>Gets or sets the number of items per page.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    /// <summary>Gets or sets the total number of results.</summary>
    [JsonPropertyName("results")]
    public int Results { get; set; }

    /// <summary>Gets or sets the current page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }
}

/// <summary>
/// Represents Seerr's single webhook notification agent configuration, as returned by and
/// submitted to <c>GET</c>/<c>POST api/v1/settings/notifications/webhook</c>. Unlike Sonarr/Radarr's
/// Connect notifications, Seerr only has one webhook slot — there is no list of independent
/// connections.
/// </summary>
public class SeerrWebhookSettings
{
    /// <summary>Gets or sets a value indicating whether the webhook agent is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the notification-type bitmask (see Seerr's <c>Notification</c> enum).</summary>
    [JsonPropertyName("types")]
    public int Types { get; set; }

    /// <summary>Gets or sets the webhook-specific options.</summary>
    [JsonPropertyName("options")]
    public SeerrWebhookOptions Options { get; set; } = new();
}

/// <summary>
/// Options nested under <see cref="SeerrWebhookSettings"/>.
/// </summary>
public class SeerrWebhookOptions
{
    /// <summary>Gets or sets the URL Seerr posts the webhook payload to.</summary>
    [JsonPropertyName("webhookUrl")]
    public string? WebhookUrl { get; set; }

    /// <summary>Gets or sets the JSON payload template, base64-encoded (Seerr's own wire format).</summary>
    [JsonPropertyName("jsonPayload")]
    public string? JsonPayload { get; set; }

    /// <summary>Gets or sets an optional Authorization header value sent with each webhook call.</summary>
    [JsonPropertyName("authHeader")]
    public string? AuthHeader { get; set; }
}
