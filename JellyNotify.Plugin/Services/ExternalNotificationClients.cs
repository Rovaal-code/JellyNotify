using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.JellyNotify.Models;
using Jellyfin.Plugin.JellyNotify.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Sends notification embeds to a Discord webhook.
/// The webhook URL is stored server-side only and never exposed to the frontend.
/// </summary>
public sealed class DiscordNotificationClient : IDiscordNotificationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly ILogger<DiscordNotificationClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="DiscordNotificationClient"/> class.</summary>
    public DiscordNotificationClient(HttpClient http, ILogger<DiscordNotificationClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> SendAsync(NotificationEvent notification, CancellationToken cancellationToken = default) =>
        SendAsync(notification, notification.Message, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> SendAsync(NotificationEvent notification, string enrichedMessage, CancellationToken cancellationToken = default)
    {
        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.DiscordSettings;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return false;
        }

        try
        {
            var color = ParseHexColor(settings.EmbedColor);
            var payload = new
            {
                username = settings.BotUsername,
                avatar_url = settings.AvatarUrl,
                embeds = new[]
                {
                    new
                    {
                        // No separate title — the description's own leading "Estado" field
                        // (see NotificationCardFormatter) carries that now.
                        description = enrichedMessage,
                        color,
                        timestamp = notification.CreatedAt.ToString("O"),
                        image = notification.ThumbnailUrl is not null
                            ? new { url = notification.ThumbnailUrl }
                            : null,
                        author = new { name = "JellyNotify" },
                        footer = new { text = "JellyNotify" }
                    }
                }
            };

            using var response = await _http.PostAsJsonAsync(settings.WebhookUrl, payload, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Discord webhook returned {Status} for notification {Id}", response.StatusCode, notification.Id);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Discord notification {Id}", notification.Id);
            return false;
        }
    }

    private static int ParseHexColor(string hexColor)
    {
        try
        {
            var hex = hexColor.TrimStart('#');
            return Convert.ToInt32(hex, 16);
        }
        catch
        {
            return 0xAA5CC3; // default purple
        }
    }
}

/// <summary>
/// Sends notifications via the Telegram Bot API.
/// The bot token is stored server-side only and never exposed to the frontend.
/// </summary>
public sealed class TelegramNotificationClient : ITelegramNotificationClient
{
    private readonly HttpClient _http;
    private readonly ILogger<TelegramNotificationClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="TelegramNotificationClient"/> class.</summary>
    public TelegramNotificationClient(HttpClient http, ILogger<TelegramNotificationClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> SendAsync(NotificationEvent notification, string chatId, CancellationToken cancellationToken = default)
    {
        var caption = $"<b>{EscapeHtml(notification.Title)}</b>\n{EscapeHtml(notification.Message)}";
        return notification.ThumbnailUrl is not null
            ? SendPhotoAsync(notification.ThumbnailUrl, caption, chatId, notification.Id, cancellationToken)
            : SendRawAsync(caption, chatId, notification.Id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> SendAsync(string htmlBody, string? thumbnailUrl, string chatId, string notificationId, CancellationToken cancellationToken = default) =>
        thumbnailUrl is not null
            ? SendPhotoAsync(thumbnailUrl, htmlBody, chatId, notificationId, cancellationToken)
            : SendRawAsync(htmlBody, chatId, notificationId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> SendHtmlAsync(string html, string chatId, CancellationToken cancellationToken = default) =>
        SendRawAsync(html, chatId, "html-message", cancellationToken);

    /// <summary>
    /// Sends a poster image with the notification text as its caption, via Telegram's
    /// sendPhoto endpoint. Falls back to a plain text message if the photo send fails
    /// (e.g. Telegram couldn't fetch the poster URL) so the notification still arrives.
    /// </summary>
    private async Task<bool> SendPhotoAsync(string photoUrl, string caption, string chatId, string logId, CancellationToken cancellationToken)
    {
        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.TelegramSettings;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.BotToken))
        {
            return false;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{settings.BotToken}/sendPhoto";

            var payload = new
            {
                chat_id = chatId,
                photo = photoUrl,
                caption = CaptionTruncator.TruncateForPhotoCaption(caption),
                parse_mode = "HTML",
                disable_notification = settings.SilentMessages,
                message_thread_id = settings.MessageThreadId
            };

            using var response = await _http.PostAsJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Telegram sendPhoto returned {Status} for notification {Id}: {Body} — falling back to text",
                    response.StatusCode, logId, body);
                return await SendRawAsync(caption, chatId, logId, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram photo {Id} to chat {ChatId} — falling back to text", logId, chatId);
            return await SendRawAsync(caption, chatId, logId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> SendRawAsync(string text, string chatId, string logId, CancellationToken cancellationToken)
    {
        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.TelegramSettings;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.BotToken))
        {
            return false;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{settings.BotToken}/sendMessage";

            var payload = new
            {
                chat_id = chatId,
                text,
                parse_mode = "HTML",
                disable_web_page_preview = settings.DisableLinkPreviews,
                disable_notification = settings.SilentMessages,
                message_thread_id = settings.MessageThreadId
            };

            using var response = await _http.PostAsJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Telegram API returned {Status} for notification {Id}: {Body}",
                    response.StatusCode, logId, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram notification {Id} to chat {ChatId}", logId, chatId);
            return false;
        }
    }

    /// <summary>Escapes Telegram HTML parse-mode metacharacters. Shared with <see cref="Services.NotificationCardFormatter"/> so field labels/values appended to a Telegram caption use the exact same escaping as the base title/message.</summary>
    internal static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>
/// Sends direct messages to Discord users via the Bot REST API. Both steps —
/// opening a DM channel and posting a message to it — are plain authenticated
/// REST calls (Authorization: Bot &lt;token&gt;); no Gateway/WebSocket session is
/// needed. Discord still enforces its own anti-spam rule that a bot can only DM
/// a user it shares at least one server with — that restriction surfaces here as
/// an ordinary failed HTTP response, not something this client can work around.
/// </summary>
public sealed class DiscordDmClient : IDiscordDmClient
{
    private const string ApiBase = "https://discord.com/api/v10";

    private readonly HttpClient _http;
    private readonly ILogger<DiscordDmClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="DiscordDmClient"/> class.</summary>
    public DiscordDmClient(HttpClient http, ILogger<DiscordDmClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SendDirectMessageAsync(string discordUserId, string text, CancellationToken cancellationToken = default) =>
        await SendPayloadAsync(discordUserId, new { content = text }, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> SendEmbedAsync(string discordUserId, string message, string? thumbnailUrl, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    description = message,
                    image = thumbnailUrl is not null ? new { url = thumbnailUrl } : null,
                    author = new { name = "JellyNotify" },
                    footer = new { text = "JellyNotify" }
                }
            }
        };

        return await SendPayloadAsync(discordUserId, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SendPayloadAsync(string discordUserId, object messagePayload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discordUserId);

        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.DiscordSettings;
        if (string.IsNullOrWhiteSpace(settings.BotToken))
        {
            return false;
        }

        try
        {
            using var channelRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/users/@me/channels")
            {
                Content = JsonContent.Create(new { recipient_id = discordUserId })
            };
            channelRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", settings.BotToken);

            using var channelResponse = await _http.SendAsync(channelRequest, cancellationToken).ConfigureAwait(false);
            if (!channelResponse.IsSuccessStatusCode)
            {
                var body = await channelResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Discord DM channel creation returned {Status} for user {UserId}: {Body}",
                    channelResponse.StatusCode, discordUserId, body);
                return false;
            }

            var channel = await channelResponse.Content.ReadFromJsonAsync<DiscordChannelResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(channel?.Id))
            {
                _logger.LogWarning("Discord DM channel response had no id for user {UserId}", discordUserId);
                return false;
            }

            using var messageRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/channels/{channel.Id}/messages")
            {
                Content = JsonContent.Create(messagePayload)
            };
            messageRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", settings.BotToken);

            using var messageResponse = await _http.SendAsync(messageRequest, cancellationToken).ConfigureAwait(false);
            if (!messageResponse.IsSuccessStatusCode)
            {
                var body = await messageResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Discord DM send returned {Status} for user {UserId}: {Body}",
                    messageResponse.StatusCode, discordUserId, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Discord DM to user {UserId}", discordUserId);
            return false;
        }
    }

    private sealed class DiscordChannelResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}
