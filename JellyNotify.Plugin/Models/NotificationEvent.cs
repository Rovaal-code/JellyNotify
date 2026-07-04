using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Represents a single notification event to be delivered to a user.
/// </summary>
public class NotificationEvent
{
    /// <summary>Gets or sets the unique identifier for this notification event.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the Jellyfin user ID this notification targets.</summary>
    [JsonPropertyName("jellyfinUserId")]
    public string JellyfinUserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the type of notification.</summary>
    [JsonPropertyName("type")]
    public NotificationType Type { get; set; }

    /// <summary>Gets or sets the notification title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the notification message body.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the title of the associated media, if any.</summary>
    [JsonPropertyName("mediaTitle")]
    public string? MediaTitle { get; set; }

    /// <summary>Gets or sets the media type (e.g., "movie", "tv"), if any.</summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>Gets or sets external service identifiers for the media.</summary>
    [JsonPropertyName("externalIds")]
    public ExternalIds? ExternalIds { get; set; }

    /// <summary>Gets or sets the Overseerr/Jellyseerr request ID, if applicable.</summary>
    [JsonPropertyName("seerrRequestId")]
    public int? SeerrRequestId { get; set; }

    /// <summary>Gets or sets the name of the Sonarr/Radarr instance, if applicable.</summary>
    [JsonPropertyName("arrInstanceName")]
    public string? ArrInstanceName { get; set; }

    /// <summary>Gets or sets the timestamp when this event was created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets a value indicating whether this notification has been read.</summary>
    [JsonPropertyName("isRead")]
    public bool IsRead { get; set; } = false;

    /// <summary>Gets or sets the timestamp when this notification was read.</summary>
    [JsonPropertyName("readAt")]
    public DateTime? ReadAt { get; set; }

    /// <summary>Gets or sets the previous state before the event (for state-change notifications).</summary>
    [JsonPropertyName("previousState")]
    public string? PreviousState { get; set; }

    /// <summary>Gets or sets the new state after the event (for state-change notifications).</summary>
    [JsonPropertyName("newState")]
    public string? NewState { get; set; }

    /// <summary>Gets or sets the thumbnail URL for the associated media.</summary>
    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; }

    /// <summary>Gets or sets the release year (movies) or first-air year (TV), if known.</summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>Gets or sets the download progress percentage (0-100), for download-related events.</summary>
    [JsonPropertyName("progressPercent")]
    public double? ProgressPercent { get; set; }

    /// <summary>Gets or sets the *arr-reported time remaining for the download (e.g. "01:23:00"), if known.</summary>
    [JsonPropertyName("etaRaw")]
    public string? EtaRaw { get; set; }

    /// <summary>Gets or sets the quality/profile label (e.g. "WEBDL-1080p"), if known.</summary>
    [JsonPropertyName("quality")]
    public string? Quality { get; set; }

    /// <summary>Gets or sets a human-readable failure reason, for failed/blocklisted events.</summary>
    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; set; }

    /// <summary>Gets or sets the audio track languages (e.g. "en, es"), comma-joined. Only populated when the notification originates from the *arr Connect webhook, which embeds this for free — the polling path does not make an extra call for it.</summary>
    [JsonPropertyName("audioLanguages")]
    public string? AudioLanguages { get; set; }

    /// <summary>Gets or sets the subtitle track languages (e.g. "en, es, fr"), comma-joined. Same webhook-only availability as <see cref="AudioLanguages"/>.</summary>
    [JsonPropertyName("subtitleLanguages")]
    public string? SubtitleLanguages { get; set; }

    /// <summary>Gets or sets the season number that just became (partially) available, for TV MediaPartiallyAvailable events.</summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }
}
