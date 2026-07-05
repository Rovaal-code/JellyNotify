using System.Globalization;
using Jellyfin.Plugin.JellyNotify;
using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// The external channel a notification card is being formatted for — each has a
/// different bold-text syntax (Discord Markdown, Telegram HTML, WhatsApp's
/// single-asterisk Markdown), so field formatting is channel-aware. The in-app bell
/// never calls <see cref="NotificationCardFormatter.Enrich"/> at all (see
/// <see cref="NotificationDispatcher"/>) — it stores the bare base message — so no
/// "InApp" member exists here.
/// </summary>
public enum NotificationChannel
{
    /// <summary>Discord (webhook and personal DM) — Markdown <c>**bold**</c>.</summary>
    Discord,

    /// <summary>Telegram — HTML <c>&lt;b&gt;bold&lt;/b&gt;</c>, requires escaping.</summary>
    Telegram,

    /// <summary>WhatsApp — single-asterisk <c>*bold*</c> (double-asterisk renders as literal asterisks).</summary>
    WhatsApp
}

/// <summary>
/// Builds the full, localized, per-channel-formatted body of a notification — a leading
/// "Estado" field (this notification type's own icon + its
/// <see cref="NotificationEvent.Title"/>, e.g. "📋 Estado / Solicitud registrada") followed
/// by whichever type-specific fields apply (Progreso/ETA for downloads,
/// Título/Año/Calidad/Audio/Subtítulos for availability...). Each field renders as its own
/// two-line "emoji **Label**\nValue" block, separated by a blank line — there is no
/// separate free-text message anymore; this is the entire body. The poster itself is
/// attached separately by each channel's client (Discord embed image, Telegram/WhatsApp
/// sendPhoto), not part of this text. Fields whose backing value is null/empty are
/// omitted. Called once per external channel by <see cref="NotificationDispatcher"/>,
/// immediately before sending — the in-app store never calls this, so its copy stays the
/// bare base message.
/// </summary>
public static class NotificationCardFormatter
{
    /// <summary>Builds the notification's full text body (Estado field + type-specific fields), formatted for <paramref name="channel"/>.</summary>
    public static string Enrich(NotificationEvent notification, string language, NotificationChannel channel) =>
        BuildFieldBlock(notification, language, channel);

    /// <summary>Same as <see cref="Enrich"/> — kept as a separate name for callers that only want the field block by itself.</summary>
    public static string BuildFieldBlock(NotificationEvent notification, string language, NotificationChannel channel)
    {
        var t = FieldLabels.For(language);
        var fields = new List<(string Emoji, string Label, string Value)>
        {
            (GetTypeIcon(notification.Type), t.Estado, notification.Title)
        };
        fields.AddRange(BuildFields(notification, t));
        return string.Join("\n\n", fields.Select(f => FormatField(f, channel)));
    }

    /// <summary>
    /// The same per-type icon shown next to this notification in the bell (see
    /// <c>TYPE_ICONS</c> in jellynotify.js) — kept in sync manually since the two run in
    /// different languages with no shared source of truth.
    /// </summary>
    private static string GetTypeIcon(NotificationType type) => type switch
    {
        NotificationType.RequestCreated => "📋",
        NotificationType.RequestApproved => "✅",
        NotificationType.RequestDeclined => "❌",
        NotificationType.RequestFailed => "⚠️",
        NotificationType.DownloadStarted => "⬇️",
        NotificationType.DownloadProgress => "📊",
        NotificationType.DownloadWarning => "⚠️",
        NotificationType.DownloadFailed => "💔",
        NotificationType.MediaAvailable => "🎬",
        NotificationType.MediaPartiallyAvailable => "🎞️",
        NotificationType.IssueWarning => "🔔",
        NotificationType.IssueResolved => "🎉",
        _ => "🔔"
    };

    private static List<(string Emoji, string Label, string Value)> BuildFields(NotificationEvent n, FieldLabels t) =>
        n.Type switch
        {
            NotificationType.RequestCreated or NotificationType.RequestApproved or NotificationType.IssueResolved =>
                TitleYear(n, t),
            NotificationType.RequestDeclined or NotificationType.RequestFailed or NotificationType.IssueWarning =>
                Reason(n, t),
            NotificationType.DownloadStarted or NotificationType.DownloadProgress or NotificationType.DownloadWarning =>
                ProgressEta(n, t),
            NotificationType.DownloadFailed => Reason(n, t),
            NotificationType.MediaAvailable => MediaFields(n, t, includeSeason: false),
            NotificationType.MediaPartiallyAvailable => MediaFields(n, t, includeSeason: true),
            _ => new List<(string, string, string)>()
        };

    private static List<(string Emoji, string Label, string Value)> TitleYear(NotificationEvent n, FieldLabels t)
    {
        var fields = new List<(string, string, string)>();
        if (!string.IsNullOrWhiteSpace(n.MediaTitle))
        {
            fields.Add(("🎬", t.Titulo, n.MediaTitle));
        }

        if (n.Year is not null)
        {
            fields.Add(("📅", t.Ano, n.Year.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return fields;
    }

    private static List<(string Emoji, string Label, string Value)> Reason(NotificationEvent n, FieldLabels t)
    {
        var fields = new List<(string, string, string)>();
        if (!string.IsNullOrWhiteSpace(n.FailureReason))
        {
            fields.Add(("⚠️", t.Motivo, n.FailureReason));
        }

        return fields;
    }

    private static List<(string Emoji, string Label, string Value)> ProgressEta(NotificationEvent n, FieldLabels t)
    {
        var fields = new List<(string, string, string)>();
        if (n.ProgressPercent is not null)
        {
            fields.Add(("📊", t.Progreso, n.ProgressPercent.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%"));
        }

        fields.Add(("⏳", t.Eta, FormatEta(n.EtaRaw, t)));
        return fields;
    }

    private static List<(string Emoji, string Label, string Value)> MediaFields(NotificationEvent n, FieldLabels t, bool includeSeason)
    {
        var fields = TitleYear(n, t);
        if (!string.IsNullOrWhiteSpace(n.Quality))
        {
            fields.Add(("🎞️", t.Calidad, n.Quality));
        }

        if (!string.IsNullOrWhiteSpace(n.AudioLanguages))
        {
            fields.Add(("🔊", t.Audio, TruncateLanguageList(n.AudioLanguages)));
        }

        if (!string.IsNullOrWhiteSpace(n.SubtitleLanguages))
        {
            fields.Add(("💬", t.Subtitulos, TruncateLanguageList(n.SubtitleLanguages)));
        }

        if (includeSeason && n.Season is not null)
        {
            fields.Add(("📺", t.Temporada, n.Season.Value.ToString(CultureInfo.InvariantCulture)));
        }

        return fields;
    }

    /// <summary>
    /// Caps a comma-separated language list (e.g. from Sonarr/Radarr's own MediaInfo) at 3
    /// entries, folding the rest into a trailing "…(+N)" — some releases carry a dozen+
    /// audio/subtitle tracks, which would otherwise dominate the whole card.
    /// </summary>
    private static string TruncateLanguageList(string languages)
    {
        var all = languages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (all.Length <= 3)
        {
            return languages;
        }

        return $"{string.Join(", ", all.Take(3))} …(+{all.Length - 3})";
    }

    private static string FormatField((string Emoji, string Label, string Value) f, NotificationChannel channel) => channel switch
    {
        NotificationChannel.Discord => $"{f.Emoji} **{f.Label}**\n{f.Value}",
        NotificationChannel.Telegram => $"{f.Emoji} <b>{TelegramNotificationClient.EscapeHtml(f.Label)}</b>\n{TelegramNotificationClient.EscapeHtml(f.Value)}",
        NotificationChannel.WhatsApp => $"{f.Emoji} *{f.Label}*\n{f.Value}",
        _ => $"{f.Emoji} {f.Label}\n{f.Value}"
    };

    /// <summary>
    /// Lenient parsing of *arr's raw "timeleft" (a TimeSpan-shaped string) — falls back to
    /// the raw value for a shape this doesn't recognize, and to an explicit "unknown" (never
    /// a blank field) when *arr hasn't estimated one yet.
    /// </summary>
    private static string FormatEta(string? rawTimeLeft, FieldLabels t)
    {
        if (string.IsNullOrWhiteSpace(rawTimeLeft))
        {
            return t.Unknown;
        }

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

    private sealed class FieldLabels
    {
        public required string Estado { get; init; }
        public required string Titulo { get; init; }
        public required string Ano { get; init; }
        public required string Progreso { get; init; }
        public required string Eta { get; init; }
        public required string Calidad { get; init; }
        public required string Audio { get; init; }
        public required string Subtitulos { get; init; }
        public required string Temporada { get; init; }
        public required string Motivo { get; init; }
        public required string Unknown { get; init; }
        public required string Days { get; init; }
        public required string Hours { get; init; }
        public required string Minutes { get; init; }

        public static FieldLabels For(string language)
        {
            var isSpanish = string.Equals(language, "es-ES", StringComparison.OrdinalIgnoreCase);
            var isCatalan = string.Equals(language, "ca", StringComparison.OrdinalIgnoreCase);

            return isCatalan
                ? new FieldLabels
                {
                    Estado = "Estat",
                    Titulo = "Títol",
                    Ano = "Any",
                    Progreso = "Progrés",
                    Eta = "ETA",
                    Calidad = "Qualitat",
                    Audio = "Àudio",
                    Subtitulos = "Subtítols",
                    Temporada = "Temporada",
                    Motivo = "Motiu",
                    Unknown = "desconegut",
                    Days = "d",
                    Hours = "h",
                    Minutes = "min"
                }
                : isSpanish
                    ? new FieldLabels
                    {
                        Estado = "Estado",
                        Titulo = "Título",
                        Ano = "Año",
                        Progreso = "Progreso",
                        Eta = "ETA",
                        Calidad = "Calidad",
                        Audio = "Audio",
                        Subtitulos = "Subtítulos",
                        Temporada = "Temporada",
                        Motivo = "Motivo",
                        Unknown = "desconocido",
                        Days = "d",
                        Hours = "h",
                        Minutes = "min"
                    }
                    : new FieldLabels
                    {
                        Estado = "Status",
                        Titulo = "Title",
                        Ano = "Year",
                        Progreso = "Progress",
                        Eta = "ETA",
                        Calidad = "Quality",
                        Audio = "Audio",
                        Subtitulos = "Subtitles",
                        Temporada = "Season",
                        Motivo = "Reason",
                        Unknown = "unknown",
                        Days = "d",
                        Hours = "h",
                        Minutes = "min"
                    };
        }
    }
}
