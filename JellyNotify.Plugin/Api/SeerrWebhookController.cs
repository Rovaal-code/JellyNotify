using Jellyfin.Plugin.JellyNotify.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Api;

/// <summary>
/// Receives inbound webhook calls from Overseerr/Jellyseerr's own "Webhook" notification
/// agent, so request status changes (approved, available, declined, etc.) are processed
/// instantly instead of waiting for the next poll cycle. Unlike the rest of the plugin's
/// API, this endpoint is intentionally unauthenticated — Seerr's webhook agent has no
/// custom-header auth in most versions — so the unguessable secret embedded in the URL
/// path (<see cref="Configuration.SeerrSettings.WebhookSecret"/>) is the only "secret" a
/// caller needs to know, same principle as the *arr instance-ID-in-path approach in
/// <see cref="ArrWebhookController"/> but for a single global endpoint.
/// </summary>
[ApiController]
[Route("JellyNotify/seerr/webhook/{secret}")]
[AllowAnonymous]
public sealed class SeerrWebhookController : ControllerBase
{
    private readonly IMediaRequestService _mediaRequestService;
    private readonly ILogger<SeerrWebhookController> _logger;

    /// <summary>Initializes a new instance of the <see cref="SeerrWebhookController"/> class.</summary>
    public SeerrWebhookController(IMediaRequestService mediaRequestService, ILogger<SeerrWebhookController> logger)
    {
        _mediaRequestService = mediaRequestService;
        _logger = logger;
    }

    /// <summary>Receives an inbound Seerr webhook call. Always acknowledges quickly, per the same fast-ack convention as the other inbound webhooks.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(string secret, [FromBody] SeerrWebhookPayload? payload, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessAsync(secret, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Seerr webhook");
        }

        return Ok();
    }

    private async Task ProcessAsync(string secret, SeerrWebhookPayload? payload, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var settings = config.SeerrSettings;
        if (!settings.WebhookEnabled
            || string.IsNullOrWhiteSpace(settings.WebhookSecret)
            || !string.Equals(secret, settings.WebhookSecret, StringComparison.Ordinal))
        {
            return;
        }

        // Recorded for any authenticated call, including Seerr's own TEST_NOTIFICATION
        // (its "Test" button) — this is what lets the admin UI show a visible
        // confirmation that JellyNotify actually received it.
        settings.LastWebhookReceivedAt = DateTime.UtcNow;
        Plugin.Instance!.SavePluginConfiguration(config);

        if (payload?.Request?.RequestId is not string ridStr || !int.TryParse(ridStr, out var requestId))
        {
            // TEST_NOTIFICATION / ISSUE_* events, or a payload template that doesn't
            // include the request object — nothing to process.
            return;
        }

        await _mediaRequestService.ProcessSingleRequestAsync(requestId, cancellationToken).ConfigureAwait(false);
    }
}
