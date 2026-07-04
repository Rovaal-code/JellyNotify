using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Stores per-user notification preferences including which event types
/// and delivery channels are enabled.
/// </summary>
public class UserNotificationPreference
{
    /// <summary>Gets or sets the Jellyfin user ID these preferences belong to.</summary>
    [JsonPropertyName("jellyfinUserId")]
    public string JellyfinUserId { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to notify on new requests.</summary>
    [JsonPropertyName("notifyOnRequest")]
    public bool NotifyOnRequest { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to notify on request approvals.</summary>
    [JsonPropertyName("notifyOnApproval")]
    public bool NotifyOnApproval { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to notify on download events.</summary>
    [JsonPropertyName("notifyOnDownload")]
    public bool NotifyOnDownload { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to notify when media becomes available.</summary>
    [JsonPropertyName("notifyOnAvailable")]
    public bool NotifyOnAvailable { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to notify on media issues.</summary>
    [JsonPropertyName("notifyOnIssue")]
    public bool NotifyOnIssue { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to notify when media is partially available.</summary>
    [JsonPropertyName("notifyOnPartiallyAvailable")]
    public bool NotifyOnPartiallyAvailable { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether in-app Jellyfin UI notifications are enabled.</summary>
    [JsonPropertyName("jellyfinUiEnabled")]
    public bool JellyfinUiEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the preferred language for notifications (auto, en-US, es-ES, ca).
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "auto";

    /// <summary>
    /// Gets or sets the concrete language the frontend last resolved "auto" to for this
    /// user (e.g. the Jellyfin display language it detected). Only meaningful when
    /// <see cref="Language"/> is "auto" — server-only code (bot welcome messages, test
    /// notifications) has no browser to inspect, so it falls back to this cached value
    /// instead of always defaulting to English. Updated silently by the client via
    /// <c>POST /JellyNotify/preferences/resolved-language</c>, not by the regular save flow.
    /// </summary>
    [JsonPropertyName("resolvedLanguage")]
    public string? ResolvedLanguage { get; set; }

    /// <summary>Gets or sets a value indicating whether notification sounds are enabled.</summary>
    [JsonPropertyName("soundEnabled")]
    public bool SoundEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this user has ever opened the
    /// notification bell's panel. While false, the bell pulses to draw attention to
    /// it; the first time the user opens the panel, the client shows a short guided
    /// tour and then silently marks this true via
    /// <c>POST /JellyNotify/preferences/bell-opened</c> — not by the regular save flow.
    /// </summary>
    [JsonPropertyName("hasOpenedBell")]
    public bool HasOpenedBell { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user has a linked WhatsApp
    /// number. This is computed by <c>GET /JellyNotify/preferences</c> from the
    /// user's <see cref="UserChannelBinding"/> — it is not itself persisted by
    /// <c>PUT /JellyNotify/preferences</c> and is ignored if a caller sends it.
    /// </summary>
    [JsonPropertyName("whatsAppConnected")]
    public bool WhatsAppConnected { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user has a linked Telegram
    /// chat. Computed the same way as <see cref="WhatsAppConnected"/> — read-only
    /// from the client's perspective.
    /// </summary>
    [JsonPropertyName("telegramConnected")]
    public bool TelegramConnected { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user has a linked Discord
    /// account. Computed the same way as <see cref="WhatsAppConnected"/> — read-only
    /// from the client's perspective.
    /// </summary>
    [JsonPropertyName("discordConnected")]
    public bool DiscordConnected { get; set; }
}
