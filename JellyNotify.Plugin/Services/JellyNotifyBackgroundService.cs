using Jellyfin.Plugin.JellyNotify.Store;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Long-running background service. Instant delivery now runs through webhooks JellyNotify
/// creates directly in Seerr/Sonarr/Radarr (see <c>AdminController</c>'s auto-configure
/// actions) — this service is no longer the primary way notification-worthy changes are
/// detected. What's left, deliberately:
/// - One-time Seerr catch-up at startup only (see <see cref="ExecuteAsync"/>), to pick up
///   anything that changed while Jellyfin was off or before the webhook was set up. Safe to
///   run every restart: the baseline flag it relies on lives on disk and only actually
///   suppresses anything on a genuinely fresh install.
/// - A recurring, *arr-only* queue check for download progress/stalled/failed state
///   (<see cref="IArrSyncService.PollAllAsync"/>) — Sonarr/Radarr have no webhook event for
///   "still downloading", only for grab/import, so this has no webhook equivalent and stays
///   on a timer.
/// - Notification purge.
/// </summary>
public sealed class JellyNotifyBackgroundService : BackgroundService
{
    private readonly IMediaRequestService _mediaRequestService;
    private readonly IArrSyncService _arrSyncService;
    private readonly INotificationStore _notificationStore;
    private readonly ILogger<JellyNotifyBackgroundService> _logger;

    /// <summary>Initializes a new instance of the <see cref="JellyNotifyBackgroundService"/> class.</summary>
    public JellyNotifyBackgroundService(
        IMediaRequestService mediaRequestService,
        IArrSyncService arrSyncService,
        INotificationStore notificationStore,
        ILogger<JellyNotifyBackgroundService> logger)
    {
        _mediaRequestService = mediaRequestService;
        _arrSyncService = arrSyncService;
        _notificationStore = notificationStore;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JellyNotify background service starting");

        // Initial short delay to allow Jellyfin to fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);

        // One-time Seerr catch-up — not part of the recurring loop below. From here on,
        // request status changes arrive instantly via the Seerr webhook (which shares the
        // exact same processing path, see MediaRequestService.ProcessSingleRequestAsync);
        // this single run only exists to reconcile whatever happened while Jellyfin was down
        // or before the webhook was configured. Failure here shouldn't prevent the recurring
        // *arr progress loop from starting.
        try
        {
            await _mediaRequestService.PollAndProcessAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyNotify: one-time Seerr catch-up at startup failed");
        }

        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleSuccess = false;

            // Create a timeout token for this specific cycle (3 minutes) to prevent hanging calls
            using (var cycleTimeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, cycleTimeoutCts.Token))
            {
                try
                {
                    await RunCycleAsync(linkedCts.Token).ConfigureAwait(false);
                    cycleSuccess = true;
                    consecutiveFailures = 0;
                    RecordSyncState(null);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    RecordSyncState(ex.Message);
                    _logger.LogError(ex, "Unhandled error in JellyNotify background cycle (Consecutive failures: {Count})", consecutiveFailures);
                }
            }

            // Calculate wait interval with basic backoff
            var baseInterval = GetPollingIntervalSeconds();
            var intervalSeconds = baseInterval;

            if (!cycleSuccess && consecutiveFailures > 0)
            {
                // Exponential backoff: base * 2^failures, capped at 15 minutes (900 seconds)
                var backoffFactor = Math.Pow(2, Math.Min(consecutiveFailures, 4));
                intervalSeconds = (int)Math.Min(baseInterval * backoffFactor, 900);
                _logger.LogWarning("JellyNotify: cycle failed. Applying backoff. Next poll in {Seconds}s", intervalSeconds);
            }
            else
            {
                _logger.LogDebug("JellyNotify: waiting {Seconds}s until next poll", intervalSeconds);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("JellyNotify background service stopped");
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        // *arr download progress/stalled/failed state has no webhook equivalent in
        // Sonarr/Radarr (only grab/import fire an event) — this is the only remaining
        // recurring check. Availability itself no longer comes from here; see the arr
        // webhook (Download/Upgrade events) and AutoConfigureArrWebhookAsync.
        await _arrSyncService.PollAllAsync(cancellationToken).ConfigureAwait(false);

        // Purge old notifications when age-based retention is enabled.
        var retentionDays = Plugin.Instance?.Configuration.NotificationRetentionDays ?? 30;
        if (retentionDays > 0)
        {
            await _notificationStore.PurgeOldAsync(retentionDays).ConfigureAwait(false);
        }
    }

    private static void RecordSyncState(string? error)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return;
        }

        var config = plugin.Configuration;
        if (string.IsNullOrWhiteSpace(error))
        {
            config.LastSuccessfulSyncUtc = DateTime.UtcNow;
            config.LastSyncError = null;
        }
        else
        {
            config.LastSyncError = error.Length > 500 ? error[..500] : error;
        }

        plugin.SavePluginConfiguration(config);
    }

    private static int GetPollingIntervalSeconds()
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return 300;
        }

        // Seerr no longer contributes to this — it has no recurring cycle anymore (see the
        // class doc comment). This interval only governs how often the *arr queue is
        // checked for download progress, so it's driven purely by enabled Sonarr/Radarr
        // instances. Floor of 30s matches the shortest option offered in the admin UI's
        // dropdown, so picking that value there is never silently overridden here.
        var intervals = config.SonarrInstances
            .Where(i => i.Enabled)
            .Select(i => i.PollingIntervalSeconds)
            .Concat(config.RadarrInstances.Where(i => i.Enabled).Select(i => i.PollingIntervalSeconds))
            .Where(i => i > 0)
            .ToList();

        return intervals.Count > 0 ? Math.Max(30, intervals.Min()) : 300;
    }
}
