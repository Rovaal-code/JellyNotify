using Jellyfin.Plugin.JellyNotify.Models;
using Jellyfin.Plugin.JellyNotify.Services;
using Jellyfin.Plugin.JellyNotify.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Api;

/// <summary>
/// Receives inbound Sonarr/Radarr "Connect" webhook calls for instant notification
/// delivery, in parallel to the existing poll-based path (see
/// <see cref="ArrSyncService"/>). Unlike the rest of the plugin's API, this endpoint is
/// intentionally unauthenticated — Sonarr/Radarr Connect webhooks carry no bearer/API-key
/// header by default — so a single shared, unguessable secret embedded in the URL path
/// (<see cref="Configuration.PluginConfiguration.ArrWebhookSecret"/>) is the only "secret"
/// a caller needs to know. The same URL/secret is pasted into every Sonarr/Radarr instance
/// that should deliver instantly — because of that, an inbound call can't be traced back
/// to a specific configured instance, so <see cref="NotificationEvent.ArrInstanceName"/>
/// is left unset here (unlike the polling path, which always knows the instance).
/// </summary>
[ApiController]
[Route("JellyNotify/arr/webhook/{secret}")]
[AllowAnonymous]
public sealed class ArrWebhookController : ControllerBase
{
    private readonly IRequestSnapshotStore _snapshotStore;
    private readonly IUserPreferenceStore _preferenceStore;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<ArrWebhookController> _logger;

    /// <summary>Initializes a new instance of the <see cref="ArrWebhookController"/> class.</summary>
    public ArrWebhookController(
        IRequestSnapshotStore snapshotStore,
        IUserPreferenceStore preferenceStore,
        INotificationDispatcher dispatcher,
        ILogger<ArrWebhookController> logger)
    {
        _snapshotStore = snapshotStore;
        _preferenceStore = preferenceStore;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>Receives an inbound Sonarr/Radarr Connect webhook call. Always acknowledges quickly, per *arr's expectations.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(string secret, [FromBody] ArrWebhookPayload? payload, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessAsync(secret, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing *arr webhook");
        }

        return Ok();
    }

    private async Task ProcessAsync(string secret, ArrWebhookPayload? payload, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        if (!config.ArrWebhookEnabled
            || string.IsNullOrWhiteSpace(config.ArrWebhookSecret)
            || !string.Equals(secret, config.ArrWebhookSecret, StringComparison.Ordinal))
        {
            return;
        }

        // Recorded for any authenticated call, including a Sonarr/Radarr Connect "Test"
        // event — this is what lets the admin UI show a visible confirmation that
        // JellyNotify actually received it. Since the secret is shared across every
        // instance, this can't say which specific instance sent it, only that the
        // shared endpoint is reachable and accepting calls.
        config.ArrWebhookLastReceivedAt = DateTime.UtcNow;
        Plugin.Instance!.SavePluginConfiguration(config);

        if (payload is null)
        {
            return;
        }

        // Only import events (Download/Upgrade) notify from the webhook. Grab used to fire
        // "download started" the instant a release was sent to the download client, but at
        // that point nothing has transferred yet — it arrived with no progress and no ETA.
        // "Download started" is now driven entirely by the queue poll, which waits until a
        // real transfer is underway (progress > 0 with an ETA), so Grab is deliberately
        // ignored here. Download/Upgrade still carry the import + MediaInfo data for the
        // "available" notification; everything else (Grab/HealthIssue/Test/Rename/etc.) is
        // not notification-worthy.
        var isImport = string.Equals(payload.EventType, "Download", StringComparison.OrdinalIgnoreCase)
            || string.Equals(payload.EventType, "Upgrade", StringComparison.OrdinalIgnoreCase);

        if (!isImport)
        {
            return;
        }

        var snapshots = await _snapshotStore.GetAllAsync().ConfigureAwait(false);
        var mediaType = payload.Series is not null ? "tv" : "movie";
        var matched = payload.Series is not null
            ? ArrSyncService.FindSnapshotsForSeries(snapshots, new ArrSeries { TvdbId = payload.Series.TvdbId })
            : ArrSyncService.FindSnapshotsForMovie(snapshots, new ArrMovie { TmdbId = payload.Movie?.TmdbId });

        if (matched.Count == 0)
        {
            return;
        }

        var mediaTitle = payload.Series?.Title ?? payload.Movie?.Title;

        await DispatchImportAsync(matched, mediaType, mediaTitle, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchImportAsync(
        IReadOnlyList<RequestSnapshot> matched,
        string mediaType,
        string? mediaTitle,
        ArrWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        var mediaFile = payload.EpisodeFile ?? payload.MovieFile;
        var audioLanguages = JoinOrNull(mediaFile?.MediaInfo?.AudioLanguages);
        var subtitleLanguages = JoinOrNull(mediaFile?.MediaInfo?.Subtitles);
        var quality = mediaFile?.Quality?.Quality?.Name;
        var season = payload.Episodes?.FirstOrDefault()?.SeasonNumber;

        foreach (var snapshot in matched.Where(s => !string.IsNullOrWhiteSpace(s.JellyfinUserId)))
        {
            var prefs = await _preferenceStore.GetByUserAsync(snapshot.JellyfinUserId).ConfigureAwait(false);
            var language = NotificationLanguage.Resolve(prefs);

            // A movie import is always a complete availability; a single Sonarr episode
            // import is only a full "available" if Seerr already considers the whole
            // series available — otherwise it's a partial update, same distinction the
            // polling path makes from Seerr's own status.
            var isFullyAvailable = mediaType == "movie" || snapshot.Status.Contains("media:Available", StringComparison.Ordinal);
            var type = isFullyAvailable ? NotificationType.MediaAvailable : NotificationType.MediaPartiallyAvailable;
            var (title, message) = isFullyAvailable
                ? NotificationText.MediaAvailable(mediaTitle ?? snapshot.MediaTitle, language)
                : NotificationText.MediaPartiallyAvailable(mediaTitle ?? snapshot.MediaTitle, language);

            await _dispatcher.DispatchAsync(new NotificationEvent
            {
                JellyfinUserId = snapshot.JellyfinUserId,
                Type = type,
                Title = title,
                Message = message,
                MediaTitle = mediaTitle ?? snapshot.MediaTitle,
                MediaType = mediaType,
                ExternalIds = snapshot.ExternalIds,
                ThumbnailUrl = snapshot.PosterUrl,
                Year = snapshot.Year,
                SeerrRequestId = snapshot.SeerrRequestId,
                Quality = quality,
                AudioLanguages = audioLanguages,
                SubtitleLanguages = subtitleLanguages,
                Season = isFullyAvailable ? null : season,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? JoinOrNull(List<string>? values) =>
        values is { Count: > 0 } ? string.Join(", ", values) : null;
}
