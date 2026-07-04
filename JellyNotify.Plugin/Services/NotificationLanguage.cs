using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Resolves the effective language to use for server-driven text (bot welcome messages,
/// test notifications, request-status summaries) given a user's notification preferences.
/// "Auto" has no meaning on the server by itself — there is no browser to inspect the
/// Jellyfin display language from — so this falls back through the last language the
/// frontend resolved "auto" to, then the admin's configured default, then English.
/// </summary>
public static class NotificationLanguage
{
    /// <summary>Resolves the effective language for the given user preferences.</summary>
    public static string Resolve(UserNotificationPreference prefs)
    {
        if (IsSupported(prefs.Language))
        {
            return prefs.Language;
        }

        if (IsSupported(prefs.ResolvedLanguage))
        {
            return prefs.ResolvedLanguage!;
        }

        var adminDefault = Plugin.Instance?.Configuration.DefaultLanguage;
        if (IsSupported(adminDefault))
        {
            return adminDefault!;
        }

        return "en-US";
    }

    /// <summary>
    /// Resolves the admin's configured default language, for contexts with no specific
    /// Jellyfin user to resolve a preference for (e.g. testing a global channel like the
    /// Telegram Global chat ID or the Discord webhook, which aren't tied to one account).
    /// </summary>
    public static string ResolveAdminDefault()
    {
        var adminDefault = Plugin.Instance?.Configuration.DefaultLanguage;
        return IsSupported(adminDefault) ? adminDefault! : "en-US";
    }

    /// <summary>Gets a value indicating whether the given language code is one of the three shipped locales.</summary>
    internal static bool IsSupported(string? language) =>
        string.Equals(language, "es-ES", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, "ca", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase);
}
