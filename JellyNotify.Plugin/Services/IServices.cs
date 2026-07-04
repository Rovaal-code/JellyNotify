using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Sends notifications via Discord webhooks.
/// </summary>
public interface IDiscordNotificationClient
{
    /// <summary>Sends a notification embed to the configured Discord webhook, using the notification's own (unenriched) message as the embed description. Used only for admin connection tests. Returns whether the webhook accepted it.</summary>
    Task<bool> SendAsync(NotificationEvent notification, CancellationToken cancellationToken = default);

    /// <summary>Sends a notification embed to the configured Discord webhook, using <paramref name="enrichedMessage"/> (built by <see cref="Services.NotificationCardFormatter"/>) as the embed description instead of the notification's raw message. Returns whether the webhook accepted it.</summary>
    Task<bool> SendAsync(NotificationEvent notification, string enrichedMessage, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends notifications via the Telegram Bot API.
/// </summary>
public interface ITelegramNotificationClient
{
    /// <summary>Sends a notification message to the specified Telegram chat, HTML-escaping the notification's own (unenriched) title/message. Used only for admin connection tests and other non-enriched paths. Returns whether the Bot API accepted it.</summary>
    Task<bool> SendAsync(NotificationEvent notification, string chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a notification message built from an already-HTML-escaped, already fully
    /// formatted <paramref name="htmlBody"/> (the <see cref="Services.NotificationCardFormatter"/>
    /// field block, complete with its own leading "Estado" field — there is no separate
    /// title to wrap here anymore), attaching <paramref name="thumbnailUrl"/> as a photo when present.
    /// </summary>
    Task<bool> SendAsync(string htmlBody, string? thumbnailUrl, string chatId, string notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends already-formatted HTML directly to the specified Telegram chat, with no
    /// title wrapping and no escaping — unlike <see cref="SendAsync(NotificationEvent, string, CancellationToken)"/>, which always
    /// HTML-escapes both the title and message (safe for arbitrary notification text,
    /// but would also escape away any intentional &lt;b&gt; tags). Callers are responsible
    /// for having already escaped any untrusted dynamic values they interpolate.
    /// </summary>
    Task<bool> SendHtmlAsync(string html, string chatId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends direct messages to individual Discord users via the Discord Bot REST API
/// (no Gateway/WebSocket connection required — DM channel creation and message
/// sending both work over plain REST calls with a bot token). Used for the
/// per-user "Connect Discord" flow, distinct from the admin-only global webhook.
/// </summary>
public interface IDiscordDmClient
{
    /// <summary>
    /// Opens (or reuses) a DM channel with the given Discord user and sends a plain-text message.
    /// Returns false if the bot token is not configured, the user ID is invalid, or Discord rejects
    /// the request (e.g. the bot shares no server with that user, which Discord requires for DMs).
    /// </summary>
    Task<bool> SendDirectMessageAsync(string discordUserId, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens (or reuses) a DM channel and sends a structured notification: a rich embed
    /// with <paramref name="message"/> (already fully formatted, own leading "Estado" field
    /// included — no separate title) as its description, and a poster thumbnail when
    /// <paramref name="thumbnailUrl"/> is given. Same failure conditions as <see cref="SendDirectMessageAsync"/>.
    /// </summary>
    Task<bool> SendEmbedAsync(string discordUserId, string message, string? thumbnailUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory store for download progress state from Sonarr/Radarr.
/// Tracks which downloads are being monitored to detect state transitions.
/// </summary>
public interface IDownloadProgressStore
{
    /// <summary>Gets the current tracked progress for a given download key.</summary>
    string? GetProgress(string downloadKey);

    /// <summary>Sets (upserts) the progress for a given download key.</summary>
    void SetProgress(string downloadKey, string status);

    /// <summary>Removes a download from tracking.</summary>
    void Remove(string downloadKey);

    /// <summary>Returns all currently tracked download keys.</summary>
    IReadOnlyList<string> GetAllKeys();
}

/// <summary>
/// Tracks notification deduplication state in-memory.
/// Prevents sending the same notification multiple times within a time window.
/// </summary>
public interface INotificationDeduplicationStore
{
    /// <summary>Returns true if a notification with the given key was already sent within the deduplication window.</summary>
    bool IsDuplicate(string key);

    /// <summary>Records a notification key as sent.</summary>
    void Record(string key, TimeSpan window);

    /// <summary>Clears all deduplication records (e.g., on service restart).</summary>
    void Clear();
}

/// <summary>
/// Orchestrates dispatching a notification to all configured channels for a user.
/// Handles in-app storage, Discord, and Telegram delivery.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>Dispatches a notification event to all appropriate channels for the target user.</summary>
    Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages media request polling and state comparison with Overseerr/Jellyseerr.
/// Generates <see cref="NotificationEvent"/> instances for each detected state change.
/// </summary>
public interface IMediaRequestService
{
    /// <summary>Polls Seerr for all requests and emits notifications for any state changes.</summary>
    Task PollAndProcessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and processes a single Seerr request by ID, emitting a notification if its
    /// state changed since the last known snapshot — the reactive counterpart to
    /// <see cref="PollAndProcessAsync"/>'s full sweep, used by the Seerr webhook receiver
    /// for instant delivery. A no-op if the request can't be fetched or Seerr is disabled.
    /// </summary>
    Task ProcessSingleRequestAsync(int seerrRequestId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates polling Sonarr and Radarr for download progress updates
/// and correlates them to Jellyfin users via snapshot data.
/// </summary>
public interface IArrSyncService
{
    /// <summary>Polls all configured *arr instances and dispatches progress notifications.</summary>
    Task PollAllAsync(CancellationToken cancellationToken = default);
}
