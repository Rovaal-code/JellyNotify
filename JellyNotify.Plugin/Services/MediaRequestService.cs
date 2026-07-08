using Jellyfin.Plugin.JellyNotify.Models;
using Jellyfin.Plugin.JellyNotify.Services;
using Jellyfin.Plugin.JellyNotify.Store;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Polls Overseerr/Jellyseerr for all requests, compares their state against stored snapshots,
/// and dispatches notifications for any detected changes.
/// User identity is resolved by mapping Seerr users to Jellyfin users via the JellyfinUserId
/// field on the Seerr user object — if no mapping exists, no notification is sent.
/// </summary>
public sealed class MediaRequestService : IMediaRequestService
{
    // Caps how many Seerr movie/tv detail lookups (poster + title-fallback + year)
    // a single poll cycle will perform. Without this cap, a server with a large
    // backlog of snapshots missing this data (e.g. right after upgrading to the
    // version that added it) would fire one sequential HTTP call per snapshot in
    // the very first cycle — with hundreds of requests this can run long enough
    // to blow through JellyNotifyBackgroundService's 3-minute cycle timeout,
    // aborting the cycle before later snapshots are even looked at and missing
    // their state transitions entirely. Capping this means backfill instead
    // trickles in gradually over several cycles, and every cycle stays fast.
    private const int MaxDetailFetchesPerCycle = 15;

    private readonly ISeerrApiClient _seerr;
    private readonly IRequestSnapshotStore _snapshotStore;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IUserPreferenceStore _preferenceStore;
    private readonly IArrMediaInfoLookup _arrMediaInfo;
    private readonly ILogger<MediaRequestService> _logger;

    private int _detailFetchesThisCycle;

    /// <summary>Initializes a new instance of the <see cref="MediaRequestService"/> class.</summary>
    public MediaRequestService(
        ISeerrApiClient seerr,
        IRequestSnapshotStore snapshotStore,
        INotificationDispatcher dispatcher,
        IUserPreferenceStore preferenceStore,
        IArrMediaInfoLookup arrMediaInfo,
        ILogger<MediaRequestService> logger)
    {
        _seerr = seerr;
        _snapshotStore = snapshotStore;
        _dispatcher = dispatcher;
        _preferenceStore = preferenceStore;
        _arrMediaInfo = arrMediaInfo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PollAndProcessAsync(CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance!.Configuration;
        if (!config.SeerrSettings.Enabled)
        {
            return;
        }

        _logger.LogDebug("Polling Seerr for request updates...");
        _detailFetchesThisCycle = 0;

        IReadOnlyList<SeerrRequest> requests;
        try
        {
            requests = await _seerr.GetAllRequestsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to poll Seerr requests");
            return;
        }

        var isBaseline = !await _snapshotStore.HasBaselineAsync().ConfigureAwait(false);

        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessRequestAsync(request, isBaseline, cancellationToken).ConfigureAwait(false);
        }

        if (isBaseline)
        {
            await _snapshotStore.SetBaselineCompleteAsync().ConfigureAwait(false);
            _logger.LogInformation("Seerr baseline snapshot complete. {Count} requests indexed.", requests.Count);
        }
    }

    /// <inheritdoc />
    public async Task ProcessSingleRequestAsync(int seerrRequestId, CancellationToken cancellationToken = default)
    {
        var config = Plugin.Instance!.Configuration;
        if (!config.SeerrSettings.Enabled)
        {
            return;
        }

        SeerrRequest? request;
        try
        {
            request = await _seerr.GetRequestByIdAsync(seerrRequestId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Seerr request {RequestId} for webhook-triggered processing", seerrRequestId);
            return;
        }

        if (request is null)
        {
            _logger.LogDebug("Seerr request {RequestId} not found — skipping webhook-triggered processing", seerrRequestId);
            return;
        }

        var isBaseline = !await _snapshotStore.HasBaselineAsync().ConfigureAwait(false);
        await ProcessRequestAsync(request, isBaseline, cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessRequestAsync(SeerrRequest request, bool isBaseline, CancellationToken cancellationToken)
    {
        // Resolve the Jellyfin user ID from the Seerr user
        var jellyfinUserId = request.RequestedBy?.JellyfinUserId;
        if (string.IsNullOrWhiteSpace(jellyfinUserId))
        {
            // Try to fetch user details if not embedded in the request
            if (request.RequestedBy?.Id > 0)
            {
                var user = await _seerr.GetUserByIdAsync(request.RequestedBy.Id, cancellationToken).ConfigureAwait(false);
                jellyfinUserId = user?.JellyfinUserId;
            }
        }

        if (string.IsNullOrWhiteSpace(jellyfinUserId))
        {
            // Cannot safely associate this request with a Jellyfin user — skip notification
            _logger.LogDebug("Seerr request {RequestId} has no linked Jellyfin user — skipping", request.Id);
            return;
        }

        var currentStatus = MapStatus(request.Status);
        var currentMediaStatus = MapMediaStatus(request.Media?.Status ?? 0);
        var combinedStatus = $"req:{currentStatus}|media:{currentMediaStatus}";

        var existing = await _snapshotStore.GetBySeerrRequestIdAsync(request.Id).ConfigureAwait(false);

        // Extract real media title — prefer MediaInfo title/name, fall back to type label
        var mediaTitle = ExtractMediaTitle(request);

        var externalIds = new ExternalIds
        {
            TmdbId = (request.Media?.TmdbId ?? request.TmdbId)?.ToString(),
            TvdbId = (request.Media?.TvdbId ?? request.TvdbId)?.ToString(),
            ImdbId = FirstNonEmpty(request.Media?.ImdbId, request.ImdbId)
        };

        if (existing is null)
        {
            // First time seeing this request — create baseline snapshot. One detail
            // fetch (rate-limited, see MaxDetailFetchesPerCycle) covers the poster, a
            // real title when the request list didn't have one, and the release year —
            // every later notification for it, including *arr-driven ones that never
            // talk to Seerr directly, reuses whatever got cached here.
            var isGeneric = IsGenericFallbackTitle(mediaTitle, request.Type);
            var details = await ResolveExternalMediaDetailsAsync(request, cancellationToken).ConfigureAwait(false);
            var resolvedTitle = isGeneric && !string.IsNullOrWhiteSpace(details?.DisplayTitle) ? details!.DisplayTitle! : mediaTitle;

            var snapshot = new RequestSnapshot
            {
                SeerrRequestId = request.Id,
                JellyfinUserId = jellyfinUserId,
                SeerrUserId = request.RequestedBy?.Id.ToString(),
                MediaType = request.Type,
                MediaTitle = resolvedTitle,
                Status = combinedStatus,
                ExternalIds = externalIds,
                IsBaseline = true,
                CreatedAt = DateTime.UtcNow,
                LastChecked = DateTime.UtcNow,
                PosterUrl = PosterUrlBuilder.Build(details?.PosterPath),
                Year = details?.Year
            };
            StampStatusTimestamps(snapshot, request);

            await _snapshotStore.UpsertAsync(snapshot).ConfigureAwait(false);

            // On first poll (baseline), only generate RequestCreated if NOT in baseline phase
            if (!isBaseline)
            {
                await DispatchNotificationAsync(
                    NotificationType.RequestCreated,
                    NotificationText.RequestCreated,
                    resolvedTitle,
                    jellyfinUserId,
                    request,
                    externalIds,
                    snapshot.PosterUrl,
                    snapshot.Year,
                    null,
                    currentStatus,
                    cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        // Update snapshot with latest media title if we now have a real one
        var prevStatus = existing.Status;
        existing.Status = combinedStatus;
        existing.LastChecked = DateTime.UtcNow;
        existing.IsBaseline = false;
        existing.JellyfinUserId = jellyfinUserId;

        // Prefer a title extracted directly from this poll's own request payload
        // (free, no network call) — only reach for the fetched detail below when
        // that's still generic.
        if (!IsGenericFallbackTitle(mediaTitle, request.Type))
        {
            existing.MediaTitle = mediaTitle;
        }

        // Backfill poster and/or a real title for snapshots still missing either —
        // created before this feature existed, or where Seerr's request list never
        // carried a resolvable title. Rate-limited per cycle (see
        // MaxDetailFetchesPerCycle), so this trickles in over several cycles for a
        // large backlog instead of ever blocking a whole poll cycle.
        var stillNeedsPoster = string.IsNullOrWhiteSpace(existing.PosterUrl);
        var stillNeedsTitle = IsGenericFallbackTitle(existing.MediaTitle, request.Type);
        if (stillNeedsPoster || stillNeedsTitle)
        {
            var details = await ResolveExternalMediaDetailsAsync(request, cancellationToken).ConfigureAwait(false);
            if (details is not null)
            {
                if (stillNeedsPoster)
                {
                    existing.PosterUrl = PosterUrlBuilder.Build(details.PosterPath);
                }

                if (stillNeedsTitle && !string.IsNullOrWhiteSpace(details.DisplayTitle))
                {
                    existing.MediaTitle = details.DisplayTitle;
                }

                existing.Year ??= details.Year;
            }
        }

        StampStatusTimestamps(existing, request);

        await _snapshotStore.UpsertAsync(existing).ConfigureAwait(false);

        // If status didn't change, nothing to notify
        if (string.Equals(prevStatus, combinedStatus, StringComparison.Ordinal))
        {
            return;
        }

        // Skip notifications if no Jellyfin user can be resolved
        if (string.IsNullOrWhiteSpace(existing.JellyfinUserId))
        {
            _logger.LogDebug("Seerr request {RequestId} state changed but has no Jellyfin user — skipping notification", request.Id);
            return;
        }

        var targetUserId = existing.JellyfinUserId;
        var displayTitle = existing.MediaTitle;

        // Generate appropriate notification based on new state
        if (request.Status == (int)SeerrRequestStatus.Approved && prevStatus.Contains("req:Pending", StringComparison.Ordinal))
        {
            await DispatchNotificationAsync(
                NotificationType.RequestApproved,
                NotificationText.RequestApproved,
                displayTitle,
                targetUserId, request, externalIds, existing.PosterUrl, existing.Year, "Pending", "Approved", cancellationToken).ConfigureAwait(false);
        }
        else if (request.Status == (int)SeerrRequestStatus.Declined)
        {
            await DispatchNotificationAsync(
                NotificationType.RequestDeclined,
                NotificationText.RequestDeclined,
                displayTitle,
                targetUserId, request, externalIds, existing.PosterUrl, existing.Year, prevStatus, "Declined", cancellationToken).ConfigureAwait(false);
        }
        else if (request.Status == (int)SeerrRequestStatus.Failed)
        {
            await DispatchNotificationAsync(
                NotificationType.RequestFailed,
                NotificationText.RequestFailed,
                displayTitle,
                targetUserId, request, externalIds, existing.PosterUrl, existing.Year, prevStatus, "Failed", cancellationToken).ConfigureAwait(false);
        }
        else if (request.Media?.Status == (int)SeerrMediaStatus.Available
                 && !prevStatus.Contains($"media:{MapMediaStatus((int)SeerrMediaStatus.Available)}", StringComparison.Ordinal))
        {
            // Seerr's status carries no quality/audio/subtitle detail, so look the imported
            // file up in *arr to enrich the card (the webhook path already has it inline).
            var mediaInfo = await _arrMediaInfo.LookupAsync(request.Type, externalIds, cancellationToken).ConfigureAwait(false);
            await DispatchNotificationAsync(
                NotificationType.MediaAvailable,
                NotificationText.MediaAvailable,
                displayTitle,
                targetUserId, request, externalIds, existing.PosterUrl, existing.Year, prevStatus, "Available", cancellationToken, mediaInfo).ConfigureAwait(false);
        }
        else if (request.Media?.Status == (int)SeerrMediaStatus.PartiallyAvailable
                 && !prevStatus.Contains($"media:{MapMediaStatus((int)SeerrMediaStatus.PartiallyAvailable)}", StringComparison.Ordinal))
        {
            var mediaInfo = await _arrMediaInfo.LookupAsync(request.Type, externalIds, cancellationToken).ConfigureAwait(false);
            await DispatchNotificationAsync(
                NotificationType.MediaPartiallyAvailable,
                NotificationText.MediaPartiallyAvailable,
                displayTitle,
                targetUserId, request, externalIds, existing.PosterUrl, existing.Year, prevStatus, "PartiallyAvailable", cancellationToken, mediaInfo).ConfigureAwait(false);
        }
        else if ((request.Media?.Status == (int)SeerrMediaStatus.Blocklisted
                  || request.Media?.Status == (int)SeerrMediaStatus.Deleted)
                 && !prevStatus.Contains($"media:{currentMediaStatus}", StringComparison.Ordinal))
        {
            await DispatchNotificationAsync(
                NotificationType.IssueWarning,
                NotificationText.ProblemDetected,
                displayTitle,
                targetUserId, request, externalIds, existing.PosterUrl, existing.Year, prevStatus, currentMediaStatus, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchNotificationAsync(
        NotificationType type,
        Func<string, string, (string Title, string Message)> textFactory,
        string mediaTitle,
        string jellyfinUserId,
        SeerrRequest request,
        ExternalIds externalIds,
        string? posterUrl,
        int? year,
        string? previousState,
        string? newState,
        CancellationToken cancellationToken,
        ArrMediaInfoResult? mediaInfo = null)
    {
        var prefs = await _preferenceStore.GetByUserAsync(jellyfinUserId).ConfigureAwait(false);
        var language = NotificationLanguage.Resolve(prefs);
        var (title, message) = textFactory(mediaTitle, language);

        var notification = new NotificationEvent
        {
            JellyfinUserId = jellyfinUserId,
            Type = type,
            Title = title,
            Message = message,
            MediaTitle = mediaTitle,
            MediaType = request.Type,
            ExternalIds = externalIds,
            ThumbnailUrl = posterUrl,
            Year = year,
            SeerrRequestId = request.Id,
            PreviousState = previousState,
            NewState = newState,
            Quality = mediaInfo?.Quality,
            AudioLanguages = mediaInfo?.AudioLanguages,
            SubtitleLanguages = mediaInfo?.SubtitleLanguages,
            CreatedAt = DateTime.UtcNow
        };

        await _dispatcher.DispatchAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches poster/title/year details for a request via Seerr's movie/tv detail
    /// endpoints, using whichever TMDB ID is available. Rate-limited per cycle (see
    /// <see cref="MaxDetailFetchesPerCycle"/>) — callers should only call this when they
    /// actually still need something from it (missing poster or still-generic title),
    /// not unconditionally on every snapshot, so the limited budget goes to snapshots
    /// that need it. Returns null (not an error) when there's no TMDB ID, the cap for
    /// this cycle is already spent, or Seerr has nothing for it.
    /// </summary>
    private async Task<SeerrMediaDetails?> ResolveExternalMediaDetailsAsync(SeerrRequest request, CancellationToken cancellationToken)
    {
        if (_detailFetchesThisCycle >= MaxDetailFetchesPerCycle)
        {
            return null;
        }

        var tmdbId = request.Media?.TmdbId ?? request.TmdbId;
        if (tmdbId is null or 0)
        {
            return null;
        }

        _detailFetchesThisCycle++;
        return await _seerr.GetMediaDetailsAsync(request.Type, tmdbId.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stamps the first-observed timestamp for each status milestone this request has
    /// reached, so /status can show a real date even after the request has since moved
    /// on to a later status. Uses <c>??=</c> throughout — once set, a timestamp is never
    /// overwritten, since it should reflect the first time it was seen, not the most
    /// recent poll. Safe to call on every poll, baseline or not.
    /// </summary>
    private static void StampStatusTimestamps(RequestSnapshot snapshot, SeerrRequest request)
    {
        snapshot.RequestedAt ??= request.CreatedAt;

        if (request.Status == (int)SeerrRequestStatus.Approved)
        {
            snapshot.ApprovedAt ??= DateTime.UtcNow;
        }

        if (request.Media?.Status == (int)SeerrMediaStatus.Processing)
        {
            snapshot.DownloadStartedAt ??= DateTime.UtcNow;
        }

        if (request.Media?.Status == (int)SeerrMediaStatus.PartiallyAvailable)
        {
            snapshot.PartiallyAvailableAt ??= DateTime.UtcNow;
        }

        if (request.Media?.Status == (int)SeerrMediaStatus.Available)
        {
            snapshot.AvailableAt ??= DateTime.UtcNow;
        }

        if (request.Status == (int)SeerrRequestStatus.Declined
            || request.Status == (int)SeerrRequestStatus.Failed
            || request.Media?.Status == (int)SeerrMediaStatus.Blocklisted
            || request.Media?.Status == (int)SeerrMediaStatus.Deleted)
        {
            snapshot.FailedAt ??= DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Extracts the best available display title from a Seerr request.
    /// Priority: explicit title/name fields, embedded MediaInfo, media fields, then media type label.
    /// </summary>
    internal static string ExtractMediaTitle(SeerrRequest request)
    {
        var title = FirstNonEmpty(
            request.Title,
            request.Name,
            request.OriginalTitle,
            request.OriginalName,
            request.MediaInfo?.DisplayTitle,
            request.Media?.Title,
            request.Media?.Name,
            request.Media?.OriginalTitle,
            request.Media?.OriginalName);

        // Fall back to media type label (better than empty) — GenericTypeLabel/
        // IsGenericFallbackTitle below must stay in sync with this exact fallback.
        return !string.IsNullOrWhiteSpace(title) ? title : GenericTypeLabel(request.Type);
    }

    /// <summary>The generic placeholder <see cref="ExtractMediaTitle"/> falls back to when Seerr has no real title. Internal so <see cref="RequestStatusSummaryBuilder"/> can use the exact same definition to keep placeholder rows out of its visible "/status" list.</summary>
    internal static string GenericTypeLabel(string mediaType) => mediaType switch
    {
        "movie" => "Movie",
        "tv" => "TV Show",
        _ => "Media"
    };

    /// <summary>
    /// Whether the given title is (or would be) just the generic type-label fallback
    /// rather than a real title — used to decide whether it's worth trying to upgrade it
    /// via a Seerr detail fetch. Compares against the exact fallback text so it can't
    /// accidentally treat a genuine title that happens to contain "tv" or "movie" as generic.
    /// Internal so <see cref="RequestStatusSummaryBuilder"/> can reuse it (see <see cref="GenericTypeLabel"/>).
    /// </summary>
    internal static bool IsGenericFallbackTitle(string? title, string mediaType) =>
        string.IsNullOrWhiteSpace(title) || string.Equals(title, GenericTypeLabel(mediaType), StringComparison.Ordinal);

    private static string MapStatus(int status) => status switch
    {
        1 => "Pending",
        2 => "Approved",
        3 => "Declined",
        4 => "Failed",
        _ => "Unknown"
    };

    private static string MapMediaStatus(int status) => status switch
    {
        1 => "Unknown",
        2 => "Pending",
        3 => "Processing",
        4 => "Partial",
        5 => "Available",
        6 => "Deleted",
        7 => "Blocklisted",
        _ => "Unknown"
    };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

