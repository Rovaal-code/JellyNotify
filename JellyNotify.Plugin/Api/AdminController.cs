using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.JellyNotify.Configuration;
using Jellyfin.Plugin.JellyNotify.Models;
using Jellyfin.Plugin.JellyNotify.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Api;

/// <summary>
/// Jellyfin's own JSON responses (and its default MVC JSON options) use
/// PascalCase, matching C# property names as-is. jellynotify.js reads every
/// field as camelCase (matching <see cref="Models.UserNotificationPreference"/>,
/// which declares explicit camelCase [JsonPropertyName] attributes). Rather than
/// annotate every property on every admin DTO — easy to miss on future additions —
/// admin/diagnostics responses are serialized explicitly with these options and
/// returned as raw JSON content instead of via Ok(dto).
/// </summary>
internal static class JsonCamelCase
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// Admin-only API controller for JellyNotify plugin configuration.
/// All endpoints require Jellyfin administrator role.
/// </summary>
[ApiController]
[Route("JellyNotify/Admin")]
[Authorize(Policy = "RequiresElevation")]
[Produces("application/json")]
public sealed class AdminController : ControllerBase
{
    private readonly ISeerrApiClient _seerr;
    private readonly ISonarrApiClient _sonarr;
    private readonly IRadarrApiClient _radarr;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IDiscordNotificationClient _discord;
    private readonly ITelegramNotificationClient _telegram;
    private readonly ITelegramActivityStore _telegramActivity;
    private readonly IWhatsAppCloudApiClient _whatsApp;
    private readonly Store.INotificationStore _notificationStore;
    private readonly Store.IUserPreferenceStore _preferenceStore;
    private readonly ILogger<AdminController> _logger;

    /// <summary>Initializes a new instance of the <see cref="AdminController"/> class.</summary>
    public AdminController(
        ISeerrApiClient seerr,
        ISonarrApiClient sonarr,
        IRadarrApiClient radarr,
        INotificationDispatcher dispatcher,
        IDiscordNotificationClient discord,
        ITelegramNotificationClient telegram,
        ITelegramActivityStore telegramActivity,
        IWhatsAppCloudApiClient whatsApp,
        Store.INotificationStore notificationStore,
        Store.IUserPreferenceStore preferenceStore,
        ILogger<AdminController> logger)
    {
        _seerr = seerr;
        _sonarr = sonarr;
        _radarr = radarr;
        _dispatcher = dispatcher;
        _discord = discord;
        _telegram = telegram;
        _telegramActivity = telegramActivity;
        _whatsApp = whatsApp;
        _notificationStore = notificationStore;
        _preferenceStore = preferenceStore;
        _logger = logger;
    }

    private ContentResult JsonOk(object value) =>
        Content(JsonSerializer.Serialize(value, JsonCamelCase.Options), "application/json");

    private ContentResult JsonNotFound(object value) =>
        new() { Content = JsonSerializer.Serialize(value, JsonCamelCase.Options), ContentType = "application/json", StatusCode = StatusCodes.Status404NotFound };

    /// <summary>Gets the current plugin configuration (without sensitive API keys).</summary>
    [HttpGet("config")]
    [ProducesResponseType(typeof(PluginConfigDto), StatusCodes.Status200OK)]
    public IActionResult GetConfig()
    {
        var config = Plugin.Instance!.Configuration;

        // Generated lazily (rather than in the constructor) and persisted immediately,
        // so the URL is stable across restarts even before the admin's first explicit
        // save — see the WebhookSecret doc comments for why.
        var needsSave = false;
        if (string.IsNullOrWhiteSpace(config.SeerrSettings.WebhookSecret))
        {
            config.SeerrSettings.WebhookSecret = Guid.NewGuid().ToString("N");
            needsSave = true;
        }

        if (string.IsNullOrWhiteSpace(config.ArrWebhookSecret))
        {
            config.ArrWebhookSecret = Guid.NewGuid().ToString("N");
            needsSave = true;
        }

        if (needsSave)
        {
            Plugin.Instance!.SavePluginConfiguration(config);
        }

        var dto = new PluginConfigDto(config)
        {
            WhatsAppWebhookUrl = $"{Request.Scheme}://{Request.Host}/JellyNotify/whatsapp/webhook",
            ArrWebhookUrl = $"{Request.Scheme}://{Request.Host}/JellyNotify/arr/webhook/{config.ArrWebhookSecret}",
            SeerrWebhookUrl = $"{Request.Scheme}://{Request.Host}/JellyNotify/seerr/webhook/{config.SeerrSettings.WebhookSecret}"
        };
        return JsonOk(dto);
    }

    /// <summary>
    /// Saves the plugin configuration while preserving existing secrets.
    /// If the incoming config has an empty/null API key for any integration,
    /// the existing persisted key is retained — preventing accidental secret erasure
    /// when the frontend sends a form without re-entering credentials.
    /// </summary>
    [HttpPut("config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult SaveConfig([FromBody] PluginConfiguration incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        var existing = Plugin.Instance!.Configuration;

        // Preserve secrets that were not re-submitted by the frontend
        PluginConfiguration.PreserveSecrets(existing, incoming);
        incoming.DefaultLanguage = NormalizeLanguage(incoming.DefaultLanguage);
        if (incoming.NotificationRetentionDays < 0)
        {
            incoming.NotificationRetentionDays = existing.NotificationRetentionDays;
        }

        Plugin.Instance!.SavePluginConfiguration(incoming);
        _logger.LogInformation("JellyNotify configuration saved");
        return NoContent();
    }

    private static string NormalizeLanguage(string? language) =>
        string.Equals(language, "es-ES", StringComparison.OrdinalIgnoreCase) ? "es-ES" :
        string.Equals(language, "ca", StringComparison.OrdinalIgnoreCase) ? "ca" :
        string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" :
        "auto";

    /// <summary>Tests the Seerr connection and returns the server version.</summary>
    [HttpPost("test/seerr")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestSeerr()
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var result = await _seerr.TestConnectionAsync().ConfigureAwait(false);
        if (result is null)
        {
            return JsonOk(new TestConnectionResponse { Success = false, Message = AdminTestMessages.SeerrConnectFail(language) });
        }

        var (webhookCapable, webhookMessage) = await CheckSeerrWebhookCapabilityAsync(language).ConfigureAwait(false);
        return JsonOk(new TestConnectionResponse
        {
            Success = true,
            Message = AdminTestMessages.SeerrConnectSuccess(result.Version, language),
            WebhookCapable = webhookCapable,
            WebhookMessage = webhookMessage
        });
    }

    /// <summary>
    /// Checks whether JellyNotify can auto-configure Seerr's webhook: fires a real
    /// (pre-save) test call through Seerr's own webhook test endpoint with a candidate
    /// configuration. Returned separately from the connection test's own result so the UI
    /// can show them as two independent outcomes instead of one combined message.
    /// </summary>
    private async Task<(bool Capable, string Message)> CheckSeerrWebhookCapabilityAsync(string language)
    {
        var config = Plugin.Instance!.Configuration;
        if (string.IsNullOrWhiteSpace(config.SeerrSettings.WebhookSecret))
        {
            config.SeerrSettings.WebhookSecret = Guid.NewGuid().ToString("N");
            Plugin.Instance!.SavePluginConfiguration(config);
        }

        var seerrWebhookUrl = $"{Request.Scheme}://{Request.Host}/JellyNotify/seerr/webhook/{config.SeerrSettings.WebhookSecret}";
        var candidate = BuildSeerrWebhookCandidate(seerrWebhookUrl);
        var (success, error) = await _seerr.TestWebhookSettingsAsync(candidate).ConfigureAwait(false);
        return success
            ? (true, AdminTestMessages.SeerrWebhookCapabilityConfirmed(language))
            : (false, AdminTestMessages.SeerrWebhookCapabilityFailed(error ?? AdminTestMessages.ArrWebhookTestNoDetail(language), language));
    }

    /// <summary>Tests a Sonarr instance connection.</summary>
    [HttpPost("test/sonarr/{instanceId}")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestSonarr(string instanceId)
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var config = Plugin.Instance!.Configuration;
        var instance = config.SonarrInstances.FirstOrDefault(i =>
            string.Equals(i.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
        {
            return JsonNotFound(new TestConnectionResponse { Success = false, Message = AdminTestMessages.SonarrInstanceNotFound(language) });
        }

        var result = await _sonarr.TestConnectionAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors).ConfigureAwait(false);
        if (result is null)
        {
            return JsonOk(new TestConnectionResponse { Success = false, Message = AdminTestMessages.SonarrConnectFail(language) });
        }

        var (webhookCapable, webhookMessage) = await CheckArrWebhookCapabilityAsync(
            instance.ServerUrl,
            instance.ApiKey,
            instance.IgnoreSslErrors,
            (url, key, ignoreSsl, ct) => _sonarr.GetNotificationsAsync(url, key, ignoreSsl, ct),
            (url, key, ignoreSsl, ct) => _sonarr.GetNotificationSchemasAsync(url, key, ignoreSsl, ct),
            (url, key, candidate, ignoreSsl, ct) => _sonarr.TestNotificationAsync(url, key, candidate, ignoreSsl, ct),
            language).ConfigureAwait(false);

        return JsonOk(new TestConnectionResponse
        {
            Success = true,
            Message = AdminTestMessages.SonarrConnectSuccess(result.Version, language),
            WebhookCapable = webhookCapable,
            WebhookMessage = webhookMessage
        });
    }

    /// <summary>Tests a Radarr instance connection.</summary>
    [HttpPost("test/radarr/{instanceId}")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestRadarr(string instanceId)
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var config = Plugin.Instance!.Configuration;
        var instance = config.RadarrInstances.FirstOrDefault(i =>
            string.Equals(i.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
        {
            return JsonNotFound(new TestConnectionResponse { Success = false, Message = AdminTestMessages.RadarrInstanceNotFound(language) });
        }

        var result = await _radarr.TestConnectionAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors).ConfigureAwait(false);
        if (result is null)
        {
            return JsonOk(new TestConnectionResponse { Success = false, Message = AdminTestMessages.RadarrConnectFail(language) });
        }

        var (webhookCapable, webhookMessage) = await CheckArrWebhookCapabilityAsync(
            instance.ServerUrl,
            instance.ApiKey,
            instance.IgnoreSslErrors,
            (url, key, ignoreSsl, ct) => _radarr.GetNotificationsAsync(url, key, ignoreSsl, ct),
            (url, key, ignoreSsl, ct) => _radarr.GetNotificationSchemasAsync(url, key, ignoreSsl, ct),
            (url, key, candidate, ignoreSsl, ct) => _radarr.TestNotificationAsync(url, key, candidate, ignoreSsl, ct),
            language).ConfigureAwait(false);

        return JsonOk(new TestConnectionResponse
        {
            Success = true,
            Message = AdminTestMessages.RadarrConnectSuccess(result.Version, language),
            WebhookCapable = webhookCapable,
            WebhookMessage = webhookMessage
        });
    }

    /// <summary>
    /// Checks whether JellyNotify can auto-configure the shared *arr webhook on this instance:
    /// fetches the Webhook notification schema and, if present, fires a real (pre-creation)
    /// test call through it. Returned separately from the connection test's own result so the
    /// UI can show them as two independent outcomes — confirming automatic setup will work at
    /// save time, or explaining what to configure manually if it won't — instead of collapsing
    /// a possible webhook failure into the same green message as a successful connection.
    /// Checks for an existing "JellyNotify" connection first: testing a fresh candidate with
    /// that same fixed name while one already exists fails Sonarr/Radarr's own name-uniqueness
    /// validation ("Should be unique") — a false negative, not a real capability problem — so
    /// this reports "already configured" instead of attempting (and failing) that test.
    /// </summary>
    private async Task<(bool Capable, string Message)> CheckArrWebhookCapabilityAsync(
        string serverUrl,
        string apiKey,
        bool ignoreSsl,
        Func<string, string, bool, CancellationToken, Task<IReadOnlyList<ArrNotificationResource>>> getNotifications,
        Func<string, string, bool, CancellationToken, Task<IReadOnlyList<ArrNotificationResource>>> getSchemas,
        Func<string, string, ArrNotificationResource, bool, CancellationToken, Task<(bool Success, string? Error)>> testNotification,
        string language)
    {
        var config = Plugin.Instance!.Configuration;
        if (string.IsNullOrWhiteSpace(config.ArrWebhookSecret))
        {
            config.ArrWebhookSecret = Guid.NewGuid().ToString("N");
            Plugin.Instance!.SavePluginConfiguration(config);
        }

        var arrWebhookUrl = $"{Request.Scheme}://{Request.Host}/JellyNotify/arr/webhook/{config.ArrWebhookSecret}";
        var ct = HttpContext.RequestAborted;

        var existing = await getNotifications(serverUrl, apiKey, ignoreSsl, ct).ConfigureAwait(false);
        if (existing.Any(n => string.Equals(n.Name, "JellyNotify", StringComparison.OrdinalIgnoreCase)))
        {
            return (true, AdminTestMessages.ArrWebhookAlreadyConfigured(language));
        }

        var schemas = await getSchemas(serverUrl, apiKey, ignoreSsl, ct).ConfigureAwait(false);
        var webhookSchema = schemas.FirstOrDefault(s => string.Equals(s.Implementation, "Webhook", StringComparison.OrdinalIgnoreCase));
        if (webhookSchema is null)
        {
            return (false, AdminTestMessages.ArrWebhookCapabilityFailed(AdminTestMessages.ArrWebhookSchemaUnsupported(language), language));
        }

        var candidate = BuildArrWebhookCandidate(webhookSchema, arrWebhookUrl);
        var (success, error) = await testNotification(serverUrl, apiKey, candidate, ignoreSsl, ct).ConfigureAwait(false);
        return success
            ? (true, AdminTestMessages.ArrWebhookCapabilityConfirmed(language))
            : (false, AdminTestMessages.ArrWebhookCapabilityFailed(error ?? AdminTestMessages.ArrWebhookTestNoDetail(language), language));
    }

    /// <summary>Creates a Webhook Connect notification in Sonarr pointing at JellyNotify's shared arr webhook URL, if one doesn't already exist.</summary>
    [HttpPost("arr-webhook/sonarr/{instanceId}/auto-configure")]
    [ProducesResponseType(typeof(AutoConfigureWebhookResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AutoConfigureSonarrWebhook(string instanceId)
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var config = Plugin.Instance!.Configuration;
        var instance = config.SonarrInstances.FirstOrDefault(i =>
            string.Equals(i.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
        {
            return JsonNotFound(new AutoConfigureWebhookResponse { Success = false, Message = AdminTestMessages.SonarrInstanceNotFound(language) });
        }

        return JsonOk(await AutoConfigureArrWebhookAsync(
            instance.ServerUrl,
            instance.ApiKey,
            instance.IgnoreSslErrors,
            (url, key, ignoreSsl, ct) => _sonarr.GetNotificationsAsync(url, key, ignoreSsl, ct),
            (url, key, ignoreSsl, ct) => _sonarr.GetNotificationSchemasAsync(url, key, ignoreSsl, ct),
            (url, key, notification, ignoreSsl, ct) => _sonarr.CreateNotificationAsync(url, key, notification, ignoreSsl, ct),
            (url, key, candidate, ignoreSsl, ct) => _sonarr.TestNotificationAsync(url, key, candidate, ignoreSsl, ct),
            language).ConfigureAwait(false));
    }

    /// <summary>Creates a Webhook Connect notification in Radarr pointing at JellyNotify's shared arr webhook URL, if one doesn't already exist.</summary>
    [HttpPost("arr-webhook/radarr/{instanceId}/auto-configure")]
    [ProducesResponseType(typeof(AutoConfigureWebhookResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AutoConfigureRadarrWebhook(string instanceId)
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var config = Plugin.Instance!.Configuration;
        var instance = config.RadarrInstances.FirstOrDefault(i =>
            string.Equals(i.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
        {
            return JsonNotFound(new AutoConfigureWebhookResponse { Success = false, Message = AdminTestMessages.RadarrInstanceNotFound(language) });
        }

        return JsonOk(await AutoConfigureArrWebhookAsync(
            instance.ServerUrl,
            instance.ApiKey,
            instance.IgnoreSslErrors,
            (url, key, ignoreSsl, ct) => _radarr.GetNotificationsAsync(url, key, ignoreSsl, ct),
            (url, key, ignoreSsl, ct) => _radarr.GetNotificationSchemasAsync(url, key, ignoreSsl, ct),
            (url, key, notification, ignoreSsl, ct) => _radarr.CreateNotificationAsync(url, key, notification, ignoreSsl, ct),
            (url, key, candidate, ignoreSsl, ct) => _radarr.TestNotificationAsync(url, key, candidate, ignoreSsl, ct),
            language).ConfigureAwait(false));
    }

    /// <summary>
    /// Builds a candidate/created Webhook notification resource from a schema template (its
    /// fields, cloned) with just the "url" field overridden — shared by the capability check
    /// (tested before creation) and the actual auto-configure action (used to create it).
    /// </summary>
    private static ArrNotificationResource BuildArrWebhookCandidate(ArrNotificationResource schema, string arrWebhookUrl)
    {
        var fields = schema.Fields.Select(f => new ArrNotificationField { Name = f.Name, Value = f.Value }).ToList();
        var urlField = fields.FirstOrDefault(f => string.Equals(f.Name, "url", StringComparison.OrdinalIgnoreCase));
        if (urlField is not null)
        {
            urlField.Value = arrWebhookUrl;
        }
        else
        {
            fields.Add(new ArrNotificationField { Name = "url", Value = arrWebhookUrl });
        }

        return new ArrNotificationResource
        {
            Name = "JellyNotify",
            Implementation = schema.Implementation,
            ImplementationName = schema.ImplementationName,
            ConfigContract = schema.ConfigContract,
            OnDownload = true,
            OnUpgrade = true,
            OnGrab = true,
            Fields = fields
        };
    }

    /// <summary>
    /// Shared logic for both the Sonarr and Radarr auto-configure actions: compute the shared
    /// arr webhook URL, skip creation if a "JellyNotify" connection already exists, otherwise
    /// clone the Webhook implementation's schema template with the URL field filled in and
    /// create it. Either way, fires a real test call through the (existing or freshly created)
    /// connection so the instance sends JellyNotify a live webhook call right away — this is
    /// what lets the admin see "Last webhook call received" update without ever opening
    /// Sonarr/Radarr's own UI. Enables the shared *arr webhook toggle on success, since a
    /// connection is useless while JellyNotify itself is still configured to ignore incoming calls.
    /// </summary>
    private async Task<AutoConfigureWebhookResponse> AutoConfigureArrWebhookAsync(
        string serverUrl,
        string apiKey,
        bool ignoreSsl,
        Func<string, string, bool, CancellationToken, Task<IReadOnlyList<ArrNotificationResource>>> getNotifications,
        Func<string, string, bool, CancellationToken, Task<IReadOnlyList<ArrNotificationResource>>> getSchemas,
        Func<string, string, ArrNotificationResource, bool, CancellationToken, Task<(ArrNotificationResource? Created, string? Error)>> createNotification,
        Func<string, string, ArrNotificationResource, bool, CancellationToken, Task<(bool Success, string? Error)>> testNotification,
        string language)
    {
        var config = Plugin.Instance!.Configuration;
        if (string.IsNullOrWhiteSpace(config.ArrWebhookSecret))
        {
            config.ArrWebhookSecret = Guid.NewGuid().ToString("N");
            Plugin.Instance!.SavePluginConfiguration(config);
        }

        var arrWebhookUrl = $"{Request.Scheme}://{Request.Host}/JellyNotify/arr/webhook/{config.ArrWebhookSecret}";
        var ct = HttpContext.RequestAborted;

        var existing = await getNotifications(serverUrl, apiKey, ignoreSsl, ct).ConfigureAwait(false);
        var existingMatch = existing.FirstOrDefault(n => string.Equals(n.Name, "JellyNotify", StringComparison.OrdinalIgnoreCase));
        if (existingMatch is not null)
        {
            await testNotification(serverUrl, apiKey, existingMatch, ignoreSsl, ct).ConfigureAwait(false);
            config.ArrWebhookEnabled = true;
            Plugin.Instance!.SavePluginConfiguration(config);
            return new AutoConfigureWebhookResponse { Success = true, AlreadyExists = true, Message = AdminTestMessages.ArrWebhookAlreadyConfigured(language) };
        }

        var schemas = await getSchemas(serverUrl, apiKey, ignoreSsl, ct).ConfigureAwait(false);
        var webhookSchema = schemas.FirstOrDefault(s => string.Equals(s.Implementation, "Webhook", StringComparison.OrdinalIgnoreCase));
        if (webhookSchema is null)
        {
            return new AutoConfigureWebhookResponse { Success = false, Message = AdminTestMessages.ArrWebhookSchemaNotFound(language) };
        }

        var candidate = BuildArrWebhookCandidate(webhookSchema, arrWebhookUrl);
        var (created, createError) = await createNotification(serverUrl, apiKey, candidate, ignoreSsl, ct).ConfigureAwait(false);
        if (created is null)
        {
            return new AutoConfigureWebhookResponse { Success = false, Message = AdminTestMessages.ArrWebhookCreateFailed(createError ?? AdminTestMessages.ArrWebhookTestNoDetail(language), language) };
        }

        await testNotification(serverUrl, apiKey, created, ignoreSsl, ct).ConfigureAwait(false);

        config.ArrWebhookEnabled = true;
        Plugin.Instance!.SavePluginConfiguration(config);
        return new AutoConfigureWebhookResponse { Success = true, Message = AdminTestMessages.ArrWebhookCreateSuccess(language) };
    }

    /// <summary>Configures Seerr's webhook notification agent to point at JellyNotify's Seerr webhook URL, unless a different one is already active.</summary>
    [HttpPost("seerr-webhook/auto-configure")]
    [ProducesResponseType(typeof(AutoConfigureWebhookResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AutoConfigureSeerrWebhook()
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var config = Plugin.Instance!.Configuration;
        if (string.IsNullOrWhiteSpace(config.SeerrSettings.WebhookSecret))
        {
            config.SeerrSettings.WebhookSecret = Guid.NewGuid().ToString("N");
            Plugin.Instance!.SavePluginConfiguration(config);
        }

        var seerrWebhookUrl = $"{Request.Scheme}://{Request.Host}/JellyNotify/seerr/webhook/{config.SeerrSettings.WebhookSecret}";
        var candidate = BuildSeerrWebhookCandidate(seerrWebhookUrl);

        var current = await _seerr.GetWebhookSettingsAsync().ConfigureAwait(false);
        if (current is null)
        {
            return JsonOk(new AutoConfigureWebhookResponse { Success = false, Message = AdminTestMessages.SeerrConnectFail(language) });
        }

        if (current.Enabled && !string.IsNullOrWhiteSpace(current.Options.WebhookUrl)
            && !string.Equals(current.Options.WebhookUrl, seerrWebhookUrl, StringComparison.Ordinal))
        {
            return JsonOk(new AutoConfigureWebhookResponse { Success = false, Message = AdminTestMessages.SeerrWebhookConflict(language) });
        }

        if (current.Enabled && string.Equals(current.Options.WebhookUrl, seerrWebhookUrl, StringComparison.Ordinal))
        {
            await _seerr.TestWebhookSettingsAsync(candidate).ConfigureAwait(false);
            config.SeerrSettings.WebhookEnabled = true;
            Plugin.Instance!.SavePluginConfiguration(config);
            return JsonOk(new AutoConfigureWebhookResponse { Success = true, AlreadyExists = true, Message = AdminTestMessages.SeerrWebhookAlreadyConfigured(language) });
        }

        var (success, createError) = await _seerr.SetWebhookSettingsAsync(candidate).ConfigureAwait(false);
        if (!success)
        {
            return JsonOk(new AutoConfigureWebhookResponse { Success = false, Message = AdminTestMessages.SeerrWebhookCreateFailed(createError ?? AdminTestMessages.ArrWebhookTestNoDetail(language), language) });
        }

        await _seerr.TestWebhookSettingsAsync(candidate).ConfigureAwait(false);

        config.SeerrSettings.WebhookEnabled = true;
        Plugin.Instance!.SavePluginConfiguration(config);
        return JsonOk(new AutoConfigureWebhookResponse { Success = true, Message = AdminTestMessages.SeerrWebhookCreateSuccess(language) });
    }

    // Bitmask of Seerr's Notification enum values relevant to media request status changes:
    // MEDIA_PENDING(2) + MEDIA_APPROVED(4) + MEDIA_AVAILABLE(8) + MEDIA_FAILED(16) +
    // TEST_NOTIFICATION(32) + MEDIA_DECLINED(64) + MEDIA_AUTO_APPROVED(128), confirmed against
    // Overseerr's own source (server/lib/notifications/index.ts).
    private const int SeerrWebhookAllRelevantTypes = 254;

    private const string SeerrWebhookPayloadTemplate = "{\"notification_type\":\"{{notification_type}}\",\"request\":{\"request_id\":\"{{request_id}}\"}}";

    /// <summary>
    /// Builds the webhook settings JellyNotify wants Seerr to have — shared by the capability
    /// check, the test-trigger call, and the actual auto-configure action. <see
    /// cref="SeerrWebhookOptions.JsonPayload"/> is always sent as the raw JSON template text,
    /// never pre-base64-encoded: confirmed against Seerr's own source (both <c>POST
    /// /settings/notifications/webhook</c> and <c>POST .../webhook/test</c> call
    /// <c>JSON.parse()</c> directly on whatever is sent, then base64-encode it themselves
    /// before storing/using it) — sending an already-base64 string makes both endpoints try to
    /// parse base64 text as JSON and fail with "is not valid JSON".
    /// </summary>
    private static SeerrWebhookSettings BuildSeerrWebhookCandidate(string seerrWebhookUrl) => new()
    {
        Enabled = true,
        Types = SeerrWebhookAllRelevantTypes,
        Options = new SeerrWebhookOptions
        {
            WebhookUrl = seerrWebhookUrl,
            JsonPayload = SeerrWebhookPayloadTemplate,
            // Seerr's own settings schema requires this to be a string, not null/absent —
            // omitting it entirely fails validation with "authHeader must be string".
            AuthHeader = string.Empty
        }
    };

    /// <summary>
    /// Fetches real counts from a Sonarr instance (total series, anything currently
    /// downloading) and returns them formatted as a sample notification message,
    /// so admins can see real data without waiting for an actual event.
    /// </summary>
    [HttpPost("test/sonarr/{instanceId}/sample")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SampleSonarr(string instanceId)
    {
        // The response text and the real dispatched notification should read the same
        // language, so this resolves the calling admin's own preference once and reuses
        // it for both, rather than falling back to the server-wide default for one of them.
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var config = Plugin.Instance!.Configuration;
        var instance = config.SonarrInstances.FirstOrDefault(i =>
            string.Equals(i.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
        {
            return JsonNotFound(new TestConnectionResponse { Success = false, Message = AdminTestMessages.SonarrInstanceNotFound(language) });
        }

        var series = await _sonarr.GetAllSeriesAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors).ConfigureAwait(false);
        var queue = await _sonarr.GetQueueAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors).ConfigureAwait(false);
        var downloadingCount = queue?.TotalRecords ?? 0;

        var message = downloadingCount > 0
            ? AdminTestMessages.SonarrSampleWithDownloads(series.Count, downloadingCount, language)
            : AdminTestMessages.SonarrSampleNoDownloads(series.Count, language);

        await DispatchSampleToCurrentAdminAsync("Sonarr", message, language).ConfigureAwait(false);
        return JsonOk(new TestConnectionResponse { Success = true, Message = message });
    }

    /// <summary>
    /// Fetches real counts from a Radarr instance (total movies, anything currently
    /// downloading) and returns them formatted as a sample notification message.
    /// </summary>
    [HttpPost("test/radarr/{instanceId}/sample")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SampleRadarr(string instanceId)
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var config = Plugin.Instance!.Configuration;
        var instance = config.RadarrInstances.FirstOrDefault(i =>
            string.Equals(i.Id, instanceId, StringComparison.OrdinalIgnoreCase));

        if (instance is null)
        {
            return JsonNotFound(new TestConnectionResponse { Success = false, Message = AdminTestMessages.RadarrInstanceNotFound(language) });
        }

        var movies = await _radarr.GetAllMoviesAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors).ConfigureAwait(false);
        var queue = await _radarr.GetQueueAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors).ConfigureAwait(false);
        var downloadingCount = queue?.TotalRecords ?? 0;

        var message = downloadingCount > 0
            ? AdminTestMessages.RadarrSampleWithDownloads(movies.Count, downloadingCount, language)
            : AdminTestMessages.RadarrSampleNoDownloads(movies.Count, language);

        await DispatchSampleToCurrentAdminAsync("Radarr", message, language).ConfigureAwait(false);
        return JsonOk(new TestConnectionResponse { Success = true, Message = message });
    }

    /// <summary>
    /// Fetches real request counts from Seerr (movies, series, and how many are
    /// still pending) and returns them formatted as a sample notification message.
    /// </summary>
    [HttpPost("test/seerr/sample")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SampleSeerr()
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var requests = await _seerr.GetAllRequestsAsync().ConfigureAwait(false);
        if (requests is null)
        {
            return JsonOk(new TestConnectionResponse { Success = false, Message = AdminTestMessages.SeerrConnectFail(language) });
        }

        var movieCount = requests.Count(r => string.Equals(r.Type, "movie", StringComparison.OrdinalIgnoreCase));
        var tvCount = requests.Count(r => string.Equals(r.Type, "tv", StringComparison.OrdinalIgnoreCase));
        var pendingCount = requests.Count(r => r.Status == 1);

        var message = AdminTestMessages.SeerrSampleSummary(movieCount, tvCount, pendingCount, language);
        await DispatchSampleToCurrentAdminAsync("Serr", message, language).ConfigureAwait(false);
        return JsonOk(new TestConnectionResponse { Success = true, Message = message });
    }

    /// <summary>
    /// Dispatches the given sample text through the full notification pipeline (in-app +
    /// Discord/Telegram/WhatsApp, per the current admin's own preferences) so clicking
    /// Sample notification actually verifies delivery, not just shows text in the browser.
    /// Best-effort: if the caller's user ID can't be resolved, the inline text response
    /// (already returned separately) is still useful on its own, so this silently no-ops
    /// rather than failing the whole request.
    /// </summary>
    private async Task DispatchSampleToCurrentAdminAsync(string sourceLabel, string sampleMessage, string language)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return;
        }

        var (title, _) = TestMessages.General(language);

        await _dispatcher.DispatchAsync(new NotificationEvent
        {
            JellyfinUserId = userId,
            Type = NotificationType.TestNotification,
            Title = $"{title} ({sourceLabel})",
            Message = sampleMessage,
            CreatedAt = DateTime.UtcNow
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the language for text shown directly on the config page (connection test
    /// results, webhook capability messages, sample-notification text) and for any real
    /// notification dispatched to the admin alongside it, so both read consistently.
    /// Prefers the plugin's own "Default language" setting first — this is a config-page
    /// field the admin can see and change right there, so a message on that same page
    /// should follow it, rather than silently falling back to the admin's own personal
    /// notification-recipient preference (which exists for the very different case of
    /// formatting a notification actually delivered to them as a bell/Discord/Telegram/
    /// WhatsApp recipient, and may have auto-resolved to something else entirely, e.g. the
    /// browser/Jellyfin session language). Only falls through to that per-user preference
    /// if "Default language" itself is left on "auto".
    /// </summary>
    private async Task<string> ResolveCurrentAdminLanguageAsync()
    {
        var configDefault = Plugin.Instance?.Configuration.DefaultLanguage;
        if (NotificationLanguage.IsSupported(configDefault))
        {
            return configDefault!;
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return NotificationLanguage.ResolveAdminDefault();
        }

        var prefs = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        return NotificationLanguage.Resolve(prefs);
    }

    /// <summary>
    /// Extracts the current Jellyfin user ID from the HTTP context, same claim precedence
    /// as <see cref="Api.NotificationsController"/>'s equivalent helper.
    /// </summary>
    private string? GetCurrentUserId()
    {
        var userIdClaim = HttpContext.User?.FindFirst("Jellyfin-UserId")
            ?? HttpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? HttpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Name);

        return userIdClaim?.Value;
    }

    /// <summary>
    /// Reports the most recent Telegram chat ID seen by the background linking poller, so
    /// the admin doesn't have to know or type it manually — they just message the bot once
    /// and click this. Reads from the poller's shared state rather than calling Telegram's
    /// getUpdates directly here: a second, independent call would race the poller (whichever
    /// one reaches Telegram first "consumes" the update from the server's perspective), which
    /// previously made this endpoint reliably report an empty/zero result.
    /// </summary>
    [HttpPost("telegram/detect-chat-id")]
    [ProducesResponseType(typeof(TelegramDetectChatIdResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DetectTelegramChatId()
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.TelegramSettings;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.BotToken))
        {
            return JsonOk(new TelegramDetectChatIdResponse { Success = false, Message = AdminTestMessages.TelegramNotConfigured(language) });
        }

        var chatId = _telegramActivity.LastChatId;
        if (chatId is null)
        {
            return JsonOk(new TelegramDetectChatIdResponse
            {
                Success = false,
                Message = AdminTestMessages.TelegramNoMessagesFound(language)
            });
        }

        return JsonOk(new TelegramDetectChatIdResponse { Success = true, Message = AdminTestMessages.TelegramChatIdFound(chatId, language), ChatId = chatId });
    }

    /// <summary>Sends a test message to the configured Telegram global chat ID.</summary>
    [HttpPost("test/telegram")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestTelegram()
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.TelegramSettings;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.BotToken))
        {
            return JsonOk(new TestConnectionResponse { Success = false, Message = AdminTestMessages.TelegramNotConfigured(language) });
        }

        if (string.IsNullOrWhiteSpace(settings.ChatId))
        {
            return JsonOk(new TestConnectionResponse { Success = false, Message = AdminTestMessages.TelegramNoChatId(language) });
        }

        var (channelTitle, channelMessage) = TestMessages.Channel(language);
        var notification = new NotificationEvent
        {
            Type = NotificationType.TestNotification,
            Title = channelTitle,
            Message = channelMessage,
            CreatedAt = DateTime.UtcNow
        };

        var success = await _telegram.SendAsync(notification, settings.ChatId).ConfigureAwait(false);
        return JsonOk(success
            ? new TestConnectionResponse { Success = true, Message = AdminTestMessages.TelegramTestSent(language) }
            : new TestConnectionResponse { Success = false, Message = AdminTestMessages.TelegramTestRejected(language) });
    }

    /// <summary>Sends a test embed to the configured Discord webhook.</summary>
    [HttpPost("test/discord")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestDiscord()
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var settings = Plugin.Instance!.Configuration.ExternalChannelSettings.DiscordSettings;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return JsonOk(new TestConnectionResponse { Success = false, Message = AdminTestMessages.DiscordNotConfigured(language) });
        }

        var (channelTitle, channelMessage) = TestMessages.Channel(language);
        var notification = new NotificationEvent
        {
            Type = NotificationType.TestNotification,
            Title = channelTitle,
            Message = channelMessage,
            CreatedAt = DateTime.UtcNow
        };

        var success = await _discord.SendAsync(notification).ConfigureAwait(false);
        return JsonOk(success
            ? new TestConnectionResponse { Success = true, Message = AdminTestMessages.DiscordTestSent(language) }
            : new TestConnectionResponse { Success = false, Message = AdminTestMessages.DiscordTestRejected(language) });
    }

    /// <summary>
    /// If Cloud API credentials are configured, verifies them against the Meta Graph API
    /// (confirming the access token and Phone Number ID actually work). Otherwise falls back
    /// to building a wa.me test link — which only confirms the number is configured, not that
    /// anything was actually sent, since link-only mode has no server-side sending at all.
    /// </summary>
    [HttpPost("test/whatsapp")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestWhatsApp()
    {
        var language = await ResolveCurrentAdminLanguageAsync().ConfigureAwait(false);
        var settings = Plugin.Instance!.Configuration.WhatsAppSettings;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.PhoneNumber))
        {
            return JsonOk(new TestConnectionResponse { Success = false, Message = AdminTestMessages.WhatsAppNotConfigured(language) });
        }

        if (!string.IsNullOrWhiteSpace(settings.AccessToken) && !string.IsNullOrWhiteSpace(settings.PhoneNumberId))
        {
            var check = await _whatsApp.VerifyCredentialsAsync().ConfigureAwait(false);
            return JsonOk(check.Success
                ? new TestConnectionResponse { Success = true, Message = AdminTestMessages.WhatsAppCloudApiValid(check.DisplayPhoneNumber ?? string.Empty, language) }
                : new TestConnectionResponse { Success = false, Message = check.Error ?? AdminTestMessages.WhatsAppCloudApiInvalidFallback(language) });
        }

        var message = Uri.EscapeDataString("JellyNotify test message");
        var waMeUrl = $"https://wa.me/{settings.PhoneNumber}?text={message}";
        return JsonOk(new TestConnectionResponse { Success = true, Message = AdminTestMessages.WhatsAppLinkOnly(waMeUrl, language) });
    }

    /// <summary>
    /// Sends a test notification to the currently authenticated admin, via the
    /// admin-only route (distinct from the regular-user self-test endpoint).
    /// </summary>
    [HttpPost("test-notification")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> TestNotification()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var prefs = await _preferenceStore.GetByUserAsync(userId).ConfigureAwait(false);
        var (title, message) = TestMessages.General(NotificationLanguage.Resolve(prefs));

        await _dispatcher.DispatchAsync(new NotificationEvent
        {
            JellyfinUserId = userId,
            Type = NotificationType.TestNotification,
            Title = title,
            Message = message,
            CreatedAt = DateTime.UtcNow
        }).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Admin view: gets all notifications for a specific user.
    /// This is a separate admin-only endpoint to avoid exposing user data via the regular notification API.
    /// </summary>
    [HttpGet("notifications/{userId}")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationEvent>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserNotifications(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest();
        }

        var notifications = await _notificationStore.GetByUserAsync(userId).ConfigureAwait(false);
        return Ok(notifications);
    }

}


/// <summary>Plugin configuration DTO that redacts sensitive API keys.</summary>
public sealed class PluginConfigDto
{
    /// <summary>Initializes a new instance of the <see cref="PluginConfigDto"/> class.</summary>
    public PluginConfigDto(PluginConfiguration config)
    {
        SeerrEnabled = config.SeerrSettings.Enabled;
        SeerrServerUrl = config.SeerrSettings.ServerUrl;
        SeerrType = config.SeerrSettings.SeerrType.ToString();
        SeerrHasApiKey = !string.IsNullOrWhiteSpace(config.SeerrSettings.ApiKey);
        SeerrIgnoreSslErrors = config.SeerrSettings.IgnoreSslErrors;
        SeerrWebhookEnabled = config.SeerrSettings.WebhookEnabled;
        SeerrWebhookLastReceivedAt = config.SeerrSettings.LastWebhookReceivedAt;
        ArrWebhookEnabled = config.ArrWebhookEnabled;
        ArrWebhookLastReceivedAt = config.ArrWebhookLastReceivedAt;
        SonarrInstances = config.SonarrInstances.Select(i => new ArrInstanceDto(i)).ToList();
        RadarrInstances = config.RadarrInstances.Select(i => new ArrInstanceDto(i)).ToList();
        NotificationSettings = config.NotificationSettings;
        DefaultLanguage = NormalizeLanguage(config.DefaultLanguage);
        NotificationRetentionDays = config.NotificationRetentionDays;
        DiscordEnabled = config.ExternalChannelSettings.DiscordSettings.Enabled;
        DiscordHasWebhook = !string.IsNullOrWhiteSpace(config.ExternalChannelSettings.DiscordSettings.WebhookUrl);
        DiscordHasBotToken = !string.IsNullOrWhiteSpace(config.ExternalChannelSettings.DiscordSettings.BotToken);
        DiscordClientId = config.ExternalChannelSettings.DiscordSettings.ClientId;
        DiscordHasClientSecret = !string.IsNullOrWhiteSpace(config.ExternalChannelSettings.DiscordSettings.ClientSecret);
        TelegramEnabled = config.ExternalChannelSettings.TelegramSettings.Enabled;
        TelegramHasBotToken = !string.IsNullOrWhiteSpace(config.ExternalChannelSettings.TelegramSettings.BotToken);
        TelegramBotUsername = config.ExternalChannelSettings.TelegramSettings.BotUsername;
        WhatsAppEnabled = config.WhatsAppSettings.Enabled;
        WhatsAppPhoneNumber = config.WhatsAppSettings.PhoneNumber;
        WhatsAppHasAccessToken = !string.IsNullOrWhiteSpace(config.WhatsAppSettings.AccessToken);
        WhatsAppPhoneNumberId = config.WhatsAppSettings.PhoneNumberId;
        WhatsAppVerifyToken = config.WhatsAppSettings.VerifyToken;
        DisableScriptInjectionMiddleware = config.DisableScriptInjectionMiddleware;
    }

    /// <summary>Gets a value indicating whether Seerr integration is enabled.</summary>
    public bool SeerrEnabled { get; }

    /// <summary>Gets the Seerr server URL.</summary>
    public string SeerrServerUrl { get; }

    /// <summary>Gets the Seerr server type.</summary>
    public string SeerrType { get; }

    /// <summary>Gets a value indicating whether a Seerr API key is configured.</summary>
    public bool SeerrHasApiKey { get; }

    /// <summary>Gets a value indicating whether SSL certificate validation is skipped for Seerr.</summary>
    public bool SeerrIgnoreSslErrors { get; }

    /// <summary>Gets a value indicating whether Overseerr/Jellyseerr's own webhook agent is accepted for instant delivery.</summary>
    public bool SeerrWebhookEnabled { get; }

    /// <summary>Gets or sets the webhook URL to enter in Overseerr/Jellyseerr's Settings → Notifications → Webhook, computed from the current request.</summary>
    public string? SeerrWebhookUrl { get; set; }

    /// <summary>Gets when the Seerr webhook last received a validated call (including a Test), or null if it never has.</summary>
    public DateTime? SeerrWebhookLastReceivedAt { get; }

    /// <summary>Gets a value indicating whether Sonarr/Radarr Connect webhook calls are accepted for instant delivery, shared across every configured instance.</summary>
    public bool ArrWebhookEnabled { get; }

    /// <summary>Gets when the shared *arr webhook last received a validated call (including a Test, from any instance), or null if it never has.</summary>
    public DateTime? ArrWebhookLastReceivedAt { get; }

    /// <summary>Gets the Sonarr instance list (without API keys).</summary>
    public List<ArrInstanceDto> SonarrInstances { get; }

    /// <summary>Gets the Radarr instance list (without API keys).</summary>
    public List<ArrInstanceDto> RadarrInstances { get; }

    /// <summary>Gets the notification delivery settings.</summary>
    public NotificationSettings NotificationSettings { get; }

    /// <summary>Gets the default language for users with automatic language selection.</summary>
    public string DefaultLanguage { get; }

    /// <summary>Gets the notification retention period in days.</summary>
    public int NotificationRetentionDays { get; }

    /// <summary>Gets a value indicating whether Discord notifications are enabled.</summary>
    public bool DiscordEnabled { get; }

    /// <summary>Gets a value indicating whether a Discord webhook is configured.</summary>
    public bool DiscordHasWebhook { get; }

    /// <summary>Gets a value indicating whether a Discord bot token is configured (enables per-user DM connect).</summary>
    public bool DiscordHasBotToken { get; }

    /// <summary>Gets the Discord OAuth2 Client ID (not sensitive — safe to display as-is).</summary>
    public string? DiscordClientId { get; }

    /// <summary>Gets a value indicating whether a Discord OAuth2 Client Secret is configured.</summary>
    public bool DiscordHasClientSecret { get; }

    /// <summary>Gets a value indicating whether Telegram notifications are enabled.</summary>
    public bool TelegramEnabled { get; }

    /// <summary>Gets a value indicating whether a Telegram bot token is configured.</summary>
    public bool TelegramHasBotToken { get; }

    /// <summary>Gets the bot's public @username, used to build the Connect Telegram deep link.</summary>
    public string? TelegramBotUsername { get; }

    /// <summary>Gets a value indicating whether WhatsApp is enabled.</summary>
    public bool WhatsAppEnabled { get; }

    /// <summary>Gets the admin/bot WhatsApp phone number used to build wa.me links.</summary>
    public string? WhatsAppPhoneNumber { get; }

    /// <summary>Gets a value indicating whether a Cloud API access token is configured.</summary>
    public bool WhatsAppHasAccessToken { get; }

    /// <summary>Gets the Cloud API Phone Number ID.</summary>
    public string? WhatsAppPhoneNumberId { get; }

    /// <summary>Gets the shared verify token used for the Meta webhook handshake (not a secret — just needs to match what's entered in Meta's dashboard).</summary>
    public string? WhatsAppVerifyToken { get; }

    /// <summary>Gets or sets the webhook URL to enter in the Meta dashboard, computed from the current request.</summary>
    public string? WhatsAppWebhookUrl { get; set; }

    /// <summary>Gets or sets the webhook URL to enter in every Sonarr/Radarr instance's Settings → Connect → Webhook that should deliver instantly — the same URL/secret is shared by all configured instances.</summary>
    public string? ArrWebhookUrl { get; set; }

    /// <summary>Gets a value indicating whether the global web-injection middleware is disabled.</summary>
    public bool DisableScriptInjectionMiddleware { get; }

    private static string NormalizeLanguage(string? language) =>
        string.Equals(language, "es-ES", StringComparison.OrdinalIgnoreCase) ? "es-ES" :
        string.Equals(language, "ca", StringComparison.OrdinalIgnoreCase) ? "ca" :
        string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" :
        "auto";
}

/// <summary>*arr instance DTO without sensitive fields.</summary>
public sealed class ArrInstanceDto
{
    /// <summary>Initializes a new instance of the <see cref="ArrInstanceDto"/> class.</summary>
    public ArrInstanceDto(ArrInstanceConfig config)
    {
        Id = config.Id;
        Name = config.Name;
        Enabled = config.Enabled;
        ServerUrl = config.ServerUrl;
        HasApiKey = !string.IsNullOrWhiteSpace(config.ApiKey);
        IgnoreSslErrors = config.IgnoreSslErrors;
        PollingIntervalSeconds = config.PollingIntervalSeconds;
    }

    /// <summary>Gets the instance ID.</summary>
    public string Id { get; }

    /// <summary>Gets the instance display name.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether this instance is enabled.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the server URL.</summary>
    public string ServerUrl { get; }

    /// <summary>Gets a value indicating whether an API key is configured.</summary>
    public bool HasApiKey { get; }

    /// <summary>Gets a value indicating whether SSL certificate validation is skipped for this instance.</summary>
    public bool IgnoreSslErrors { get; }

    /// <summary>Gets the polling interval in seconds for this instance's download queue (progress/stalled/failed tracking — Sonarr/Radarr have no webhook for this).</summary>
    public int PollingIntervalSeconds { get; }
}

/// <summary>Response DTO for connection tests.</summary>
public sealed class TestConnectionResponse
{
    /// <summary>Gets or sets a value indicating whether the test succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets a human-readable result message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the automatic webhook was confirmed working
    /// (Seerr/Sonarr/Radarr only — <see langword="null"/> for connection tests that don't
    /// check this, e.g. Discord/Telegram/WhatsApp). Kept separate from <see cref="Success"/>
    /// so the UI can show connection success and webhook capability as two independent
    /// results instead of collapsing a possible webhook failure into the same green message
    /// as a successful connection.
    /// </summary>
    public bool? WebhookCapable { get; set; }

    /// <summary>Gets or sets the human-readable webhook capability result, shown separately from <see cref="Message"/>.</summary>
    public string? WebhookMessage { get; set; }
}

/// <summary>Response DTO for the Telegram chat-ID auto-detect action.</summary>
public sealed class TelegramDetectChatIdResponse
{
    /// <summary>Gets or sets a value indicating whether a chat ID was found.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets a human-readable result message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the detected chat ID, if any.</summary>
    public string? ChatId { get; set; }
}

/// <summary>Response DTO for the "Create webhook automatically" actions.</summary>
public sealed class AutoConfigureWebhookResponse
{
    /// <summary>Gets or sets a value indicating whether the webhook is now correctly configured (either just created, or already was).</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets a value indicating whether nothing was created because one already existed.</summary>
    public bool AlreadyExists { get; set; }

    /// <summary>Gets or sets a human-readable result message.</summary>
    public string Message { get; set; } = string.Empty;
}
