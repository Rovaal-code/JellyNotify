using Jellyfin.Plugin.JellyNotify.Models;
using Jellyfin.Plugin.JellyNotify.Services;
using Jellyfin.Plugin.JellyNotify.Store;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Polls all configured Sonarr and Radarr instances for download activity.
/// Correlates downloads to Jellyfin users via Seerr request snapshots using
/// external IDs (TMDb, TVDb, IMDb). Only dispatches to users who can be
/// securely identified — ambiguous downloads are silently skipped.
/// </summary>
public sealed class ArrSyncService : IArrSyncService
{
    private readonly ISonarrApiClient _sonarr;
    private readonly IRadarrApiClient _radarr;
    private readonly IRequestSnapshotStore _snapshotStore;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IDownloadProgressStore _progressStore;
    private readonly IUserPreferenceStore _preferenceStore;
    private readonly ILogger<ArrSyncService> _logger;

    /// <summary>Initializes a new instance of the <see cref="ArrSyncService"/> class.</summary>
    public ArrSyncService(
        ISonarrApiClient sonarr,
        IRadarrApiClient radarr,
        IRequestSnapshotStore snapshotStore,
        INotificationDispatcher dispatcher,
        IDownloadProgressStore progressStore,
        IUserPreferenceStore preferenceStore,
        ILogger<ArrSyncService> logger)
    {
        _sonarr = sonarr;
        _radarr = radarr;
        _snapshotStore = snapshotStore;
        _dispatcher = dispatcher;
        _progressStore = progressStore;
        _preferenceStore = preferenceStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PollAllAsync(CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance!.Configuration;
        var snapshots = await _snapshotStore.GetAllAsync().ConfigureAwait(false);

        // Poll Sonarr instances
        foreach (var instance in config.SonarrInstances.Where(i => i.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PollSonarrAsync(instance, snapshots, cancellationToken).ConfigureAwait(false);
        }

        // Poll Radarr instances
        foreach (var instance in config.RadarrInstances.Where(i => i.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PollRadarrAsync(instance, snapshots, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PollSonarrAsync(
        Configuration.ArrInstanceConfig instance,
        IReadOnlyList<RequestSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        try
        {
            var series = await _sonarr.GetAllSeriesAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
            var queue = await _sonarr.GetQueueAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);

            if (queue?.Records is null)
            {
                return;
            }

            foreach (var item in queue.Records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Find the series for this queue item
                var matchedSeries = item.SeriesId.HasValue
                    ? series.FirstOrDefault(s => s.Id == item.SeriesId.Value)
                    : null;

                // Correlate to all snapshots via TVDb or TMDb
                var matchedSnapshots = FindSnapshotsForSeries(snapshots, matchedSeries);
                if (matchedSnapshots.Count == 0)
                {
                    continue;
                }

                foreach (var snapshot in matchedSnapshots)
                {
                    if (string.IsNullOrWhiteSpace(snapshot.JellyfinUserId))
                    {
                        continue;
                    }

                    var progressKey = $"{instance.Name}:sonarr:{item.DownloadId ?? item.Id.ToString()}:{snapshot.JellyfinUserId}";
                    var currentStatus = NormalizeArrStatus(item);
                    var previousStatus = _progressStore.GetProgress(progressKey);

                    // Reuses the queue item this poll cycle already fetched — no extra
                    // network calls — so a future "check my requests" command can show a
                    // real download percentage instead of just a coarse status string.
                    await UpdateSnapshotProgressAsync(snapshot, instance.Name, currentStatus, item).ConfigureAwait(false);

                    if (string.Equals(currentStatus, previousStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _progressStore.SetProgress(progressKey, currentStatus);

                    var prefs = await _preferenceStore.GetByUserAsync(snapshot.JellyfinUserId).ConfigureAwait(false);
                    var language = NotificationLanguage.Resolve(prefs);
                    var (notifType, title, message) = MapArrStatus(currentStatus, matchedSeries?.Title ?? item.Title, language);
                    if (notifType is null)
                    {
                        continue;
                    }

                    await _dispatcher.DispatchAsync(new NotificationEvent
                    {
                        JellyfinUserId = snapshot.JellyfinUserId,
                        Type = notifType.Value,
                        Title = title,
                        Message = message,
                        MediaTitle = matchedSeries?.Title ?? item.Title,
                        MediaType = "tv",
                        ExternalIds = snapshot.ExternalIds,
                        ThumbnailUrl = snapshot.PosterUrl,
                        ArrInstanceName = instance.Name,
                        PreviousState = previousStatus,
                        NewState = currentStatus,
                        Year = snapshot.Year,
                        ProgressPercent = snapshot.ArrProgress,
                        EtaRaw = snapshot.ArrTimeLeft,
                        Quality = snapshot.ArrQuality,
                        FailureReason = ExtractFailureReason(item)
                    }, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling Sonarr instance {Name}", instance.Name);
        }
    }

    private async Task PollRadarrAsync(
        Configuration.ArrInstanceConfig instance,
        IReadOnlyList<RequestSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        try
        {
            var movies = await _radarr.GetAllMoviesAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
            var queue = await _radarr.GetQueueAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);

            if (queue?.Records is null)
            {
                return;
            }

            foreach (var item in queue.Records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var matchedMovie = item.MovieId.HasValue
                    ? movies.FirstOrDefault(m => m.Id == item.MovieId.Value)
                    : null;

                var matchedSnapshots = FindSnapshotsForMovie(snapshots, matchedMovie);
                if (matchedSnapshots.Count == 0)
                {
                    continue;
                }

                foreach (var snapshot in matchedSnapshots)
                {
                    if (string.IsNullOrWhiteSpace(snapshot.JellyfinUserId))
                    {
                        continue;
                    }

                    var progressKey = $"{instance.Name}:radarr:{item.DownloadId ?? item.Id.ToString()}:{snapshot.JellyfinUserId}";
                    var currentStatus = NormalizeArrStatus(item);
                    var previousStatus = _progressStore.GetProgress(progressKey);

                    await UpdateSnapshotProgressAsync(snapshot, instance.Name, currentStatus, item).ConfigureAwait(false);

                    if (string.Equals(currentStatus, previousStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _progressStore.SetProgress(progressKey, currentStatus);

                    var prefs = await _preferenceStore.GetByUserAsync(snapshot.JellyfinUserId).ConfigureAwait(false);
                    var language = NotificationLanguage.Resolve(prefs);
                    var (notifType, title, message) = MapArrStatus(currentStatus, matchedMovie?.Title ?? item.Title, language);
                    if (notifType is null)
                    {
                        continue;
                    }

                    await _dispatcher.DispatchAsync(new NotificationEvent
                    {
                        JellyfinUserId = snapshot.JellyfinUserId,
                        Type = notifType.Value,
                        Title = title,
                        Message = message,
                        MediaTitle = matchedMovie?.Title ?? item.Title,
                        MediaType = "movie",
                        ExternalIds = snapshot.ExternalIds,
                        ThumbnailUrl = snapshot.PosterUrl,
                        ArrInstanceName = instance.Name,
                        PreviousState = previousStatus,
                        NewState = currentStatus,
                        Year = snapshot.Year,
                        ProgressPercent = snapshot.ArrProgress,
                        EtaRaw = snapshot.ArrTimeLeft,
                        Quality = snapshot.ArrQuality,
                        FailureReason = ExtractFailureReason(item)
                    }, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling Radarr instance {Name}", instance.Name);
        }
    }

    /// <summary>
    /// Persists the current *arr status/progress onto the matched snapshot, so a later
    /// "check my requests" summary can read a real download percentage straight from
    /// storage instead of needing a fresh Sonarr/Radarr call at request time. Only writes
    /// when something actually changed, to keep this cheap on the common case where a
    /// download's percentage hasn't moved since the last poll.
    /// </summary>
    private async Task UpdateSnapshotProgressAsync(RequestSnapshot snapshot, string instanceName, string currentStatus, ArrQueueItem item)
    {
        var progress = ComputeProgressPercent(item);
        var quality = item.Quality?.Quality?.Name;
        var changed = !string.Equals(snapshot.ArrStatus, currentStatus, StringComparison.OrdinalIgnoreCase)
            || snapshot.ArrProgress != progress
            || !string.Equals(snapshot.ArrInstanceName, instanceName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snapshot.ArrTimeLeft, item.Timeleft, StringComparison.Ordinal)
            || !string.Equals(snapshot.ArrQuality, quality, StringComparison.Ordinal);

        if (!changed)
        {
            return;
        }

        snapshot.ArrInstanceName = instanceName;
        snapshot.ArrStatus = currentStatus;
        snapshot.ArrProgress = progress;
        snapshot.ArrTimeLeft = item.Timeleft;
        snapshot.ArrQuality = quality;

        // "downloading" is *arr's own view of an active download — stamp it the first
        // time it's seen (never overwritten) so /status can show when a download
        // actually started even if Seerr's own "Processing" status was missed between
        // polls. ArrLastProgressAt instead refreshes on every change, since it's meant
        // to show how fresh the current percentage is, not when it first began.
        if (string.Equals(currentStatus, "downloading", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.DownloadStartedAt ??= DateTime.UtcNow;
            snapshot.ArrLastProgressAt = DateTime.UtcNow;
        }

        if (string.Equals(currentStatus, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentStatus, "blocklisted", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.FailedAt ??= DateTime.UtcNow;
        }

        await _snapshotStore.UpsertAsync(snapshot).ConfigureAwait(false);
    }

    private static double? ComputeProgressPercent(ArrQueueItem item)
    {
        if (item.Size <= 0)
        {
            return null;
        }

        var downloaded = item.Size - item.Sizeleft;
        var percent = downloaded / item.Size * 100;
        return Math.Clamp(Math.Round(percent, 1), 0, 100);
    }

    /// <summary>Finds all snapshots matching a Sonarr series by TVDb, TMDb or IMDb ID.</summary>
    internal static List<RequestSnapshot> FindSnapshotsForSeries(IReadOnlyList<RequestSnapshot> snapshots, ArrSeries? series)
    {
        if (series is null)
        {
            return new List<RequestSnapshot>();
        }

        return snapshots.Where(s =>
            (series.TvdbId.HasValue && s.ExternalIds?.TvdbId == series.TvdbId.Value.ToString()) ||
            (series.TmdbId.HasValue && s.ExternalIds?.TmdbId == series.TmdbId.Value.ToString()) ||
            (!string.IsNullOrWhiteSpace(series.ImdbId) && s.ExternalIds?.ImdbId == series.ImdbId)).ToList();
    }

    /// <summary>Finds all snapshots matching a Radarr movie by TMDb or IMDb ID.</summary>
    internal static List<RequestSnapshot> FindSnapshotsForMovie(IReadOnlyList<RequestSnapshot> snapshots, ArrMovie? movie)
    {
        if (movie is null)
        {
            return new List<RequestSnapshot>();
        }

        return snapshots.Where(s =>
            (movie.TmdbId.HasValue && s.ExternalIds?.TmdbId == movie.TmdbId.Value.ToString()) ||
            (!string.IsNullOrWhiteSpace(movie.ImdbId) && s.ExternalIds?.ImdbId == movie.ImdbId)).ToList();
    }

    private static string NormalizeArrStatus(ArrQueueItem item)
    {
        if (string.Equals(item.TrackedDownloadStatus, "error", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(item.ErrorMessage))
        {
            return "failed";
        }

        if (string.Equals(item.TrackedDownloadStatus, "warning", StringComparison.OrdinalIgnoreCase)
            || item.StatusMessages?.Count > 0)
        {
            return "warning";
        }

        return item.TrackedDownloadState ?? item.Status;
    }

    /// <summary>
    /// Extracts a human-readable failure/warning reason directly from data this poll
    /// already fetched (no extra call) — the queue's own error message, or its first
    /// status message, whichever is present. Null when there's nothing to show.
    /// </summary>
    private static string? ExtractFailureReason(ArrQueueItem item)
    {
        string? reason;
        if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
        {
            reason = item.ErrorMessage;
        }
        else
        {
            var firstStatusMessage = item.StatusMessages?.FirstOrDefault();
            if (firstStatusMessage is null)
            {
                return null;
            }

            var detail = firstStatusMessage.Messages?.FirstOrDefault();
            reason = !string.IsNullOrWhiteSpace(detail) ? detail : firstStatusMessage.Title;
        }

        return SanitizeFailureReason(reason);
    }

    /// <summary>
    /// Neutralizes Discord/WhatsApp markdown metacharacters in text that came from
    /// *arr, not from this plugin — unlike the fixed strings this plugin writes itself,
    /// this could in principle contain characters that break formatting (or, in the
    /// worst case, unintended emphasis/links) on channels that render it as markdown
    /// with no escaping of their own. Telegram is unaffected: its client already
    /// HTML-escapes the whole message separately, so this only needs to cover the
    /// other two.
    /// </summary>
    private static string? SanitizeFailureReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return reason;
        }

        return reason.Replace("*", string.Empty).Replace("_", string.Empty).Replace("`", string.Empty).Trim();
    }

    /// <summary>Internal (rather than private) purely for test visibility.</summary>
    internal static (NotificationType? Type, string Title, string Message) MapArrStatus(string status, string mediaTitle, string language)
    {
        (string Title, string Message) text;
        NotificationType? type;

        switch (status.ToLowerInvariant())
        {
            case "downloading":
                type = NotificationType.DownloadStarted;
                text = NotificationText.ArrDownloadStarted(mediaTitle, language);
                break;

            // "importpending"/"imported"/"completed" fall through to default (no
            // notification) — MediaAvailable (Seerr-driven) already covers "your
            // content is ready," so this *arr-level import signal is redundant.
            case "failed":
            case "error":
            case "blocklisted":
                type = NotificationType.DownloadFailed;
                text = NotificationText.ArrDownloadFailed(mediaTitle, language);
                break;
            case "warning":
            case "stalled":
                type = NotificationType.DownloadWarning;
                text = NotificationText.ArrDownloadWarning(mediaTitle, language);
                break;
            default:
                return (null, string.Empty, string.Empty);
        }

        return (type, text.Title, text.Message);
    }
}
