using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Root payload Overseerr/Jellyseerr's own "Webhook" notification agent sends. The admin
/// configures the JSON body as a Handlebars-style template in Seerr's UI (defaulting to a
/// well-known shape) — only <see cref="Request"/>'s <c>request_id</c> is read here, since
/// the handler re-fetches the request fresh from Seerr's API rather than trusting any other
/// field in the payload, making this tolerant of template variations as long as that one
/// field survives.
/// </summary>
public sealed class SeerrWebhookPayload
{
    /// <summary>Gets or sets the event type (e.g. "MEDIA_APPROVED", "MEDIA_AVAILABLE", "TEST_NOTIFICATION"). Not used for routing — informational only, since the affected request is always re-fetched fresh.</summary>
    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    /// <summary>Gets or sets the request this event applies to, if any (absent for issue/test events).</summary>
    [JsonPropertyName("request")]
    public SeerrWebhookRequestInfo? Request { get; set; }
}

/// <summary>The request-identifying portion of a Seerr webhook payload.</summary>
public sealed class SeerrWebhookRequestInfo
{
    /// <summary>Gets or sets the Seerr request ID. Rendered as a string by Seerr's Handlebars-style template, even though it's numeric — parse with <see cref="int.TryParse(string?, out int)"/>.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }
}
