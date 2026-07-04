using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyNotify.Models;
using Jellyfin.Plugin.JellyNotify.Services;
using Jellyfin.Plugin.JellyNotify.Store;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Api;

/// <summary>
/// REST API controller for in-app user notifications.
/// All endpoints require an authenticated Jellyfin session.
/// Notifications are scoped to the requesting user — admins cannot read other users' notifications
/// via this endpoint; they use the admin endpoint instead.
/// </summary>
[ApiController]
[Route("JellyNotify")]
[Produces("application/json")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationStore _store;
    private readonly IUserPreferenceStore _preferenceStore;
    private readonly IUserChannelStore _channelStore;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ITelegramNotificationClient _telegram;
    private readonly IDiscordDmClient _discordDm;
    private readonly IDiscordOAuthClient _discordOAuth;
    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<NotificationsController> _logger;

    /// <summary>Initializes a new instance of the <see cref="NotificationsController"/> class.</summary>
    public NotificationsController(
        INotificationStore store,
        IUserPreferenceStore preferenceStore,
        IUserChannelStore channelStore,
        INotificationDispatcher dispatcher,
        ITelegramNotificationClient telegram,
        IDiscordDmClient discordDm,
        IDiscordOAuthClient discordOAuth,
        IUserManager userManager,
        IServerApplicationHost appHost,
        ILogger<NotificationsController> logger)
    {
        _store = store;
        _preferenceStore = preferenceStore;
        _channelStore = channelStore;
        _dispatcher = dispatcher;
        _telegram = telegram;
        _discordDm = discordDm;
        _discordOAuth = discordOAuth;
        _userManager = userManager;
        _appHost = appHost;
        _logger = logger;
    }

    /// <summary>
    /// Gets all notifications for the currently authenticated user.
    /// </summary>
    [HttpGet("notifications")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationEvent>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<NotificationEvent>>> GetNotifications()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var notifications = await _store.GetByUserAsync(userId).ConfigureAwait(false);
        return Ok(notifications);
    }

    /// <summary>
    /// Gets the unread notification count for the currently authenticated user.
    /// Used by the bell badge in the UI.
    /// </summary>
    [HttpGet("notifications/unread-count")]
    [Authorize]
    [ProducesResponseType(typeof(UnreadCountResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountResponse>> GetUnreadCount()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var count = await _store.GetUnreadCountAsync(userId).ConfigureAwait(false);
        return Ok(new UnreadCountResponse { UnreadCount = count });
    }

    /// <summary>
    /// Marks all notifications as read for the currently authenticated user.
    /// </summary>
    [HttpPost("notifications/mark-all-read")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        await _store.MarkAllReadAsync(userId).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Clears all notifications for the currently authenticated user.
    /// </summary>
    [HttpDelete("notifications")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearAll()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        await _store.ClearAsync(userId).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Gets the notification preferences for the currently authenticated user.
    /// </summary>
    [HttpGet("preferences")]
    [Authorize]
    [ProducesResponseType(typeof(UserNotificationPreference), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserNotificationPreference>> GetPreferences()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var prefs = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        var binding = await _channelStore.GetByUserAsync(userId).ConfigureAwait(false);
        prefs.WhatsAppConnected = binding?.WhatsAppLinkedAt is not null;
        prefs.TelegramConnected = binding?.TelegramLinkedAt is not null;
        prefs.DiscordConnected = binding?.DiscordLinkedAt is not null;
        return Ok(prefs);
    }

    /// <summary>
    /// Gets non-sensitive settings needed by the regular user UI.
    /// This endpoint intentionally does not expose integration URLs, API keys, webhooks,
    /// bot tokens, or the full administrator configuration.
    /// </summary>
    [HttpGet("public-settings")]
    [Authorize]
    [ProducesResponseType(typeof(PublicSettingsResponse), StatusCodes.Status200OK)]
    public ActionResult<PublicSettingsResponse> GetPublicSettings()
    {
        var config = Plugin.Instance!.Configuration;
        var telegram = config.ExternalChannelSettings.TelegramSettings;
        var discord = config.ExternalChannelSettings.DiscordSettings;

        return Ok(new PublicSettingsResponse
        {
            DefaultLanguage = NormalizeLanguage(config.DefaultLanguage),
            TelegramAvailable = telegram.Enabled && !string.IsNullOrWhiteSpace(telegram.BotToken) && !string.IsNullOrWhiteSpace(telegram.BotUsername),
            // The webhook (config.discordEnabled) stays a separate global/admin-only
            // broadcast. A configured bot token + OAuth client ID/secret is what
            // unlocks the personal per-user "Connect Discord" flow advertised here.
            DiscordAvailable = !string.IsNullOrWhiteSpace(discord.BotToken)
                && !string.IsNullOrWhiteSpace(discord.ClientId)
                && !string.IsNullOrWhiteSpace(discord.ClientSecret),
            WhatsAppAvailable = config.WhatsAppSettings.Enabled && !string.IsNullOrWhiteSpace(config.WhatsAppSettings.PhoneNumber),
            JellyfinUiEnabled = config.NotificationSettings.Enabled
        });
    }

    /// <summary>
    /// Updates the notification preferences for the currently authenticated user.
    /// </summary>
    [HttpPut("preferences")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UserNotificationPreference preferences)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(preferences.JellyfinUserId))
        {
            preferences.JellyfinUserId = userId;
        }

        // Security: users can only update their own preferences
        if (!string.Equals(preferences.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("You cannot modify another user's preferences.");
        }

        preferences.Language = NormalizeLanguage(preferences.Language);

        // WhatsAppConnected/TelegramConnected/DiscordConnected are read-only fields
        // computed from the channel binding store (see GetPreferences) — never trust
        // a client-submitted value for any of them.
        preferences.WhatsAppConnected = false;
        preferences.TelegramConnected = false;
        preferences.DiscordConnected = false;

        // ResolvedLanguage is maintained exclusively by POST /preferences/resolved-language
        // (see below), not by this regular save flow. UpsertAsync replaces the whole
        // record, so preserve whatever was already stored instead of wiping it just
        // because this particular save request didn't carry it.
        var existing = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        preferences.ResolvedLanguage = existing.ResolvedLanguage;

        // Same reasoning as ResolvedLanguage above: HasOpenedBell is maintained
        // exclusively by POST /preferences/bell-opened, not this regular save flow.
        preferences.HasOpenedBell = existing.HasOpenedBell;

        await _preferenceStore.UpsertAsync(preferences).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Silently records the concrete language the frontend just resolved "auto" to for
    /// this user (e.g. the Jellyfin display language it detected via
    /// <c>detectJellyfinServerLanguage()</c>). Used so server-only code — bot welcome
    /// messages, test notifications — has something better than English to fall back to
    /// when the user's actual preference is "auto". Not shown as a "saved" action to the
    /// user and does not touch anything else in their preferences.
    /// </summary>
    [HttpPost("preferences/resolved-language")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateResolvedLanguage([FromBody] ResolvedLanguageRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var normalized = NormalizeLanguage(request?.Language);
        if (normalized == "auto")
        {
            return NoContent();
        }

        var prefs = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        if (!string.Equals(prefs.ResolvedLanguage, normalized, StringComparison.OrdinalIgnoreCase))
        {
            prefs.ResolvedLanguage = normalized;
            await _preferenceStore.UpsertAsync(prefs).ConfigureAwait(false);
        }

        return NoContent();
    }

    /// <summary>
    /// Silently records that this user has opened the notification bell's panel at
    /// least once — stops the attention-drawing pulse animation on the bell button and
    /// means the guided tour won't be shown again. Not shown as a "saved" action.
    /// </summary>
    [HttpPost("preferences/bell-opened")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkBellOpened()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var prefs = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        if (!prefs.HasOpenedBell)
        {
            prefs.HasOpenedBell = true;
            await _preferenceStore.UpsertAsync(prefs).ConfigureAwait(false);
        }

        return NoContent();
    }

    /// <summary>
    /// Starts the WhatsApp connect flow: generates a one-time link token and returns
    /// a wa.me URL prefilled with that token, addressed to the admin-configured
    /// WhatsApp number. The binding is only saved once the Meta Cloud API webhook
    /// (<see cref="WhatsAppWebhookController"/>) actually receives that message back —
    /// clicking this button alone does not "connect" anything.
    /// </summary>
    [HttpPost("whatsapp/connect")]
    [Authorize]
    [ProducesResponseType(typeof(WhatsAppConnectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConnectWhatsApp()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var settings = Plugin.Instance!.Configuration.WhatsAppSettings;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.PhoneNumber))
        {
            return BadRequest("WhatsApp is not configured by the administrator.");
        }

        var token = await _channelStore.CreateLinkTokenAsync(userId, "whatsapp").ConfigureAwait(false);

        var message = Uri.EscapeDataString($"JellyNotify connect: {token}");
        var waMeUrl = $"https://wa.me/{settings.PhoneNumber}?text={message}";

        return Ok(new WhatsAppConnectResponse { WaMeUrl = waMeUrl });
    }

    /// <summary>Disconnects the current user's WhatsApp binding.</summary>
    [HttpPost("whatsapp/disconnect")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisconnectWhatsApp()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var binding = await _channelStore.GetByUserAsync(userId).ConfigureAwait(false);
        if (binding is not null)
        {
            binding.WhatsAppPhoneNumber = null;
            binding.WhatsAppLinkedAt = null;
            await _channelStore.UpsertAsync(binding).ConfigureAwait(false);
        }

        return NoContent();
    }

    /// <summary>
    /// Starts the Telegram connect flow: generates a one-time link token and returns a
    /// https://t.me/{bot}?start={token} deep link. Opening it takes the user straight to
    /// a chat with the bot; tapping Start sends <c>/start &lt;token&gt;</c>, which
    /// <see cref="TelegramLinkingService"/> picks up in the background, verifies, and uses
    /// to save the binding and send the welcome message. This endpoint alone does not
    /// "connect" anything — the binding is only saved once that message actually arrives.
    /// </summary>
    [HttpPost("telegram/connect")]
    [Authorize]
    [ProducesResponseType(typeof(TelegramConnectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConnectTelegram()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.TelegramSettings;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.BotToken))
        {
            return BadRequest("Telegram is not configured by the administrator.");
        }

        if (string.IsNullOrWhiteSpace(settings.BotUsername))
        {
            return BadRequest("The administrator hasn't set the bot's Telegram username yet.");
        }

        var token = await _channelStore.CreateLinkTokenAsync(userId, "telegram").ConfigureAwait(false);
        var deepLink = $"https://t.me/{settings.BotUsername.TrimStart('@')}?start={token}";

        return Ok(new TelegramConnectResponse { TelegramDeepLink = deepLink });
    }

    /// <summary>Disconnects the current user's Telegram binding.</summary>
    [HttpPost("telegram/disconnect")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisconnectTelegram()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var binding = await _channelStore.GetByUserAsync(userId).ConfigureAwait(false);
        if (binding is not null)
        {
            binding.TelegramChatId = null;
            binding.TelegramLinkedAt = null;
            await _channelStore.UpsertAsync(binding).ConfigureAwait(false);
        }

        return NoContent();
    }

    /// <summary>
    /// Starts the Discord connect flow: generates a one-time link token and returns a
    /// Discord OAuth2 "Login with Discord" authorize URL (state = the token). The user's
    /// numeric ID is discovered automatically from that login — nothing to type by hand.
    /// </summary>
    [HttpPost("discord/connect-url")]
    [Authorize]
    [ProducesResponseType(typeof(DiscordConnectUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDiscordConnectUrl()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.DiscordSettings;
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return BadRequest("Discord login is not configured by the administrator.");
        }

        var token = await _channelStore.CreateLinkTokenAsync(userId, "discord").ConfigureAwait(false);
        var redirectUri = BuildDiscordRedirectUri();
        var authorizeUrl = "https://discord.com/oauth2/authorize"
            + $"?client_id={Uri.EscapeDataString(settings.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + "&response_type=code"
            + "&scope=identify"
            + $"&state={Uri.EscapeDataString(token)}";

        return Ok(new DiscordConnectUrlResponse { AuthorizeUrl = authorizeUrl });
    }

    /// <summary>
    /// OAuth2 redirect target Discord sends the user's browser back to after they log in
    /// and authorize the app. Not <see cref="AuthorizeAttribute"/>-protected — this is a
    /// plain browser navigation with no Jellyfin session token attached, so the caller's
    /// identity comes entirely from the <c>state</c> value (the link token minted in
    /// <see cref="GetDiscordConnectUrl"/>), not from a Jellyfin auth header.
    /// </summary>
    [HttpGet("discord/oauth/callback")]
    [AllowAnonymous]
    public async Task<ContentResult> DiscordOAuthCallback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return HtmlCallbackPage(false, "Discord denied the request.");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return HtmlCallbackPage(false, "Missing authorization code.");
        }

        var userId = await _channelStore.ValidateLinkTokenAsync(state, "discord").ConfigureAwait(false);
        if (userId is null)
        {
            return HtmlCallbackPage(false, "This connection link has expired. Go back to Jellyfin and click Connect Discord again.");
        }

        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.DiscordSettings;
        var redirectUri = BuildDiscordRedirectUri();
        var identity = await _discordOAuth.ExchangeCodeAsync(code, redirectUri, settings.ClientId, settings.ClientSecret).ConfigureAwait(false);
        if (identity is null)
        {
            return HtmlCallbackPage(false, "Could not verify your Discord account. Please try again.");
        }

        var prefs = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        var displayName = ResolveDisplayName(userId);
        var welcomeText = WelcomeMessageBuilder.Build(displayName, _appHost.FriendlyName, prefs, WelcomeMessageChannel.Discord);

        var sent = await _discordDm.SendDirectMessageAsync(identity.Id, welcomeText).ConfigureAwait(false);
        if (!sent)
        {
            return HtmlCallbackPage(false, "We identified your Discord account, but couldn't send you a message. Make sure the bot has been invited to a server you're also a member of, then try again.");
        }

        var binding = await _channelStore.GetByUserAsync(userId).ConfigureAwait(false)
            ?? new UserChannelBinding { JellyfinUserId = userId };
        binding.DiscordUserId = identity.Id;
        binding.DiscordLinkedAt = DateTime.UtcNow;
        await _channelStore.UpsertAsync(binding).ConfigureAwait(false);

        return HtmlCallbackPage(true, "Discord connected! You can close this tab.");
    }

    private string BuildDiscordRedirectUri() =>
        $"{Request.Scheme}://{Request.Host}/JellyNotify/discord/oauth/callback";

    private string ResolveDisplayName(string userId)
    {
        try
        {
            if (Guid.TryParse(userId, out var guid))
            {
                var user = _userManager.GetUserById(guid);
                if (user is not null)
                {
                    return user.Username;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve display name for user {UserId}", userId);
        }

        return "there";
    }

    private static ContentResult HtmlCallbackPage(bool success, string message)
    {
        var icon = success ? "✅" : "❌";
        var html = $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>JellyNotify</title>
            <style>body{font-family:sans-serif;background:#101010;color:#eee;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
            div{text-align:center;max-width:26rem;padding:2rem}</style></head>
            <body><div><h1>{{icon}}</h1><p>{{System.Net.WebUtility.HtmlEncode(message)}}</p></div></body></html>
            """;
        return new ContentResult { Content = html, ContentType = "text/html", StatusCode = 200 };
    }

    /// <summary>Disconnects the current user's Discord binding.</summary>
    [HttpPost("discord/disconnect")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisconnectDiscord()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var binding = await _channelStore.GetByUserAsync(userId).ConfigureAwait(false);
        if (binding is not null)
        {
            binding.DiscordUserId = null;
            binding.DiscordLinkedAt = null;
            await _channelStore.UpsertAsync(binding).ConfigureAwait(false);
        }

        return NoContent();
    }

    /// <summary>
    /// Sends a test notification to the currently authenticated user.
    /// Useful for verifying channel configuration.
    /// </summary>
    [HttpPost("notifications/test")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SendTestNotification()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var prefs = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        var (title, message) = TestMessages.General(NotificationLanguage.Resolve(prefs));

        var notification = new NotificationEvent
        {
            JellyfinUserId = userId,
            Type = NotificationType.TestNotification,
            Title = title,
            Message = message,
            CreatedAt = DateTime.UtcNow
        };

        await _dispatcher.DispatchAsync(notification).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Extracts the current Jellyfin user ID from the HTTP context.
    /// Jellyfin sets the user ID as the Name claim on the authenticated principal.
    /// Returns null if the user is not authenticated.
    /// </summary>
    private string? GetCurrentUserId()
    {
        // Try the Jellyfin-specific claim first
        var userIdClaim = HttpContext.User?.FindFirst("Jellyfin-UserId")
            ?? HttpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? HttpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Name);

        return userIdClaim?.Value;
    }

    private static string NormalizeLanguage(string? language) =>
        string.Equals(language, "es-ES", StringComparison.OrdinalIgnoreCase) ? "es-ES" :
        string.Equals(language, "ca", StringComparison.OrdinalIgnoreCase) ? "ca" :
        string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" :
        "auto";
}

/// <summary>Response DTO for unread notification count.</summary>
public sealed class UnreadCountResponse
{
    /// <summary>Gets or sets the number of unread notifications.</summary>
    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; set; }
}

/// <summary>Non-sensitive settings used by regular Jellyfin users.</summary>
public sealed class PublicSettingsResponse
{
    /// <summary>Gets or sets the administrator default language (auto, en-US, es-ES).</summary>
    [JsonPropertyName("defaultLanguage")]
    public string DefaultLanguage { get; set; } = "auto";

    /// <summary>Gets or sets a value indicating whether Telegram is available as a personal channel.</summary>
    [JsonPropertyName("telegramAvailable")]
    public bool TelegramAvailable { get; set; }

    /// <summary>Gets or sets a value indicating whether Discord is available as a personal channel.</summary>
    [JsonPropertyName("discordAvailable")]
    public bool DiscordAvailable { get; set; }

    /// <summary>Gets or sets a value indicating whether WhatsApp is available as a personal channel.</summary>
    [JsonPropertyName("whatsappAvailable")]
    public bool WhatsAppAvailable { get; set; }

    /// <summary>Gets or sets a value indicating whether Jellyfin UI notifications are globally enabled.</summary>
    [JsonPropertyName("jellyfinUiEnabled")]
    public bool JellyfinUiEnabled { get; set; }
}

/// <summary>Response DTO for the WhatsApp connect flow.</summary>
public sealed class WhatsAppConnectResponse
{
    /// <summary>Gets or sets the wa.me URL the client should open to complete the connection.</summary>
    [JsonPropertyName("waMeUrl")]
    public string WaMeUrl { get; set; } = string.Empty;
}

/// <summary>Request DTO for reporting a resolved "auto" language back to the server.</summary>
public sealed class ResolvedLanguageRequest
{
    /// <summary>Gets or sets the concrete language the frontend resolved "auto" to.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }
}

/// <summary>Response DTO for the Telegram connect flow.</summary>
public sealed class TelegramConnectResponse
{
    /// <summary>Gets or sets the https://t.me/... deep link the client should open to complete the connection.</summary>
    [JsonPropertyName("telegramDeepLink")]
    public string TelegramDeepLink { get; set; } = string.Empty;
}

/// <summary>Response DTO for the Discord connect flow.</summary>
public sealed class DiscordConnectUrlResponse
{
    /// <summary>Gets or sets the Discord OAuth2 authorize URL the client should open to complete the connection.</summary>
    [JsonPropertyName("authorizeUrl")]
    public string AuthorizeUrl { get; set; } = string.Empty;
}
