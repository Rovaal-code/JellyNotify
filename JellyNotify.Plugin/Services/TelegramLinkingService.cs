using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyNotify.Models;
using Jellyfin.Plugin.JellyNotify.Store;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Background service that short-polls the Telegram Bot API for incoming
/// <c>/start &lt;token&gt;</c> messages — the other half of the "Connect Telegram"
/// deep link (<c>https://t.me/{botUsername}?start={token}</c>). Telegram bots can
/// receive updates via simple REST polling (no Gateway/WebSocket session needed,
/// unlike Discord), so this runs independently of the main sync cycle at a much
/// shorter interval to feel responsive right after a user taps Connect.
/// </summary>
public sealed class TelegramLinkingService : BackgroundService
{
    private const string ApiBase = "https://api.telegram.org";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly IUserChannelStore _channelStore;
    private readonly IUserPreferenceStore _preferenceStore;
    private readonly IRequestSnapshotStore _snapshotStore;
    private readonly ITelegramNotificationClient _telegram;
    private readonly ITelegramActivityStore _activityStore;
    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<TelegramLinkingService> _logger;

    private long _offset;

    /// <summary>Initializes a new instance of the <see cref="TelegramLinkingService"/> class.</summary>
    public TelegramLinkingService(
        IHttpClientFactory httpClientFactory,
        IUserChannelStore channelStore,
        IUserPreferenceStore preferenceStore,
        IRequestSnapshotStore snapshotStore,
        ITelegramNotificationClient telegram,
        ITelegramActivityStore activityStore,
        IUserManager userManager,
        IServerApplicationHost appHost,
        ILogger<TelegramLinkingService> logger)
    {
        _http = httpClientFactory.CreateClient(nameof(TelegramLinkingService));
        _channelStore = channelStore;
        _preferenceStore = preferenceStore;
        _snapshotStore = snapshotStore;
        _telegram = telegram;
        _activityStore = activityStore;
        _userManager = userManager;
        _appHost = appHost;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = Plugin.Instance?.Configuration.ExternalChannelSettings.TelegramSettings;
            if (settings is null || !settings.Enabled || string.IsNullOrWhiteSpace(settings.BotToken))
            {
                await SafeDelay(IdleInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await PollOnceAsync(settings.BotToken, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Telegram linking poll failed");
            }

            await SafeDelay(PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PollOnceAsync(string botToken, CancellationToken cancellationToken)
    {
        var url = $"{ApiBase}/bot{botToken}/getUpdates?offset={_offset}&timeout=0&limit=20";
        var response = await _http.GetFromJsonAsync<TelegramGetUpdatesResponse>(url, cancellationToken).ConfigureAwait(false);
        if (response?.Ok != true || response.Result is null || response.Result.Count == 0)
        {
            return;
        }

        foreach (var update in response.Result)
        {
            _offset = Math.Max(_offset, update.UpdateId + 1);

            var text = update.Message?.Text;
            var chatId = update.Message?.Chat?.Id;

            // Record every chat we see a message from — regardless of its content —
            // so the admin's "Detect automatically" button (Channels > Telegram) has
            // something to read. A second, independent getUpdates call from that
            // button would race this poller: whichever call reaches Telegram first
            // advances the server-side offset and "consumes" the update, so the
            // other one would always see an empty result.
            if (chatId is not null)
            {
                _activityStore.RecordChatId(chatId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (string.IsNullOrWhiteSpace(text) || chatId is null)
            {
                continue;
            }

            var chatIdString = chatId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (text.StartsWith("/start ", StringComparison.OrdinalIgnoreCase))
            {
                var token = text["/start ".Length..].Trim();
                if (token.Length > 0)
                {
                    await TryLinkAsync(token, chatIdString, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (string.Equals(text.Trim(), "/status", StringComparison.OrdinalIgnoreCase))
            {
                await ReplyWithStatusAsync(chatIdString, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task TryLinkAsync(string token, string chatId, CancellationToken cancellationToken)
    {
        var userId = await _channelStore.ValidateLinkTokenAsync(token, "telegram").ConfigureAwait(false);
        if (userId is null)
        {
            _logger.LogDebug("Telegram /start received with an unknown or expired token");
            return;
        }

        var binding = await _channelStore.GetByUserAsync(userId).ConfigureAwait(false)
            ?? new UserChannelBinding { JellyfinUserId = userId };
        binding.TelegramChatId = chatId;
        binding.TelegramLinkedAt = DateTime.UtcNow;
        await _channelStore.UpsertAsync(binding).ConfigureAwait(false);

        var prefs = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        var displayName = ResolveDisplayName(userId);
        var welcomeText = WelcomeMessageBuilder.Build(displayName, _appHost.FriendlyName, prefs, WelcomeMessageChannel.Telegram);

        await _telegram.SendHtmlAsync(welcomeText, chatId, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Linked Telegram chat {ChatId} to Jellyfin user {UserId}", chatId, userId);
    }

    /// <summary>
    /// Handles the /status command: looks up which Jellyfin user this chat belongs to,
    /// then replies with their current request status summary built entirely from the
    /// already-cached IRequestSnapshotStore — no Seerr/Sonarr/Radarr calls at all, so a
    /// user asking repeatedly costs nothing beyond this one Telegram reply.
    /// </summary>
    private async Task ReplyWithStatusAsync(string chatId, CancellationToken cancellationToken)
    {
        var binding = await _channelStore.GetByTelegramChatIdAsync(chatId).ConfigureAwait(false);
        if (binding is null)
        {
            return;
        }

        var prefs = await _preferenceStore.GetByUserAsync(binding.JellyfinUserId).ConfigureAwait(false);
        var snapshots = await _snapshotStore.GetAllAsync().ConfigureAwait(false);
        var summary = RequestStatusSummaryBuilder.Build(snapshots, binding.JellyfinUserId, NotificationLanguage.Resolve(prefs));

        await _telegram.SendAsync(
            new NotificationEvent { JellyfinUserId = binding.JellyfinUserId, Type = NotificationType.TestNotification, Title = "JellyNotify", Message = summary, CreatedAt = DateTime.UtcNow },
            chatId,
            cancellationToken).ConfigureAwait(false);
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

    private static async Task SafeDelay(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private sealed class TelegramGetUpdatesResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("result")]
        public List<TelegramUpdate>? Result { get; set; }
    }

    private sealed class TelegramUpdate
    {
        [JsonPropertyName("update_id")]
        public long UpdateId { get; set; }

        [JsonPropertyName("message")]
        public TelegramMessage? Message { get; set; }
    }

    private sealed class TelegramMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("chat")]
        public TelegramChat? Chat { get; set; }
    }

    private sealed class TelegramChat
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }
}
