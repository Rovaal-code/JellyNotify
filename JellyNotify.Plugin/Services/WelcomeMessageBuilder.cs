using System.Text;
using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Builds the standard "you're connected" message sent by the bot the first time a
/// user successfully links a personal channel (Telegram, Discord, or WhatsApp).
/// Shared by all three connect flows so the wording stays in sync. "JellyNotify",
/// the username, and the server name are rendered bold using whichever markup the
/// target channel actually understands (Telegram HTML, Discord/WhatsApp Markdown-ish).
/// </summary>
public static class WelcomeMessageBuilder
{
    private const string BotBrand = "JellyNotify";

    /// <summary>
    /// Builds the localized welcome message for a newly-linked channel. The effective
    /// language is resolved from <paramref name="prefs"/> via <see cref="NotificationLanguage"/>
    /// — callers should not resolve "auto" themselves. The username and server name are
    /// escaped for whatever formatting syntax <paramref name="channel"/> uses before being
    /// bolded, since both are attacker- or admin-controlled free text that could otherwise
    /// break the message's formatting (or, for Telegram's HTML parse mode, the send itself).
    /// </summary>
    /// <param name="userDisplayName">The Jellyfin username to greet.</param>
    /// <param name="serverName">The Jellyfin server's configured friendly name.</param>
    /// <param name="prefs">The user's notification preferences.</param>
    /// <param name="channel">
    /// Which channel this message is being sent on — controls both the bold markup used
    /// and whether the "check your requests" command hint is shown, since only Telegram
    /// and WhatsApp can currently receive commands back from the user (Discord has no
    /// inbound channel without a Gateway connection or a registered Slash Command).
    /// </param>
    public static string Build(string userDisplayName, string serverName, UserNotificationPreference prefs, WelcomeMessageChannel channel)
    {
        var language = NotificationLanguage.Resolve(prefs);
        var isSpanish = string.Equals(language, "es-ES", System.StringComparison.OrdinalIgnoreCase);
        var isCatalan = string.Equals(language, "ca", System.StringComparison.OrdinalIgnoreCase);

        var brand = Bold(BotBrand, channel);
        var user = Bold(EscapeForChannel(userDisplayName, channel), channel);
        var server = Bold(EscapeForChannel(serverName, channel), channel);

        var sb = new StringBuilder();
        if (isCatalan)
        {
            sb.Append(brand).Append("\n\n");
            sb.Append($"Hola {user}!\n\n");
            sb.Append($"A partir d'ara, rebràs notificacions segons el que hagis configurat al servidor de {server}.\n\n");
            sb.Append("Per defecte (si encara no has canviat cap ajust), rebràs:\n\n");
            AppendNumberedList(sb, CategoryLabels(isCatalan: true, isSpanish: false));
            sb.Append("\n\nSi vols canviar algun ajust de les notificacions que reps, fes-ho des del panell d'ajustos de la campana de notificacions del servidor.");
            AppendStatusHint(sb, channel, isCatalan: true, isSpanish: false);
        }
        else if (isSpanish)
        {
            sb.Append(brand).Append("\n\n");
            sb.Append($"¡Hola {user}!\n\n");
            sb.Append($"Desde ahora, recibirás notificaciones según lo que hayas configurado en el servidor de {server}.\n\n");
            sb.Append("Por defecto (si no has cambiado ya algún ajuste), recibirás:\n\n");
            AppendNumberedList(sb, CategoryLabels(isCatalan: false, isSpanish: true));
            sb.Append("\n\nSi deseas cambiar algún ajuste de las notificaciones que recibes, hazlo desde el panel de ajustes de la campana de notificaciones del servidor.");
            AppendStatusHint(sb, channel, isCatalan: false, isSpanish: true);
        }
        else
        {
            sb.Append(brand).Append("\n\n");
            sb.Append($"Hi {user}!\n\n");
            sb.Append($"From now on, you'll receive notifications based on what you've configured on {server}'s server.\n\n");
            sb.Append("By default (if you haven't already changed any setting), you'll receive:\n\n");
            AppendNumberedList(sb, CategoryLabels(isCatalan: false, isSpanish: false));
            sb.Append("\n\nIf you want to change any of your notification settings, do so from the notification bell's settings panel on the server.");
            AppendStatusHint(sb, channel, isCatalan: false, isSpanish: false);
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> CategoryLabels(bool isCatalan, bool isSpanish) => isCatalan
        ? new[]
        {
            "Noves sol·licituds de contingut",
            "Aprovacions i rebutjos de sol·licituds",
            "Progrés de les descàrregues",
            "Contingut disponible a la biblioteca",
            "Contingut parcialment disponible",
            "Problemes o incidències"
        }
        : isSpanish
            ? new[]
            {
                "Nuevas solicitudes de contenido",
                "Aprobaciones y rechazos de solicitudes",
                "Progreso de descargas",
                "Contenido disponible en la biblioteca",
                "Contenido parcialmente disponible",
                "Problemas o incidencias"
            }
            : new[]
            {
                "New content requests",
                "Request approvals and declines",
                "Download progress",
                "Content available in the library",
                "Partially available content",
                "Problems or issues"
            };

    private static void AppendStatusHint(StringBuilder sb, WelcomeMessageChannel channel, bool isCatalan, bool isSpanish)
    {
        switch (channel)
        {
            case WelcomeMessageChannel.Telegram:
                sb.Append(isCatalan
                    ? "\n\nEnvia /status en qualsevol moment per consultar l'estat de les teves sol·licituds actuals."
                    : isSpanish
                        ? "\n\nEnvía /status en cualquier momento para consultar el estado de tus solicitudes actuales."
                        : "\n\nSend /status anytime to check the status of your current requests.");
                break;
            case WelcomeMessageChannel.WhatsApp:
                sb.Append(isCatalan
                    ? "\n\nEscriu \"estado\" en qualsevol moment per consultar l'estat de les teves sol·licituds actuals."
                    : isSpanish
                        ? "\n\nEscribe \"estado\" en cualquier momento para consultar el estado de tus solicitudes actuales."
                        : "\n\nSend \"status\" anytime to check the status of your current requests.");
                break;
            case WelcomeMessageChannel.Discord:
            default:
                // Discord has no inbound channel yet (would need a Slash Command +
                // Interactions Endpoint, or a Gateway connection) — nothing to hint at.
                break;
        }
    }

    private static void AppendNumberedList(StringBuilder sb, IReadOnlyList<string> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            sb.Append(i + 1).Append(". ").Append(items[i]);
            if (i < items.Count - 1)
            {
                sb.Append('\n');
            }
        }
    }

    /// <summary>Wraps already-escaped text in the bold markup the given channel understands.</summary>
    private static string Bold(string text, WelcomeMessageChannel channel) => channel switch
    {
        WelcomeMessageChannel.Telegram => $"<b>{text}</b>",
        WelcomeMessageChannel.Discord => $"**{text}**",
        WelcomeMessageChannel.WhatsApp => $"*{text}*",
        _ => text
    };

    /// <summary>
    /// Escapes a dynamic, untrusted value (Jellyfin username, server friendly name) for
    /// safe interpolation into the given channel's formatting syntax.
    /// </summary>
    private static string EscapeForChannel(string text, WelcomeMessageChannel channel) => channel switch
    {
        WelcomeMessageChannel.Telegram => EscapeHtml(text),
        WelcomeMessageChannel.Discord => EscapeDiscordMarkdown(text),
        WelcomeMessageChannel.WhatsApp => EscapeLightweightMarkdown(text),
        _ => text
    };

    /// <summary>
    /// HTML-escapes for Telegram's <c>parse_mode=HTML</c>. Not just cosmetic: an
    /// unescaped '&lt;' or '&gt;' breaks the HTML structure and Telegram rejects the
    /// whole message rather than rendering it.
    /// </summary>
    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Backslash-escapes Discord's Markdown metacharacters so a value containing e.g.
    /// an asterisk can't break out of the surrounding **bold** span or inject
    /// unintended formatting.
    /// </summary>
    private static string EscapeDiscordMarkdown(string text) =>
        EscapeCharacters(text, '\\', '*', '_', '~', '`', '|', '>');

    /// <summary>
    /// Backslash-escapes WhatsApp's lightweight formatting metacharacters (its own
    /// asterisk/underscore/tilde/backtick markup) for the same reason as Discord.
    /// </summary>
    private static string EscapeLightweightMarkdown(string text) =>
        EscapeCharacters(text, '\\', '*', '_', '~', '`');

    private static string EscapeCharacters(string text, params char[] toEscape)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (Array.IndexOf(toEscape, c) >= 0)
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}

/// <summary>Which channel a <see cref="WelcomeMessageBuilder"/> message is being sent on.</summary>
public enum WelcomeMessageChannel
{
    /// <summary>Telegram — supports the /status command via the background linking poller.</summary>
    Telegram,

    /// <summary>Discord — no inbound channel yet, so no command hint is shown.</summary>
    Discord,

    /// <summary>WhatsApp — supports a "status" keyword via the Cloud API webhook.</summary>
    WhatsApp
}
