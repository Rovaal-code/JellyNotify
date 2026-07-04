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
/// Receives inbound webhook calls from the Meta WhatsApp Business Cloud API.
/// Unlike the rest of the plugin's API, these endpoints are intentionally
/// unauthenticated — Meta's servers call them directly, with no Jellyfin session.
/// Authenticity is instead verified via the shared "Verify Token" (GET handshake)
/// and by the fact this only ever links accounts when a matching one-time
/// connect token is found in the message body, never arbitrary numbers.
/// </summary>
[ApiController]
[Route("JellyNotify/whatsapp/webhook")]
[AllowAnonymous]
public sealed class WhatsAppWebhookController : ControllerBase
{
    private readonly IUserChannelStore _channelStore;
    private readonly IUserPreferenceStore _preferenceStore;
    private readonly IRequestSnapshotStore _snapshotStore;
    private readonly IWhatsAppCloudApiClient _whatsApp;
    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    /// <summary>Initializes a new instance of the <see cref="WhatsAppWebhookController"/> class.</summary>
    public WhatsAppWebhookController(
        IUserChannelStore channelStore,
        IUserPreferenceStore preferenceStore,
        IRequestSnapshotStore snapshotStore,
        IWhatsAppCloudApiClient whatsApp,
        IUserManager userManager,
        IServerApplicationHost appHost,
        ILogger<WhatsAppWebhookController> logger)
    {
        _channelStore = channelStore;
        _preferenceStore = preferenceStore;
        _snapshotStore = snapshotStore;
        _whatsApp = whatsApp;
        _userManager = userManager;
        _appHost = appHost;
        _logger = logger;
    }

    /// <summary>Meta's one-time webhook verification handshake, performed when the admin saves the webhook URL in the Meta dashboard.</summary>
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var configuredToken = Plugin.Instance!.Configuration.WhatsAppSettings.VerifyToken;
        if (string.Equals(mode, "subscribe", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(configuredToken)
            && string.Equals(verifyToken, configuredToken, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(challenge))
        {
            return Content(challenge, "text/plain");
        }

        return Forbid();
    }

    /// <summary>Receives inbound WhatsApp messages. Always acknowledges quickly, per Meta's requirements.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] WhatsAppWebhookPayload? payload)
    {
        try
        {
            await ProcessAsync(payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook payload");
        }

        return Ok();
    }

    private async Task ProcessAsync(WhatsAppWebhookPayload? payload)
    {
        var messages = payload?.Entry?
            .SelectMany(e => e.Changes ?? new List<WhatsAppChange>())
            .SelectMany(c => c.Value?.Messages ?? new List<WhatsAppMessage>())
            ?? Enumerable.Empty<WhatsAppMessage>();

        foreach (var message in messages)
        {
            var body = message.Text?.Body;
            var from = message.From;
            if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(from))
            {
                continue;
            }

            const string prefix = "JellyNotify connect: ";
            var index = body.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var token = body[(index + prefix.Length)..].Trim();
                if (token.Length > 0)
                {
                    await TryLinkAsync(token, from).ConfigureAwait(false);
                }

                continue;
            }

            var trimmedBody = body.Trim();
            if (string.Equals(trimmedBody, "status", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmedBody, "estado", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyWithStatusAsync(from).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Handles the "status"/"estado" keyword: looks up which Jellyfin user this phone
    /// number belongs to, then replies with their current request status summary built
    /// entirely from the already-cached IRequestSnapshotStore — no Seerr/Sonarr/Radarr
    /// calls at all, so a user asking repeatedly costs nothing beyond one Cloud API send.
    /// </summary>
    private async Task ReplyWithStatusAsync(string fromPhoneNumber)
    {
        var binding = await _channelStore.GetByWhatsAppPhoneNumberAsync(fromPhoneNumber).ConfigureAwait(false);
        if (binding is null)
        {
            return;
        }

        var prefs = await _preferenceStore.GetByUserAsync(binding.JellyfinUserId).ConfigureAwait(false);
        var snapshots = await _snapshotStore.GetAllAsync().ConfigureAwait(false);
        var summary = RequestStatusSummaryBuilder.Build(snapshots, binding.JellyfinUserId, NotificationLanguage.Resolve(prefs));

        await _whatsApp.SendTextMessageAsync(fromPhoneNumber, summary).ConfigureAwait(false);
    }

    private async Task TryLinkAsync(string token, string fromPhoneNumber)
    {
        var userId = await _channelStore.ValidateLinkTokenAsync(token, "whatsapp").ConfigureAwait(false);
        if (userId is null)
        {
            _logger.LogDebug("WhatsApp connect message received with an unknown or expired token");
            return;
        }

        var binding = await _channelStore.GetByUserAsync(userId).ConfigureAwait(false)
            ?? new UserChannelBinding { JellyfinUserId = userId };
        binding.WhatsAppPhoneNumber = fromPhoneNumber;
        binding.WhatsAppLinkedAt = DateTime.UtcNow;
        await _channelStore.UpsertAsync(binding).ConfigureAwait(false);

        var prefs = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        var displayName = ResolveDisplayName(userId);
        var welcomeText = WelcomeMessageBuilder.Build(displayName, _appHost.FriendlyName, prefs, WelcomeMessageChannel.WhatsApp);

        await _whatsApp.SendTextMessageAsync(fromPhoneNumber, welcomeText).ConfigureAwait(false);

        _logger.LogInformation("Linked WhatsApp number to Jellyfin user {UserId}", userId);
    }

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
}

/// <summary>Root payload Meta sends for WhatsApp Cloud API webhook events.</summary>
public sealed class WhatsAppWebhookPayload
{
    /// <summary>Gets or sets the webhook entries.</summary>
    [JsonPropertyName("entry")]
    public List<WhatsAppEntry>? Entry { get; set; }
}

/// <summary>A single webhook entry.</summary>
public sealed class WhatsAppEntry
{
    /// <summary>Gets or sets the list of changes in this entry.</summary>
    [JsonPropertyName("changes")]
    public List<WhatsAppChange>? Changes { get; set; }
}

/// <summary>A single change within a webhook entry.</summary>
public sealed class WhatsAppChange
{
    /// <summary>Gets or sets the change value payload.</summary>
    [JsonPropertyName("value")]
    public WhatsAppChangeValue? Value { get; set; }
}

/// <summary>The value payload of a webhook change, containing any inbound messages.</summary>
public sealed class WhatsAppChangeValue
{
    /// <summary>Gets or sets the inbound messages in this change.</summary>
    [JsonPropertyName("messages")]
    public List<WhatsAppMessage>? Messages { get; set; }
}

/// <summary>A single inbound WhatsApp message.</summary>
public sealed class WhatsAppMessage
{
    /// <summary>Gets or sets the sender's phone number (E.164, no leading '+').</summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>Gets or sets the message type (e.g. "text").</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets the text body, if this is a text message.</summary>
    [JsonPropertyName("text")]
    public WhatsAppMessageText? Text { get; set; }
}

/// <summary>The text body of an inbound WhatsApp text message.</summary>
public sealed class WhatsAppMessageText
{
    /// <summary>Gets or sets the message body.</summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}
