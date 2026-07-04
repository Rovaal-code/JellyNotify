using Jellyfin.Plugin.JellyNotify.Models;
using Jellyfin.Plugin.JellyNotify.Services;
using Jellyfin.Plugin.JellyNotify.Store;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Dispatches a <see cref="NotificationEvent"/> to all configured delivery channels for the target user.
/// Channels: in-app Jellyfin notification store, Discord (global webhook + personal DM), Telegram
/// (per-user chat), WhatsApp (per-user Cloud API message).
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationStore _notificationStore;
    private readonly IUserPreferenceStore _preferenceStore;
    private readonly IUserChannelStore _channelStore;
    private readonly IDiscordNotificationClient _discord;
    private readonly IDiscordDmClient _discordDm;
    private readonly ITelegramNotificationClient _telegram;
    private readonly IWhatsAppCloudApiClient _whatsApp;
    private readonly INotificationDeduplicationStore _dedupStore;
    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<NotificationDispatcher> _logger;

    /// <summary>Initializes a new instance of the <see cref="NotificationDispatcher"/> class.</summary>
    public NotificationDispatcher(
        INotificationStore notificationStore,
        IUserPreferenceStore preferenceStore,
        IUserChannelStore channelStore,
        IDiscordNotificationClient discord,
        IDiscordDmClient discordDm,
        ITelegramNotificationClient telegram,
        IWhatsAppCloudApiClient whatsApp,
        INotificationDeduplicationStore dedupStore,
        IUserManager userManager,
        ISessionManager sessionManager,
        ILogger<NotificationDispatcher> logger)
    {
        _notificationStore = notificationStore;
        _preferenceStore = preferenceStore;
        _channelStore = channelStore;
        _discord = discord;
        _discordDm = discordDm;
        _telegram = telegram;
        _whatsApp = whatsApp;
        _dedupStore = dedupStore;
        _userManager = userManager;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (string.IsNullOrWhiteSpace(notification.JellyfinUserId))
        {
            _logger.LogWarning("Notification {Id} has no JellyfinUserId — not dispatching to avoid cross-user leakage", notification.Id);
            return;
        }

        var config = Plugin.Instance!.Configuration.NotificationSettings;
        if (!config.Enabled)
        {
            return;
        }

        // Global deduplication check. Skipped for TestNotification: it's an explicit,
        // repeatable user action (clicking "Send test notification"), not a data event
        // that could double-fire — the dedup key for it has no unique component
        // (SeerrRequestId/MediaTitle are both empty), so without this it silently
        // no-ops on every click after the first within the dedup window.
        if (notification.Type != NotificationType.TestNotification)
        {
            var dedupKey = $"{notification.JellyfinUserId}:{notification.Type}:{notification.SeerrRequestId}:{notification.MediaTitle}";
            var dedupWindow = TimeSpan.FromMinutes(config.DeduplicationWindowMinutes > 0 ? config.DeduplicationWindowMinutes : 10);
            if (_dedupStore.IsDuplicate(dedupKey))
            {
                _logger.LogDebug("Skipping duplicate notification {Key}", dedupKey);
                return;
            }

            _dedupStore.Record(dedupKey, dedupWindow);
        }

        _logger.LogInformation(
            "Notification {Id} ({Type}) state change detected for user {UserId}: {Previous} -> {New}",
            notification.Id, notification.Type, notification.JellyfinUserId, notification.PreviousState, notification.NewState);

        // Get per-user preferences
        var prefs = await _preferenceStore.GetByUserAsync(notification.JellyfinUserId).ConfigureAwait(false);
        var language = NotificationLanguage.Resolve(prefs);

        // 1. In-app Jellyfin notification (always stored for UI display) — stores the
        // bare base message, never enriched: the bell has no room for a field block, and
        // its markup is a plain passthrough of this string (see jellynotify.js).
        if (prefs.JellyfinUiEnabled && ShouldNotify(notification.Type, prefs))
        {
            await _notificationStore.AddAsync(notification).ConfigureAwait(false);
            _logger.LogInformation("Stored in-app notification {Id} ({Type}) for user {UserId}", notification.Id, notification.Type, notification.JellyfinUserId);
            await PushToOpenSessionsAsync(notification, cancellationToken).ConfigureAwait(false);
        }

        if (!ShouldNotify(notification.Type, prefs))
        {
            return;
        }

        // External channels have no separate "enabled" toggle anymore — being
        // connected (see UserChannelBinding) is the only switch. Connect the bot to
        // start receiving; disconnect it to stop. One lookup covers all three.
        var binding = await _channelStore.GetByUserAsync(notification.JellyfinUserId).ConfigureAwait(false);

        // 2. Discord — the global webhook broadcast (admin-only; nothing to "connect"
        // to, so it always reaches admins) and the personal bot DM (gated purely on
        // the user having connected their own Discord account) are independent
        // delivery paths and can both fire for the same notification. Both share the
        // same Discord-formatted (Markdown **bold**) enriched copy.
        var discordMessage = NotificationCardFormatter.Enrich(notification, language, NotificationChannel.Discord);

        try
        {
            if (Guid.TryParse(notification.JellyfinUserId, out var userGuid))
            {
                var user = _userManager.GetUserById(userGuid);
                var isUserAdmin = user != null && user.Permissions.Any(p => p.Kind.ToString() == "IsAdministrator" && p.Value);
                if (isUserAdmin)
                {
                    await _discord.SendAsync(notification, discordMessage, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking user admin status for Discord notification");
        }

        if (binding?.DiscordUserId is not null)
        {
            await DispatchToProviderAsync("Discord", notification, () =>
                _discordDm.SendEmbedAsync(binding.DiscordUserId, discordMessage, notification.ThumbnailUrl, cancellationToken)).ConfigureAwait(false);
        }

        // 3. Telegram (per-user chat binding) — HTML parse mode. The formatter's output is
        // the entire body now (leading "Estado" field + the type's own fields) — no
        // separate title/message to combine here anymore.
        if (binding?.TelegramChatId is not null)
        {
            var telegramBody = NotificationCardFormatter.Enrich(notification, language, NotificationChannel.Telegram);

            await DispatchToProviderAsync("Telegram", notification, () =>
                _telegram.SendAsync(telegramBody, notification.ThumbnailUrl, binding.TelegramChatId, notification.Id, cancellationToken)).ConfigureAwait(false);
        }

        // 4. WhatsApp (per-user Cloud API message — only works once the admin has
        // configured real Cloud API credentials; link-only setups never populate
        // WhatsAppPhoneNumber, so this is a no-op for them). WhatsApp markdown is
        // single-asterisk *bold*, not Discord/Telegram's double-asterisk/HTML. The
        // formatter's output is the entire body now — no separate title to prepend.
        if (binding?.WhatsAppPhoneNumber is not null)
        {
            var text = NotificationCardFormatter.Enrich(notification, language, NotificationChannel.WhatsApp);
            if (notification.ThumbnailUrl is not null)
            {
                // Only the image-message path enforces WhatsApp's ~1024-char caption cap;
                // the plain-text fallback (no poster) allows up to 4096.
                text = CaptionTruncator.TruncateForPhotoCaption(text);
            }

            await DispatchToProviderAsync("WhatsApp", notification, () =>
                _whatsApp.SendTextMessageAsync(binding.WhatsAppPhoneNumber, text, notification.ThumbnailUrl, cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs one provider's send call with structured start/success/failure logging, and
    /// contains any exception so a problem with one provider (a bug, an unexpected null,
    /// a transport-level throw the client itself didn't catch) can never prevent the
    /// other providers below it in <see cref="DispatchAsync"/> from still being tried.
    /// Providers already catch their own expected failure modes and return false rather
    /// than throw — this is deliberately a second, defensive layer, not the primary one.
    /// </summary>
    private async Task DispatchToProviderAsync(string providerName, NotificationEvent notification, Func<Task<bool>> send)
    {
        _logger.LogDebug("Dispatching notification {Id} to {Provider}", notification.Id, providerName);
        try
        {
            var success = await send().ConfigureAwait(false);
            if (success)
            {
                _logger.LogInformation("Notification {Id} delivered to {Provider}", notification.Id, providerName);
            }
            else
            {
                _logger.LogWarning("Notification {Id} was rejected by {Provider}", notification.Id, providerName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notification {Id} failed to dispatch to {Provider}", notification.Id, providerName);
        }
    }

    /// <summary>
    /// Pushes the notification over Jellyfin's own WebSocket to any of this user's
    /// currently-open browser tabs, so the bell's toast/badge appear instantly instead of
    /// waiting for the client's own periodic poll (up to 30s later). This is additive, not
    /// a replacement: the poll still runs, so a tab open before this fires, offline at the
    /// moment it's sent, or on a client that doesn't handle it yet still sees the
    /// notification on its next scheduled check.
    /// <see cref="SessionMessageType"/> is a fixed, closed enum with no "custom app
    /// notification" member of its own — <see cref="SessionMessageType.UserDataChanged"/> is
    /// reused as a low-traffic, semantically-neutral envelope, with a <c>source</c> marker
    /// in the payload so the client can tell an actual UserDataChanged event apart from this.
    /// </summary>
    private async Task PushToOpenSessionsAsync(NotificationEvent notification, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(notification.JellyfinUserId, out var userGuid))
        {
            return;
        }

        try
        {
            await _sessionManager.SendMessageToUserSessions(
                new List<Guid> { userGuid },
                SessionMessageType.UserDataChanged,
                new
                {
                    source = "JellyNotify",
                    id = notification.Id,
                    title = notification.Title,
                    message = notification.Message,
                    type = notification.Type.ToString()
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not push instant in-app notification {Id} over WebSocket — it will still arrive on the next poll", notification.Id);
        }
    }

    private static bool ShouldNotify(NotificationType type, UserNotificationPreference prefs) =>
        type switch
        {
            NotificationType.RequestCreated => prefs.NotifyOnRequest,
            NotificationType.RequestApproved => prefs.NotifyOnApproval,
            NotificationType.RequestDeclined => prefs.NotifyOnApproval,
            NotificationType.RequestFailed => prefs.NotifyOnApproval,
            NotificationType.DownloadStarted => prefs.NotifyOnDownload,
            NotificationType.DownloadProgress => prefs.NotifyOnDownload,
            NotificationType.DownloadWarning => prefs.NotifyOnDownload,
            NotificationType.DownloadFailed => prefs.NotifyOnDownload,
            NotificationType.MediaAvailable => prefs.NotifyOnAvailable,
            NotificationType.MediaPartiallyAvailable => prefs.NotifyOnPartiallyAvailable,
            NotificationType.IssueWarning => prefs.NotifyOnIssue,
            NotificationType.IssueResolved => prefs.NotifyOnIssue,
            NotificationType.TestNotification => true,
            _ => true
        };
}
