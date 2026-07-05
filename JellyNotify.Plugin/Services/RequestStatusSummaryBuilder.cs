using System.Globalization;
using System.Text;
using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Builds a localized, per-user summary of current Seerr request statuses (pending
/// approval, approved/queued, downloading with real progress/ETA/quality, partially
/// available, available, blocklisted, declined, failed) for the Telegram /status
/// command and the WhatsApp "status" keyword. Reads entirely from
/// <see cref="Store.IRequestSnapshotStore"/> — a fully in-memory-cached local JSON
/// store — so answering this costs zero network calls to Seerr/Sonarr/Radarr no
/// matter how often a user asks.
///
/// Known limitation: audio language and subtitle track lists are NOT included.
/// Sonarr/Radarr's download-queue API (the only per-item data this reads, by
/// design, to keep the zero-network-cost guarantee above) never exposes that —
/// it only becomes known after import, via a separate per-file MediaInfo call
/// this plugin does not make. Showing it would mean one extra HTTP request per
/// downloading item, every time /status is asked, which defeats the point.
/// </summary>
public static class RequestStatusSummaryBuilder
{
    private const int MaxTitlesPerCategory = 5;

    /// <summary>Builds the localized status summary for one user's requests.</summary>
    public static string Build(IReadOnlyList<RequestSnapshot> allSnapshots, string jellyfinUserId, string language)
    {
        var mine = allSnapshots.Where(s => string.Equals(s.JellyfinUserId, jellyfinUserId, StringComparison.OrdinalIgnoreCase)).ToList();

        var isSpanish = string.Equals(language, "es-ES", StringComparison.OrdinalIgnoreCase);
        var isCatalan = string.Equals(language, "ca", StringComparison.OrdinalIgnoreCase);

        if (mine.Count == 0)
        {
            return isCatalan
                ? "No tens cap sol·licitud registrada."
                : isSpanish
                    ? "No tienes ninguna solicitud registrada."
                    : "You don't have any requests on record.";
        }

        var pending = mine.Where(s => HasReqStatus(s, "Pending")).ToList();
        var declined = mine.Where(s => HasReqStatus(s, "Declined")).ToList();
        var failed = mine.Where(s => HasReqStatus(s, "Failed")).ToList();
        var blocklisted = mine.Where(s => HasReqStatus(s, "Approved") && (HasMediaStatus(s, "Deleted") || HasMediaStatus(s, "Blocklisted") || IsArrBlocked(s))).ToList();
        var downloading = mine.Where(s => HasReqStatus(s, "Approved") && HasMediaStatus(s, "Processing") && !blocklisted.Contains(s)).ToList();
        var partiallyAvailable = mine.Where(s => HasMediaStatus(s, "Partial")).ToList();
        var available = mine.Where(s => HasMediaStatus(s, "Available")).ToList();
        var approvedWaiting = mine.Where(s => HasReqStatus(s, "Approved")
            && !downloading.Contains(s) && !blocklisted.Contains(s) && !partiallyAvailable.Contains(s) && !available.Contains(s)).ToList();

        var t = Strings.For(isCatalan, isSpanish);

        var sb = new StringBuilder();
        sb.Append(t.Header);

        AppendCategory(sb, pending, "🕓", t.Pending, s => TitleWithYear(s), s => s.RequestedAt);
        AppendCategory(sb, approvedWaiting, "✅", t.ApprovedQueued, s => TitleWithYear(s), s => s.ApprovedAt ?? s.RequestedAt);
        AppendCategory(sb, downloading, "⬇️", t.Downloading, s => DownloadingLine(s, t), s => s.ArrLastProgressAt ?? s.DownloadStartedAt);
        AppendCategory(sb, partiallyAvailable, "🎞️", t.PartiallyAvailable, s => TitleWithYear(s), s => s.PartiallyAvailableAt);
        AppendCategory(sb, available, "🎬", t.Available, s => TitleWithYear(s), s => s.AvailableAt);
        AppendCategory(sb, blocklisted, "🚫", t.Blocklisted, s => TitleWithYear(s), s => s.FailedAt);
        AppendCategory(sb, declined, "❌", t.Declined, s => TitleWithYear(s), s => s.FailedAt);
        AppendCategory(sb, failed, "💔", t.Failed, s => TitleWithYear(s), s => s.FailedAt);

        return sb.ToString();
    }

    private static string TitleWithYear(RequestSnapshot s) =>
        s.Year is > 0 ? $"{s.MediaTitle} ({s.Year})" : s.MediaTitle;

    /// <summary>Title, plus real progress/ETA/quality when *arr has actually reported them — omitted entirely rather than shown as a redundant "unknown", not a date.</summary>
    private static string DownloadingLine(RequestSnapshot s, Strings t)
    {
        var parts = new List<string> { TitleWithYear(s) };

        if (s.ArrProgress is not null)
        {
            parts.Add($"{s.ArrProgress.Value.ToString("0.#", CultureInfo.InvariantCulture)}%");
        }

        if (!string.IsNullOrWhiteSpace(s.ArrTimeLeft))
        {
            parts.Add($"{t.Eta} {FormatEta(s.ArrTimeLeft, t)}");
        }

        if (!string.IsNullOrWhiteSpace(s.ArrQuality))
        {
            parts.Add(s.ArrQuality);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Formats *arr's own "timeleft" (e.g. "01:23:00", or "2.01:23:00" for over a day)
    /// into a short, friendly duration. Falls back to showing the raw value if it's in
    /// some format this doesn't recognize. Only called once <see cref="DownloadingLine"/>
    /// has already confirmed *arr reported a timeleft at all.
    /// </summary>
    private static string FormatEta(string rawTimeLeft, Strings t)
    {
        if (!TimeSpan.TryParse(rawTimeLeft, CultureInfo.InvariantCulture, out var span))
        {
            return rawTimeLeft;
        }

        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}{t.Days} {span.Hours}{t.Hours}";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}{t.Hours} {span.Minutes}{t.Minutes}";
        }

        return $"{Math.Max(span.Minutes, 1)}{t.Minutes}";
    }

    private static void AppendCategory(StringBuilder sb, IReadOnlyList<RequestSnapshot> items, string icon, string label, Func<RequestSnapshot, string> format, Func<RequestSnapshot, DateTime?> sortKey)
    {
        if (items.Count == 0)
        {
            return;
        }

        // Partition before taking the top N so a generic "Movie"/"TV Show"/"Media"
        // placeholder (title not resolved yet — see MediaRequestService.MaxDetailFetchesPerCycle)
        // never displaces a real title in the visible list; it always counts toward
        // "+N" instead, regardless of how many real titles there are. Most-recent-first
        // by this category's own relevant date, so a title that just started downloading
        // (for example) shows up instead of being buried behind older entries that
        // happen to sit earlier in the underlying store.
        var real = items
            .Where(i => !MediaRequestService.IsGenericFallbackTitle(i.MediaTitle, i.MediaType))
            .OrderByDescending(sortKey)
            .ToList();
        var genericCount = items.Count - real.Count;

        sb.Append($"\n\n{icon} {label} ({items.Count}):");
        foreach (var item in real.Take(MaxTitlesPerCategory))
        {
            sb.Append($"\n- {format(item)}");
        }

        var remaining = Math.Max(0, real.Count - MaxTitlesPerCategory) + genericCount;
        if (remaining > 0)
        {
            sb.Append($"\n… (+{remaining})");
        }
    }

    private static bool HasReqStatus(RequestSnapshot snapshot, string status) =>
        snapshot.Status.Contains($"req:{status}", StringComparison.Ordinal);

    private static bool HasMediaStatus(RequestSnapshot snapshot, string status) =>
        snapshot.Status.Contains($"media:{status}", StringComparison.Ordinal);

    private static bool IsArrBlocked(RequestSnapshot snapshot) =>
        string.Equals(snapshot.ArrStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(snapshot.ArrStatus, "blocklisted", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bundles every localized label /status needs, resolved once per call.</summary>
    private sealed class Strings
    {
        public required string Header { get; init; }
        public required string Pending { get; init; }
        public required string ApprovedQueued { get; init; }
        public required string Downloading { get; init; }
        public required string PartiallyAvailable { get; init; }
        public required string Available { get; init; }
        public required string Blocklisted { get; init; }
        public required string Declined { get; init; }
        public required string Failed { get; init; }
        public required string Eta { get; init; }
        public required string Days { get; init; }
        public required string Hours { get; init; }
        public required string Minutes { get; init; }

        public static Strings For(bool isCatalan, bool isSpanish) => isCatalan
            ? new Strings
            {
                Header = "Estat de les teves sol·licituds:",
                Pending = "Pendents d'aprovar",
                ApprovedQueued = "Aprovades, en cua",
                Downloading = "Descarregant",
                PartiallyAvailable = "Parcialment disponibles",
                Available = "Disponibles",
                Blocklisted = "Bloquejades",
                Declined = "Rebutjades",
                Failed = "Fallides",
                Eta = "ETA",
                Days = "d",
                Hours = "h",
                Minutes = "min"
            }
            : isSpanish
                ? new Strings
                {
                    Header = "Estado de tus solicitudes:",
                    Pending = "Pendientes de aprobar",
                    ApprovedQueued = "Aprobadas, en cola",
                    Downloading = "Descargando",
                    PartiallyAvailable = "Parcialmente disponibles",
                    Available = "Disponibles",
                    Blocklisted = "Bloqueadas",
                    Declined = "Rechazadas",
                    Failed = "Fallidas",
                    Eta = "ETA",
                    Days = "d",
                    Hours = "h",
                    Minutes = "min"
                }
                : new Strings
                {
                    Header = "Status of your requests:",
                    Pending = "Pending approval",
                    ApprovedQueued = "Approved, queued",
                    Downloading = "Downloading",
                    PartiallyAvailable = "Partially available",
                    Available = "Available",
                    Blocklisted = "Blocklisted",
                    Declined = "Declined",
                    Failed = "Failed",
                    Eta = "ETA",
                    Days = "d",
                    Hours = "h",
                    Minutes = "min"
                };
    }
}
