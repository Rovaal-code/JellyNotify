namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Localized title/message text for the various "send a test" actions (the General tab's
/// test notification, and the admin Channels tab's per-channel connectivity tests) — kept
/// separate from real event notification text (which is not localized yet).
/// </summary>
public static class TestMessages
{
    /// <summary>The generic test-notification text sent through the full dispatch pipeline (in-app + channels).</summary>
    public static (string Title, string Message) General(string language)
    {
        var (isSpanish, isCatalan) = Flags(language);
        return isCatalan
            ? ("Notificació de prova", "JellyNotify funciona correctament.")
            : isSpanish
                ? ("Notificación de prueba", "JellyNotify está funcionando correctamente.")
                : ("Test notification", "JellyNotify is working correctly.");
    }

    /// <summary>Text used for direct, per-channel connectivity tests (global Telegram chat, Discord webhook).</summary>
    public static (string Title, string Message) Channel(string language)
    {
        var (isSpanish, isCatalan) = Flags(language);
        return isCatalan
            ? ("Prova de JellyNotify", "Aquest és un missatge de prova de JellyNotify.")
            : isSpanish
                ? ("Prueba de JellyNotify", "Este es un mensaje de prueba de JellyNotify.")
                : ("JellyNotify test", "This is a test message from JellyNotify.");
    }

    private static (bool IsSpanish, bool IsCatalan) Flags(string language) => (
        string.Equals(language, "es-ES", System.StringComparison.OrdinalIgnoreCase),
        string.Equals(language, "ca", System.StringComparison.OrdinalIgnoreCase));
}
