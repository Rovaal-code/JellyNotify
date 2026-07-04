namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Truncates a notification's combined text so it fits Telegram's <c>sendPhoto</c> and
/// WhatsApp's image-message caption limit (~1024 characters) — neither API enforces this
/// server-side with a helpful error, they just reject or silently clip the caption.
/// Only the free-text base message is ever cut; the field block (Estado + emoji fields)
/// is short, bounded, and the highest-value part of the new card layout, so it always
/// survives intact.
/// </summary>
public static class CaptionTruncator
{
    /// <summary>Telegram <c>sendPhoto</c> caption limit and WhatsApp's image-message caption limit — both ~1024 characters.</summary>
    public const int PhotoCaptionLimit = 1024;

    /// <summary>
    /// Truncates <paramref name="fullText"/> to fit <paramref name="limit"/> characters,
    /// preferring to cut the base message (the text before the first blank line) and
    /// leaving the field block that follows untouched.
    /// </summary>
    public static string TruncateForPhotoCaption(string fullText, int limit = PhotoCaptionLimit)
    {
        if (fullText.Length <= limit)
        {
            return fullText;
        }

        var splitIndex = fullText.IndexOf("\n\n", StringComparison.Ordinal);
        if (splitIndex < 0)
        {
            // No field block at all — nothing to preserve, hard cutoff on the whole text.
            return limit <= 1 ? fullText[..limit] : fullText[..(limit - 1)] + "…";
        }

        var fieldBlock = fullText[splitIndex..]; // includes the leading "\n\n"
        if (fieldBlock.Length >= limit)
        {
            // Degenerate case: the field block alone doesn't fit either — hard cutoff on it,
            // dropping the base message entirely rather than truncating fields.
            return limit <= 1 ? fieldBlock[..limit] : fieldBlock[..(limit - 1)] + "…";
        }

        var basePart = fullText[..splitIndex];
        var budget = limit - fieldBlock.Length - 1; // -1 reserves room for the ellipsis
        var truncatedBase = basePart[..Math.Min(basePart.Length, Math.Max(budget, 0))];
        return $"{truncatedBase}…{fieldBlock}";
    }
}
