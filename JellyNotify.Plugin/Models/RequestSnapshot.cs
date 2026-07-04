using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Represents a point-in-time snapshot of a Seerr request's state,
/// used for detecting state changes and generating notifications.
/// </summary>
public class RequestSnapshot
{
    /// <summary>Gets or sets the unique identifier for this snapshot.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the Overseerr/Jellyseerr request ID.</summary>
    [JsonPropertyName("seerrRequestId")]
    public int SeerrRequestId { get; set; }

    /// <summary>Gets or sets the Jellyfin user ID associated with this request.</summary>
    [JsonPropertyName("jellyfinUserId")]
    public string JellyfinUserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Seerr user ID, if available.</summary>
    [JsonPropertyName("seerrUserId")]
    public string? SeerrUserId { get; set; }

    /// <summary>Gets or sets the media type (e.g., "movie", "tv").</summary>
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the title of the requested media.</summary>
    [JsonPropertyName("mediaTitle")]
    public string MediaTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the current status of the request.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the external identifiers for the media.</summary>
    [JsonPropertyName("externalIds")]
    public ExternalIds ExternalIds { get; set; } = new();

    /// <summary>Gets or sets the timestamp of the last polling check.</summary>
    [JsonPropertyName("lastChecked")]
    public DateTime LastChecked { get; set; }

    /// <summary>Gets or sets the timestamp when this snapshot was first created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets a value indicating whether this is the initial baseline snapshot (no notifications generated).</summary>
    [JsonPropertyName("isBaseline")]
    public bool IsBaseline { get; set; }

    /// <summary>Gets or sets the name of the Sonarr/Radarr instance handling this request.</summary>
    [JsonPropertyName("arrInstanceName")]
    public string? ArrInstanceName { get; set; }

    /// <summary>Gets or sets the current status in the *arr system.</summary>
    [JsonPropertyName("arrStatus")]
    public string? ArrStatus { get; set; }

    /// <summary>Gets or sets the download progress percentage from the *arr system.</summary>
    [JsonPropertyName("arrProgress")]
    public double? ArrProgress { get; set; }

    /// <summary>
    /// Gets or sets the *arr-reported time remaining for the active download (e.g. "01:23:00"),
    /// straight from the queue API's own <c>timeleft</c> field — already fetched for free
    /// during the normal poll, just not persisted until now. Null when not downloading or
    /// when *arr hasn't estimated it yet (common right after a download starts).
    /// </summary>
    [JsonPropertyName("arrTimeLeft")]
    public string? ArrTimeLeft { get; set; }

    /// <summary>
    /// Gets or sets the quality/profile label for the active or last-seen download
    /// (e.g. "WEBDL-1080p"), from the *arr queue API's own quality field.
    /// </summary>
    [JsonPropertyName("arrQuality")]
    public string? ArrQuality { get; set; }

    /// <summary>
    /// Gets or sets the release year (movies) or first-air year (TV), resolved once via
    /// Seerr's movie/tv detail endpoints alongside the poster — same call, no extra cost.
    /// </summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the full poster image URL (TMDB CDN), fetched once via Seerr's
    /// movie/tv detail endpoints and cached here so every later notification for this
    /// request — including *arr-driven ones, which never talk to Seerr directly —
    /// can reuse it without an extra network call.
    /// </summary>
    [JsonPropertyName("posterUrl")]
    public string? PosterUrl { get; set; }

    // ── Per-status timestamps ──────────────────────────────────────────
    // Each is set the first time that status is observed and never overwritten
    // afterwards, so /status can show "requested on", "approved on", etc. even
    // after the request has since moved on to a later status. Only one of
    // DownloadStartedAt/PartiallyAvailableAt/AvailableAt/FailedAt is normally the
    // "current" one for display — RequestStatusSummaryBuilder picks whichever
    // matches the item's current bucket.

    /// <summary>Gets or sets when this request was first seen (approximates when it was made).</summary>
    [JsonPropertyName("requestedAt")]
    public DateTime? RequestedAt { get; set; }

    /// <summary>Gets or sets when the request was first observed as approved.</summary>
    [JsonPropertyName("approvedAt")]
    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// Gets or sets when the download was first observed as started/in-progress — from
    /// Seerr's own "Processing" media status, or from *arr's queue, whichever is seen first.
    /// </summary>
    [JsonPropertyName("downloadStartedAt")]
    public DateTime? DownloadStartedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the most recent *arr progress update — refreshed on
    /// every poll while actively downloading, unlike the other timestamps here which are
    /// set once. Used to show "updated at" next to the live percentage.
    /// </summary>
    [JsonPropertyName("arrLastProgressAt")]
    public DateTime? ArrLastProgressAt { get; set; }

    /// <summary>Gets or sets when the request was first observed as partially available.</summary>
    [JsonPropertyName("partiallyAvailableAt")]
    public DateTime? PartiallyAvailableAt { get; set; }

    /// <summary>Gets or sets when the request was first observed as fully available.</summary>
    [JsonPropertyName("availableAt")]
    public DateTime? AvailableAt { get; set; }

    /// <summary>Gets or sets when the request was first observed as failed/declined/blocklisted.</summary>
    [JsonPropertyName("failedAt")]
    public DateTime? FailedAt { get; set; }
}
