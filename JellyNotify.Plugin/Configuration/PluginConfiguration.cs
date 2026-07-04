using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyNotify.Configuration;

/// <summary>
/// Plugin configuration for JellyNotify.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        SeerrSettings = new SeerrSettings();
        SonarrInstances = new List<ArrInstanceConfig>();
        RadarrInstances = new List<ArrInstanceConfig>();
        NotificationSettings = new NotificationSettings();
        ExternalChannelSettings = new ExternalChannelSettings();
    }

    /// <summary>
    /// Gets or sets the Overseerr/Jellyseerr connection settings.
    /// </summary>
    public SeerrSettings SeerrSettings { get; set; }

    /// <summary>
    /// Gets or sets the list of Sonarr instance configurations.
    /// </summary>
    public List<ArrInstanceConfig> SonarrInstances { get; set; }

    /// <summary>
    /// Gets or sets the list of Radarr instance configurations.
    /// </summary>
    public List<ArrInstanceConfig> RadarrInstances { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Sonarr/Radarr Connect webhook calls are
    /// accepted for instant notification delivery, in addition to polling. A single shared
    /// endpoint/secret covers every configured Sonarr and Radarr instance — paste the same
    /// URL into each instance's Settings → Connect that you want instant delivery from.
    /// </summary>
    public bool ArrWebhookEnabled { get; set; }

    /// <summary>
    /// Gets or sets the token embedded in the *arr webhook URL path that authenticates
    /// inbound calls (Sonarr/Radarr's Connect webhook has no custom-header auth, so an
    /// unguessable URL segment is the only practical secret). Generated lazily server-side
    /// on first access (see <c>AdminController.GetConfig</c>) rather than in this class's
    /// constructor — same reasoning as <see cref="SeerrSettings.WebhookSecret"/>.
    /// </summary>
    public string ArrWebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the shared *arr webhook endpoint last received a validated call
    /// (from any configured Sonarr/Radarr instance, including a "Test" event) — shown in
    /// the admin UI so clicking Test in Sonarr/Radarr's Connect settings has a visible
    /// confirmation that JellyNotify actually received it. Null until the first call.
    /// </summary>
    public DateTime? ArrWebhookLastReceivedAt { get; set; }

    /// <summary>
    /// Gets or sets the notification delivery settings.
    /// </summary>
    public NotificationSettings NotificationSettings { get; set; }

    /// <summary>
    /// Gets or sets the external channel settings (Discord, Telegram, etc.).
    /// </summary>
    public ExternalChannelSettings ExternalChannelSettings { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of notifications to retain per user.
    /// Oldest notifications are pruned when this limit is exceeded. 0 = unlimited.
    /// </summary>
    public int MaxNotificationsPerUser { get; set; } = 200;

    /// <summary>
    /// Gets or sets the default language for notifications (auto, en-US, es-ES).
    /// </summary>
    public string DefaultLanguage { get; set; } = "auto";

    /// <summary>
    /// Gets or sets the notification retention period in days. 0 disables age-based purging.
    /// </summary>
    public int NotificationRetentionDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the UTC timestamp of the last successful background sync.
    /// </summary>
    public DateTime? LastSuccessfulSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets the last background sync error summary.
    /// </summary>
    public string? LastSyncError { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the global web script-injection
    /// middleware (bell/panel in Jellyfin Web) is disabled. Off by default.
    /// </summary>
    public bool DisableScriptInjectionMiddleware { get; set; }

    /// <summary>
    /// Gets or sets the WhatsApp (wa.me link mode) channel settings.
    /// </summary>
    public WhatsAppChannelSettings WhatsAppSettings { get; set; } = new();

    /// <summary>
    /// Preserves existing secret values in the persisted configuration when
    /// the incoming payload does not supply them (i.e., empty/null).
    /// This prevents the common frontend pattern of not re-sending secrets
    /// from accidentally wiping previously configured credentials.
    /// </summary>
    public static void PreserveSecrets(PluginConfiguration existing, PluginConfiguration incoming)
    {
        incoming.SeerrSettings ??= new SeerrSettings();
        incoming.ExternalChannelSettings ??= new ExternalChannelSettings();
        incoming.ExternalChannelSettings.DiscordSettings ??= new DiscordChannelSettings();
        incoming.ExternalChannelSettings.TelegramSettings ??= new TelegramChannelSettings();
        incoming.SonarrInstances ??= new List<ArrInstanceConfig>();
        incoming.RadarrInstances ??= new List<ArrInstanceConfig>();
        incoming.NotificationSettings ??= new NotificationSettings();
        incoming.WhatsAppSettings ??= new WhatsAppChannelSettings();

        // Seerr API key
        if (string.IsNullOrWhiteSpace(incoming.SeerrSettings?.ApiKey))
        {
            if (incoming.SeerrSettings != null)
            {
                incoming.SeerrSettings.ApiKey = existing.SeerrSettings?.ApiKey ?? string.Empty;
            }
        }

        // *arr shared webhook secret — never resubmitted by the frontend (not an editable
        // field), so without this it would be blanked on every save, breaking the webhook
        // URL already pasted into every Sonarr/Radarr instance's Connect settings.
        if (string.IsNullOrWhiteSpace(incoming.ArrWebhookSecret))
        {
            incoming.ArrWebhookSecret = existing.ArrWebhookSecret;
        }

        // Seerr webhook secret — never resubmitted by the frontend (not an editable
        // field), so without this it would be blanked on every save, breaking the
        // webhook URL the admin already pasted into Overseerr/Jellyseerr.
        if (string.IsNullOrWhiteSpace(incoming.SeerrSettings?.WebhookSecret))
        {
            if (incoming.SeerrSettings != null)
            {
                incoming.SeerrSettings.WebhookSecret = existing.SeerrSettings?.WebhookSecret ?? string.Empty;
            }
        }

        // Discord webhook URL
        if (string.IsNullOrWhiteSpace(incoming.ExternalChannelSettings?.DiscordSettings?.WebhookUrl))
        {
            if (incoming.ExternalChannelSettings?.DiscordSettings != null)
            {
                incoming.ExternalChannelSettings.DiscordSettings.WebhookUrl =
                    existing.ExternalChannelSettings?.DiscordSettings?.WebhookUrl ?? string.Empty;
            }
        }

        // Telegram bot token
        if (string.IsNullOrWhiteSpace(incoming.ExternalChannelSettings?.TelegramSettings?.BotToken))
        {
            if (incoming.ExternalChannelSettings?.TelegramSettings != null)
            {
                incoming.ExternalChannelSettings.TelegramSettings.BotToken =
                    existing.ExternalChannelSettings?.TelegramSettings?.BotToken ?? string.Empty;
            }
        }

        // Discord bot token
        if (string.IsNullOrWhiteSpace(incoming.ExternalChannelSettings?.DiscordSettings?.BotToken))
        {
            if (incoming.ExternalChannelSettings?.DiscordSettings != null)
            {
                incoming.ExternalChannelSettings.DiscordSettings.BotToken =
                    existing.ExternalChannelSettings?.DiscordSettings?.BotToken ?? string.Empty;
            }
        }

        // Discord OAuth2 client secret
        if (string.IsNullOrWhiteSpace(incoming.ExternalChannelSettings?.DiscordSettings?.ClientSecret))
        {
            if (incoming.ExternalChannelSettings?.DiscordSettings != null)
            {
                incoming.ExternalChannelSettings.DiscordSettings.ClientSecret =
                    existing.ExternalChannelSettings?.DiscordSettings?.ClientSecret ?? string.Empty;
            }
        }

        // WhatsApp Cloud API access token
        if (string.IsNullOrWhiteSpace(incoming.WhatsAppSettings?.AccessToken))
        {
            if (incoming.WhatsAppSettings != null)
            {
                incoming.WhatsAppSettings.AccessToken = existing.WhatsAppSettings?.AccessToken ?? string.Empty;
            }
        }

        // Sonarr instance API keys — match by instance ID
        if (incoming.SonarrInstances != null && existing.SonarrInstances != null)
        {
            foreach (var incomingInst in incoming.SonarrInstances)
            {
                if (string.IsNullOrWhiteSpace(incomingInst.ApiKey))
                {
                    var existingInst = existing.SonarrInstances.FirstOrDefault(e =>
                        string.Equals(e.Id, incomingInst.Id, StringComparison.OrdinalIgnoreCase));
                    incomingInst.ApiKey = existingInst?.ApiKey ?? string.Empty;
                }
            }
        }

        // Radarr instance API keys — match by instance ID
        if (incoming.RadarrInstances != null && existing.RadarrInstances != null)
        {
            foreach (var incomingInst in incoming.RadarrInstances)
            {
                if (string.IsNullOrWhiteSpace(incomingInst.ApiKey))
                {
                    var existingInst = existing.RadarrInstances.FirstOrDefault(e =>
                        string.Equals(e.Id, incomingInst.Id, StringComparison.OrdinalIgnoreCase));
                    incomingInst.ApiKey = existingInst?.ApiKey ?? string.Empty;
                }
            }
        }

        // Webhook "last received" timestamps are written only by the webhook
        // controllers themselves (never by the config form), so an admin save must
        // always carry over whatever was already there rather than wiping it —
        // unconditional copy, not the blank-preserves-existing pattern used above.
        if (incoming.SeerrSettings != null)
        {
            incoming.SeerrSettings.LastWebhookReceivedAt = existing.SeerrSettings?.LastWebhookReceivedAt;
        }

        incoming.ArrWebhookLastReceivedAt = existing.ArrWebhookLastReceivedAt;
    }
}

/// <summary>
/// Connection settings for Overseerr or Jellyseerr.
/// </summary>
public class SeerrSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SeerrSettings"/> class.
    /// </summary>
    public SeerrSettings()
    {
        ServerUrl = string.Empty;
        ApiKey = string.Empty;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the Seerr integration is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Seerr server URL (e.g., http://localhost:5055).
    /// </summary>
    public string ServerUrl { get; set; }

    /// <summary>
    /// Gets or sets the Seerr API key.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Seerr server type.
    /// </summary>
    public SeerrType SeerrType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether SSL certificate validation is skipped.
    /// </summary>
    public bool IgnoreSslErrors { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Overseerr/Jellyseerr's own outbound webhook
    /// notification agent should be accepted for instant request-status delivery. Request
    /// status changes are driven by this webhook now, not by a recurring poll — see
    /// <c>JellyNotifyBackgroundService</c>'s one-time startup catch-up for the only remaining
    /// non-webhook check.
    /// </summary>
    public bool WebhookEnabled { get; set; }

    /// <summary>
    /// Gets or sets the token embedded in the webhook URL path that authenticates inbound
    /// calls (Overseerr/Jellyseerr's webhook agent has no custom-header auth, so an
    /// unguessable URL segment is the only practical secret). Generated lazily server-side
    /// on first access (see <c>AdminController.GetConfig</c>) rather than in this class's
    /// constructor — this config is persisted as XML, and a constructor-assigned value
    /// would be silently regenerated on every restart until the admin's first save, since
    /// XML deserialization only overwrites properties actually present in the saved file.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the Seerr webhook endpoint last received a validated call
    /// (including a TEST_NOTIFICATION from Seerr's own "Test" button) — shown in the
    /// admin UI so clicking Test has a visible confirmation JellyNotify received it.
    /// Null until the first call.
    /// </summary>
    public DateTime? LastWebhookReceivedAt { get; set; }
}

/// <summary>
/// Identifies the type of Seerr server.
/// </summary>
public enum SeerrType
{
    /// <summary>
    /// Overseerr server.
    /// </summary>
    Overseerr = 0,

    /// <summary>
    /// Jellyseerr server.
    /// </summary>
    Jellyseerr = 1,
}

/// <summary>
/// Configuration for a single Sonarr or Radarr instance.
/// </summary>
public class ArrInstanceConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArrInstanceConfig"/> class.
    /// </summary>
    public ArrInstanceConfig()
    {
        Id = Guid.NewGuid().ToString("N");
        Name = string.Empty;
        ServerUrl = string.Empty;
        ApiKey = string.Empty;
    }

    /// <summary>
    /// Gets or sets the unique identifier for this instance.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the display name for this instance.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this instance is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the server URL (e.g., http://localhost:8989).
    /// </summary>
    public string ServerUrl { get; set; }

    /// <summary>
    /// Gets or sets the API key for authentication.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether SSL certificate validation is skipped.
    /// </summary>
    public bool IgnoreSslErrors { get; set; }

    /// <summary>
    /// Gets or sets the polling interval in seconds for this instance's download queue.
    /// Sonarr/Radarr have no webhook event for "still downloading" (only grab/import), so
    /// there is no way to learn about progress/stalled/failed state except by asking — this
    /// governs only that check, not availability, which arrives instantly via the webhook.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 300;
}

/// <summary>
/// Settings controlling how and when notifications are sent.
/// </summary>
public class NotificationSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationSettings"/> class.
    /// </summary>
    public NotificationSettings()
    {
        // empty
    }

    /// <summary>
    /// Gets or sets a value indicating whether notifications are globally enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum interval in minutes between notifications for the same item.
    /// Prevents notification spam for batch imports.
    /// </summary>
    public int DeduplicationWindowMinutes { get; set; } = 10;
}

/// <summary>
/// Settings for external notification channels (Discord, Telegram, etc.).
/// </summary>
public class ExternalChannelSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalChannelSettings"/> class.
    /// </summary>
    public ExternalChannelSettings()
    {
        DiscordSettings = new DiscordChannelSettings();
        TelegramSettings = new TelegramChannelSettings();
    }

    /// <summary>
    /// Gets or sets the Discord notification channel settings.
    /// </summary>
    public DiscordChannelSettings DiscordSettings { get; set; }

    /// <summary>
    /// Gets or sets the Telegram notification channel settings.
    /// </summary>
    public TelegramChannelSettings TelegramSettings { get; set; }
}

/// <summary>
/// Discord webhook notification settings.
/// </summary>
public class DiscordChannelSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordChannelSettings"/> class.
    /// </summary>
    public DiscordChannelSettings()
    {
        WebhookUrl = string.Empty;
        BotUsername = "JellyNotify";
        AvatarUrl = string.Empty;
    }

    /// <summary>
    /// Gets or sets a value indicating whether Discord notifications are enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Discord webhook URL.
    /// </summary>
    public string WebhookUrl { get; set; }

    /// <summary>
    /// Gets or sets the Discord bot token used for per-user DM delivery (the
    /// "Connect Discord" flow). Distinct from the webhook above: the webhook
    /// is an admin-only global broadcast, while the bot token lets any user
    /// who links their Discord account receive personal notifications via DM.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth2 Client ID for the Discord application (same
    /// application the bot token belongs to). Not sensitive — used to build
    /// both the bot invite link and the "Login with Discord" authorize URL.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth2 Client Secret for the Discord application.
    /// Required server-side to exchange an authorization code for the user's
    /// Discord identity during the "Connect Discord" flow.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bot username displayed in Discord messages.
    /// </summary>
    public string BotUsername { get; set; }

    /// <summary>
    /// Gets or sets the avatar URL displayed in Discord messages.
    /// </summary>
    public string AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets the embed accent color as a hex value (e.g., #AA5CC3).
    /// </summary>
    public string EmbedColor { get; set; } = "#AA5CC3";
}

/// <summary>
/// Telegram bot notification settings.
/// </summary>
public class TelegramChannelSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TelegramChannelSettings"/> class.
    /// </summary>
    public TelegramChannelSettings()
    {
        BotToken = string.Empty;
        BotUsername = string.Empty;
        ChatId = string.Empty;
    }

    /// <summary>
    /// Gets or sets a value indicating whether Telegram notifications are enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Telegram bot token.
    /// </summary>
    public string BotToken { get; set; }

    /// <summary>
    /// Gets or sets the bot's public @username (without the @), used to build the
    /// https://t.me/{username}?start={token} deep link for the "Connect Telegram" flow.
    /// </summary>
    public string BotUsername { get; set; }

    /// <summary>
    /// Gets or sets the Telegram chat ID to send notifications to.
    /// </summary>
    public string ChatId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether link previews are disabled.
    /// </summary>
    public bool DisableLinkPreviews { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether notifications should be silent.
    /// </summary>
    public bool SilentMessages { get; set; }

    /// <summary>
    /// Gets or sets the Telegram message thread ID for sending to a specific topic.
    /// Null means the message goes to the general chat.
    /// </summary>
    public int? MessageThreadId { get; set; }
}

/// <summary>
/// WhatsApp channel settings. v1.0.3 only supports the wa.me link mode (no
/// automated sending) — WhatsApp Business Cloud API is not implemented yet.
/// </summary>
public class WhatsAppChannelSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WhatsAppChannelSettings"/> class.
    /// </summary>
    public WhatsAppChannelSettings()
    {
        PhoneNumber = string.Empty;
        AccessToken = string.Empty;
        PhoneNumberId = string.Empty;
        VerifyToken = string.Empty;
    }

    /// <summary>
    /// Gets or sets a value indicating whether WhatsApp is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the admin/bot WhatsApp phone number (E.164, no leading '+'),
    /// e.g. "34600123456". Used to build the wa.me link the user taps to start
    /// the connect flow — this must be the same number registered in the Meta
    /// Cloud API app (see <see cref="PhoneNumberId"/>).
    /// </summary>
    public string PhoneNumber { get; set; }

    /// <summary>
    /// Gets or sets the permanent (or long-lived) access token for the Meta
    /// WhatsApp Cloud API app. Required to actually send messages — without it,
    /// WhatsApp is link-only (no automated welcome message or notifications).
    /// </summary>
    public string AccessToken { get; set; }

    /// <summary>
    /// Gets or sets the Cloud API Phone Number ID (not the phone number itself —
    /// a separate identifier Meta assigns to the registered number, found in the
    /// Meta for Developers dashboard under WhatsApp -> API Setup).
    /// </summary>
    public string PhoneNumberId { get; set; }

    /// <summary>
    /// Gets or sets an admin-chosen arbitrary string used to verify the webhook
    /// subscription handshake with Meta (must match what's entered in the Meta
    /// app's Webhooks configuration page).
    /// </summary>
    public string VerifyToken { get; set; }
}
