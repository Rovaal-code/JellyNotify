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
/// <para>
/// This is the only recurring *arr check left: availability and grab arrive instantly on the
/// *arr webhook, but download progress has no webhook equivalent. It is therefore written to be
/// cheap enough to run every minute — see <see cref="PollSonarrAsync"/> for why the queue is
/// fetched before anything else.
/// </para>
/// </summary>
public sealed class ArrSyncService : IArrSyncService
{
    /// <summary>Stage token for a queue item that exists but hasn't transferred a byte yet. Notifies nothing.</summary>
    internal const string PendingStage = "downloading:pending";

    /// <summary>Stage token for a download that has really begun transferring.</summary>
    internal const string StartedStage = "downloading:started";

    /// <summary>Stage token for a download past the configured progress threshold.</summary>
    internal const string HalfStage = "downloading:half";

    /// <summary>Stage token for *arr's own warning/stalled report.</summary>
    internal const string WarningStage = "warning";

    /// <summary>Stage token for a download the watchdog has given up on. Terminal unless it moves again.</summary>
    internal const string StalledStage = "stalled:notified";

    /// <summary>
    /// How many consecutive polls must report a warning before one is sent. A torrent with no
    /// seeds flaps in and out of *arr's warning state as peers come and go, and the old
    /// "notify whenever the stage changed" rule turned every flap into another warning. Two
    /// means a warning has to survive a full extra cycle to be believed.
    /// </summary>
    internal const int WarningStreakBeforeNotifying = 2;

    /// <summary>
    /// Statuses meaning "*arr already has the bytes and is moving the file". Excluded from the
    /// stall watchdog: their percentage is 100 and will never move again, so measuring movement
    /// against them would report every slow import as a stalled download. A genuinely stuck
    /// import is an admin-side problem, and MediaAvailable is what tells the requester it landed.
    /// </summary>
    private static readonly string[] ImportStatuses =
        ["importpending", "importblocked", "importing", "imported", "completed"];

    private static readonly string[] FailureStatuses = ["failed", "error", "blocklisted"];

    private static readonly string[] WarningStatuses = ["warning", "stalled"];

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
        var context = new PollContext(
            Math.Max(1, config.NotificationSettings.DownloadingNotifyThresholdPercent),
            Math.Max(0, config.NotificationSettings.StalledDownloadHours),
            DateTime.UtcNow);

        await _progressStore.LoadAsync().ConfigureAwait(false);
        var snapshots = await _snapshotStore.GetAllAsync().ConfigureAwait(false);

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only instances that answered successfully make their keys eligible for eviction. A
        // Sonarr that was briefly unreachable must not have its tracked downloads forgotten,
        // or the next successful poll would re-announce all of them from scratch.
        var evictablePrefixes = new List<string>();

        foreach (var instance in config.SonarrInstances.Where(i => i.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keys = await PollSonarrAsync(instance, snapshots, context, cancellationToken).ConfigureAwait(false);
            if (keys is null)
            {
                continue;
            }

            seenKeys.UnionWith(keys);
            evictablePrefixes.Add(KeyPrefix(instance.Name, "sonarr"));
        }

        foreach (var instance in config.RadarrInstances.Where(i => i.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keys = await PollRadarrAsync(instance, snapshots, context, cancellationToken).ConfigureAwait(false);
            if (keys is null)
            {
                continue;
            }

            seenKeys.UnionWith(keys);
            evictablePrefixes.Add(KeyPrefix(instance.Name, "radarr"));
        }

        EvictFinishedDownloads(seenKeys, evictablePrefixes);
        await _progressStore.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>Builds the progress-store key prefix identifying one instance's downloads.</summary>
    internal static string KeyPrefix(string instanceName, string kind) => $"{instanceName}:{kind}:";

    /// <summary>
    /// Drops tracked state for downloads that have left the queue, which is what keeps the
    /// store from growing forever. Scoped to instances that polled successfully this cycle:
    /// keys belonging to an instance that failed, is disabled, or was deleted are left alone
    /// rather than risk forgetting a live download. (Deleting an instance therefore orphans a
    /// handful of entries permanently — a few hundred bytes, against re-notifying every
    /// download of an instance that was merely toggled off for an afternoon.)
    /// </summary>
    private void EvictFinishedDownloads(HashSet<string> seenKeys, List<string> evictablePrefixes)
    {
        if (evictablePrefixes.Count == 0)
        {
            return;
        }

        var evicted = 0;
        foreach (var key in _progressStore.GetAllKeys())
        {
            if (seenKeys.Contains(key))
            {
                continue;
            }

            if (!evictablePrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _progressStore.Remove(key);
            evicted++;
        }

        if (evicted > 0)
        {
            _logger.LogDebug("JellyNotify: stopped tracking {Count} download(s) that left the *arr queue", evicted);
        }
    }

    /// <summary>
    /// Polls one Sonarr instance's queue.
    /// <para>
    /// The queue is fetched <em>first</em> and an empty one returns immediately, because the
    /// alternative — the full <c>/api/v3/series</c> library dump this used to open with — costs
    /// megabytes of JSON per cycle on a large library and the queue is empty the vast majority
    /// of the time. Titles for the handful of items actually in the queue are then resolved
    /// individually and memoized, which is what makes a 60-second interval cheaper than the old
    /// 300-second one.
    /// </para>
    /// </summary>
    /// <returns>The progress-store keys seen this cycle, or null if the instance could not be polled.</returns>
    private async Task<HashSet<string>?> PollSonarrAsync(
        Configuration.ArrInstanceConfig instance,
        IReadOnlyList<RequestSnapshot> snapshots,
        PollContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var queue = await _sonarr.GetQueueAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
            if (queue?.Records is null)
            {
                return null;
            }

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (queue.Records.Count == 0)
            {
                return seenKeys;
            }

            var seriesCache = new Dictionary<int, ArrSeries?>();

            foreach (var item in queue.Records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ArrSeries? matchedSeries = null;
                if (item.SeriesId.HasValue)
                {
                    if (!seriesCache.TryGetValue(item.SeriesId.Value, out matchedSeries))
                    {
                        matchedSeries = await _sonarr.GetSeriesByIdAsync(
                            instance.ServerUrl, instance.ApiKey, item.SeriesId.Value, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
                        seriesCache[item.SeriesId.Value] = matchedSeries;
                    }
                }

                var matchedSnapshots = FindSnapshotsForSeries(snapshots, matchedSeries);
                if (matchedSnapshots.Count == 0)
                {
                    continue;
                }

                await HandleQueueItemAsync(
                    item,
                    matchedSnapshots,
                    matchedSeries?.Title ?? item.Title,
                    "tv",
                    instance.Name,
                    "sonarr",
                    context,
                    seenKeys,
                    cancellationToken).ConfigureAwait(false);
            }

            return seenKeys;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling Sonarr instance {Name}", instance.Name);
            return null;
        }
    }

    /// <summary>Polls one Radarr instance's queue. Same shape and reasoning as <see cref="PollSonarrAsync"/>.</summary>
    /// <returns>The progress-store keys seen this cycle, or null if the instance could not be polled.</returns>
    private async Task<HashSet<string>?> PollRadarrAsync(
        Configuration.ArrInstanceConfig instance,
        IReadOnlyList<RequestSnapshot> snapshots,
        PollContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var queue = await _radarr.GetQueueAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
            if (queue?.Records is null)
            {
                return null;
            }

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (queue.Records.Count == 0)
            {
                return seenKeys;
            }

            var movieCache = new Dictionary<int, ArrMovie?>();

            foreach (var item in queue.Records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ArrMovie? matchedMovie = null;
                if (item.MovieId.HasValue)
                {
                    if (!movieCache.TryGetValue(item.MovieId.Value, out matchedMovie))
                    {
                        matchedMovie = await _radarr.GetMovieByIdAsync(
                            instance.ServerUrl, instance.ApiKey, item.MovieId.Value, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
                        movieCache[item.MovieId.Value] = matchedMovie;
                    }
                }

                var matchedSnapshots = FindSnapshotsForMovie(snapshots, matchedMovie);
                if (matchedSnapshots.Count == 0)
                {
                    continue;
                }

                await HandleQueueItemAsync(
                    item,
                    matchedSnapshots,
                    matchedMovie?.Title ?? item.Title,
                    "movie",
                    instance.Name,
                    "radarr",
                    context,
                    seenKeys,
                    cancellationToken).ConfigureAwait(false);
            }

            return seenKeys;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling Radarr instance {Name}", instance.Name);
            return null;
        }
    }

    /// <summary>
    /// Runs one queue item against every snapshot it belongs to: records the key as seen (so
    /// eviction spares it), advances the stored state, and dispatches whatever notification the
    /// transition produced. Shared by both pollers — only title and media-type resolution
    /// differ between Sonarr and Radarr.
    /// </summary>
    private async Task HandleQueueItemAsync(
        ArrQueueItem item,
        IReadOnlyList<RequestSnapshot> matchedSnapshots,
        string? mediaTitle,
        string mediaType,
        string instanceName,
        string kind,
        PollContext context,
        HashSet<string> seenKeys,
        CancellationToken cancellationToken)
    {
        var currentStatus = NormalizeArrStatus(item);
        var downloadId = item.DownloadId ?? item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (var snapshot in matchedSnapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.JellyfinUserId))
            {
                continue;
            }

            var progressKey = $"{KeyPrefix(instanceName, kind)}{downloadId}:{snapshot.JellyfinUserId}";
            seenKeys.Add(progressKey);

            var previous = _progressStore.Get(progressKey);
            var transition = ComputeTransition(previous, currentStatus, item, context);

            await UpdateSnapshotProgressAsync(snapshot, instanceName, currentStatus, item).ConfigureAwait(false);

            _progressStore.Set(progressKey, transition.State);

            if (transition.Notify is null)
            {
                continue;
            }

            var prefs = await _preferenceStore.GetByUserAsync(snapshot.JellyfinUserId).ConfigureAwait(false);
            var language = NotificationLanguage.Resolve(prefs);
            var (title, message) = DescribeNotification(
                transition.Notify.Value, transition.State.Stage, mediaTitle ?? item.Title ?? string.Empty, context.StalledHours, language);

            await _dispatcher.DispatchAsync(new NotificationEvent
            {
                JellyfinUserId = snapshot.JellyfinUserId,
                Type = transition.Notify.Value,
                Title = title,
                Message = message,
                MediaTitle = mediaTitle ?? item.Title,
                MediaType = mediaType,
                ExternalIds = snapshot.ExternalIds,
                ThumbnailUrl = snapshot.PosterUrl,
                ArrInstanceName = instanceName,
                PreviousState = previous?.Stage,
                NewState = transition.State.Stage,
                Year = snapshot.Year,
                ProgressPercent = snapshot.ArrProgress,
                EtaRaw = snapshot.ArrTimeLeft,
                Quality = snapshot.ArrQuality,
                FailureReason = ExtractFailureReason(item)
            }, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Advances one download's stored state by one observation and says what, if anything, to
    /// notify. A transition function rather than a pure "what stage is this item in" mapping,
    /// because every rule here needs memory the current queue item doesn't carry:
    /// <list type="bullet">
    /// <item>The sequence is strictly ordered — "Downloading" is only ever sent after
    /// "Download started" went out for the same download, so nobody is told a download is 88%
    /// complete without first being told it began.</item>
    /// <item>A first sighting already past the threshold means the whole transfer fit inside one
    /// poll window. Both stages are marked spent and <em>nothing</em> is sent: the *arr
    /// webhook's "available" lands seconds later and is the honest signal.</item>
    /// <item>A warning must survive <see cref="WarningStreakBeforeNotifying"/> consecutive polls
    /// and is sent once, which is what stops a seedless torrent's warning/recovery flapping from
    /// notifying on every cycle.</item>
    /// <item>A download whose percentage hasn't moved in <c>StalledHours</c> is reported stalled
    /// once and then goes permanently quiet, so *arr's own silence about seedless torrents no
    /// longer leaves the requester watching "Downloading 3%" forever.</item>
    /// </list>
    /// Internal for test visibility.
    /// </summary>
    internal static DownloadTransition ComputeTransition(
        DownloadProgressState? previous,
        string normalizedStatus,
        ArrQueueItem item,
        PollContext context)
    {
        var state = previous?.Clone() ?? new DownloadProgressState();
        var progress = ComputeProgressPercent(item);

        // Movement tracking comes first: everything below reads LastMovementAtUtc, and a
        // download that resumes must get a fresh watchdog window rather than stay condemned.
        var moved = previous is null || progress != state.LastProgress;
        if (moved)
        {
            state.LastProgress = progress;
            state.LastMovementAtUtc = context.NowUtc;
            state.StalledNotified = false;
        }

        if (FailureStatuses.Contains(normalizedStatus, StringComparer.OrdinalIgnoreCase))
        {
            state.Stage = normalizedStatus;
            state.WarningStreak = 0;
            if (state.FailedNotified)
            {
                return new DownloadTransition(state, null);
            }

            state.FailedNotified = true;
            return new DownloadTransition(state, NotificationType.DownloadFailed);
        }

        var isImporting = ImportStatuses.Contains(normalizedStatus, StringComparer.OrdinalIgnoreCase);

        // The watchdog is checked before *arr's own warning flag, because the case it exists
        // for — a torrent with no seeds — is frequently reported as a perfectly healthy
        // download that simply never advances.
        if (!isImporting && context.StalledHours > 0 && !state.StalledNotified
            && state.LastMovementAtUtc is DateTime lastMovement
            && context.NowUtc - lastMovement >= TimeSpan.FromHours(context.StalledHours))
        {
            state.StalledNotified = true;
            state.WarningStreak = 0;
            state.Stage = StalledStage;
            return new DownloadTransition(state, NotificationType.DownloadWarning);
        }

        if (state.StalledNotified)
        {
            // Reported once and still frozen: stay silent no matter what *arr says about it.
            state.Stage = StalledStage;
            return new DownloadTransition(state, null);
        }

        if (WarningStatuses.Contains(normalizedStatus, StringComparer.OrdinalIgnoreCase))
        {
            state.WarningStreak++;
            state.Stage = WarningStage;

            if (state.WarningStreak < WarningStreakBeforeNotifying || state.WarningNotified)
            {
                return new DownloadTransition(state, null);
            }

            state.WarningNotified = true;
            return new DownloadTransition(state, NotificationType.DownloadWarning);
        }

        // *arr is no longer complaining, so the streak restarts. This single line is what
        // breaks the flap loop: an alternating warning/healthy download never reaches two
        // consecutive warnings and so never notifies.
        state.WarningStreak = 0;

        if (string.Equals(normalizedStatus, "downloading", StringComparison.OrdinalIgnoreCase))
        {
            if (progress is not > 0)
            {
                // In the queue, nothing transferred yet. Notifying here is what the *arr Grab
                // webhook used to do, and it arrived with no progress and no ETA to show.
                state.Stage = PendingStage;
                return new DownloadTransition(state, null);
            }

            if (!state.StartedNotified)
            {
                state.StartedNotified = true;

                if (progress >= context.DownloadingThreshold)
                {
                    state.HalfNotified = true;
                    state.Stage = HalfStage;
                    return new DownloadTransition(state, null);
                }

                state.Stage = StartedStage;
                return new DownloadTransition(state, NotificationType.DownloadStarted);
            }

            if (progress >= context.DownloadingThreshold && !state.HalfNotified)
            {
                state.HalfNotified = true;
                state.Stage = HalfStage;
                return new DownloadTransition(state, NotificationType.DownloadProgress);
            }

            state.Stage = state.HalfNotified ? HalfStage : StartedStage;
            return new DownloadTransition(state, null);
        }

        // Import states and anything unrecognised notify nothing: MediaAvailable already tells
        // the requester their content is ready, which makes an *arr-level import ping redundant.
        state.Stage = normalizedStatus;
        return new DownloadTransition(state, null);
    }

    /// <summary>
    /// Renders the user-facing text for a notification the transition decided to send. The
    /// stall watchdog and *arr's own warning share <see cref="NotificationType.DownloadWarning"/>,
    /// so the stage is what tells them apart. Internal for test visibility.
    /// </summary>
    internal static (string Title, string Message) DescribeNotification(
        NotificationType type, string stage, string mediaTitle, int stalledHours, string language) => type switch
    {
        NotificationType.DownloadStarted => NotificationText.ArrDownloadStarted(mediaTitle, language),
        NotificationType.DownloadProgress => NotificationText.ArrDownloading(mediaTitle, language),
        NotificationType.DownloadFailed => NotificationText.ArrDownloadFailed(mediaTitle, language),
        NotificationType.DownloadWarning when string.Equals(stage, StalledStage, StringComparison.OrdinalIgnoreCase) =>
            NotificationText.ArrDownloadStalled(mediaTitle, stalledHours, language),
        NotificationType.DownloadWarning => NotificationText.ArrDownloadWarning(mediaTitle, language),
        _ => (string.Empty, string.Empty)
    };

    /// <summary>
    /// Collapses a queue item's several status fields into one token. Internal for test
    /// visibility.
    /// </summary>
    internal static string NormalizeArrStatus(ArrQueueItem item)
    {
        if (string.Equals(item.TrackedDownloadStatus, "error", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(item.ErrorMessage))
        {
            return "failed";
        }

        // Status messages only mean trouble when *arr hasn't explicitly said the download is
        // fine: it also attaches purely informational messages (sample files, episodes not
        // imported) to healthy items, and treating those as warnings produced notifications
        // about downloads that were doing nothing wrong.
        if (string.Equals(item.TrackedDownloadStatus, "warning", StringComparison.OrdinalIgnoreCase)
            || (item.StatusMessages?.Count > 0
                && !string.Equals(item.TrackedDownloadStatus, "ok", StringComparison.OrdinalIgnoreCase)))
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
}

/// <summary>
/// The settings one poll cycle runs under. Bundled and passed down rather than threaded through
/// as loose parameters, and carrying an explicit "now" so the stall watchdog is testable without
/// waiting hours.
/// </summary>
/// <param name="DownloadingThreshold">Percentage at or above which the mid-download "Downloading" ping is sent.</param>
/// <param name="StalledHours">Hours without movement before a download is reported stalled; 0 disables the check.</param>
/// <param name="NowUtc">The cycle's reference time.</param>
public sealed record PollContext(int DownloadingThreshold, int StalledHours, DateTime NowUtc);

/// <summary>The outcome of advancing one download's state by a single observation.</summary>
/// <param name="State">The state to store, whether or not anything is notified.</param>
/// <param name="Notify">The notification to send, or null to stay silent.</param>
public sealed record DownloadTransition(DownloadProgressState State, NotificationType? Notify);
