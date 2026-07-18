using System.Globalization;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Localized result text for the admin config page's connection-test and sample-notification
/// buttons (Serr/Sonarr/Radarr/Telegram/Discord/WhatsApp). These are shown only in the admin's
/// own browser, so they're resolved via <see cref="NotificationLanguage.ResolveAdminDefault"/>
/// rather than any specific user's preference.
/// </summary>
public static class AdminTestMessages
{
    public static string SeerrConnectFail(string language) => Pick(language,
        "Could not connect to Seerr. Check the URL and API key.",
        "No se pudo conectar con Seerr. Comprueba la URL y la clave API.",
        "No s'ha pogut connectar amb Seerr. Comprova la URL i la clau API.");

    public static string SeerrConnectSuccess(string version, string language) => Format(language,
        "Connected successfully. Version: {0}",
        "Conectado correctamente. Versión: {0}",
        "Connectat correctament. Versió: {0}",
        version);

    public static string SonarrInstanceNotFound(string language) => Pick(language,
        "Sonarr instance not found.",
        "Instancia de Sonarr no encontrada.",
        "No s'ha trobat la instància de Sonarr.");

    public static string SonarrConnectFail(string language) => Pick(language,
        "Could not connect to Sonarr.",
        "No se pudo conectar con Sonarr.",
        "No s'ha pogut connectar amb Sonarr.");

    public static string SonarrConnectSuccess(string version, string language) => Format(language,
        "Connected to Sonarr {0}",
        "Conectado a Sonarr {0}",
        "Connectat a Sonarr {0}",
        version);

    public static string RadarrInstanceNotFound(string language) => Pick(language,
        "Radarr instance not found.",
        "Instancia de Radarr no encontrada.",
        "No s'ha trobat la instància de Radarr.");

    public static string RadarrConnectFail(string language) => Pick(language,
        "Could not connect to Radarr.",
        "No se pudo conectar con Radarr.",
        "No s'ha pogut connectar amb Radarr.");

    public static string RadarrConnectSuccess(string version, string language) => Format(language,
        "Connected to Radarr {0}",
        "Conectado a Radarr {0}",
        "Connectat a Radarr {0}",
        version);

    public static string SonarrSampleWithDownloads(int seriesCount, int downloadingCount, string language) => Format(language,
        "\"{0}\" series added, with {1} currently downloading.",
        "\"{0}\" series añadidas, con {1} descargándose ahora mismo.",
        "\"{0}\" sèries afegides, amb {1} descarregant-se ara mateix.",
        seriesCount, downloadingCount);

    public static string SonarrSampleNoDownloads(int seriesCount, string language) => Format(language,
        "\"{0}\" series added. Nothing is downloading right now.",
        "\"{0}\" series añadidas. No hay nada descargándose ahora mismo.",
        "\"{0}\" sèries afegides. No hi ha res descarregant-se ara mateix.",
        seriesCount);

    public static string RadarrSampleWithDownloads(int movieCount, int downloadingCount, string language) => Format(language,
        "{0} movies added, with {1} currently downloading.",
        "{0} películas añadidas, con {1} descargándose ahora mismo.",
        "{0} pel·lícules afegides, amb {1} descarregant-se ara mateix.",
        movieCount, downloadingCount);

    public static string RadarrSampleNoDownloads(int movieCount, string language) => Format(language,
        "{0} movies added. Nothing is downloading right now.",
        "{0} películas añadidas. No hay nada descargándose ahora mismo.",
        "{0} pel·lícules afegides. No hi ha res descarregant-se ara mateix.",
        movieCount);

    public static string SeerrSampleSummary(int movieCount, int tvCount, int pendingCount, string language) => Format(language,
        "{0} movies and {1} series requested in total, {2} of them still pending.",
        "{0} películas y {1} series solicitadas en total, {2} de ellas aún pendientes.",
        "{0} pel·lícules i {1} sèries sol·licitades en total, {2} encara pendents.",
        movieCount, tvCount, pendingCount);

    public static string TelegramNotConfigured(string language) => Pick(language,
        "Telegram is not enabled or has no bot token configured.",
        "Telegram no está activado o no tiene un token de bot configurado.",
        "Telegram no està activat o no té un token de bot configurat.");

    public static string TelegramNoMessagesFound(string language) => Pick(language,
        "No messages found yet. Open a chat with your bot in Telegram, send it any message, then wait a few seconds and try again.",
        "Aún no se ha encontrado ningún mensaje. Abre un chat con tu bot en Telegram, envíale cualquier mensaje, espera unos segundos e inténtalo de nuevo.",
        "Encara no s'ha trobat cap missatge. Obre un xat amb el teu bot a Telegram, envia-li qualsevol missatge, espera uns segons i torna-ho a provar.");

    public static string TelegramChatIdFound(string chatId, string language) => Format(language,
        "Found chat ID: {0}",
        "Chat ID encontrado: {0}",
        "Chat ID trobat: {0}",
        chatId);

    public static string TelegramNoChatId(string language) => Pick(language,
        "Set a Global chat ID first, or connect your personal Telegram from the notification panel.",
        "Configura primero un Chat ID global, o conecta tu Telegram personal desde el panel de notificaciones.",
        "Configura primer un Chat ID global, o connecta el teu Telegram personal des del panell de notificacions.");

    public static string TelegramTestSent(string language) => Pick(language,
        "Test message sent to the configured chat.",
        "Mensaje de prueba enviado al chat configurado.",
        "Missatge de prova enviat al xat configurat.");

    public static string TelegramTestRejected(string language) => Pick(language,
        "Telegram rejected the message. Check the bot token and chat ID.",
        "Telegram rechazó el mensaje. Comprueba el token del bot y el Chat ID.",
        "Telegram ha rebutjat el missatge. Comprova el token del bot i el Chat ID.");

    public static string DiscordNotConfigured(string language) => Pick(language,
        "Discord is not enabled or has no webhook URL configured.",
        "Discord no está activado o no tiene una URL de webhook configurada.",
        "Discord no està activat o no té una URL de webhook configurada.");

    public static string DiscordTestSent(string language) => Pick(language,
        "Test message sent to the configured webhook.",
        "Mensaje de prueba enviado al webhook configurado.",
        "Missatge de prova enviat al webhook configurat.");

    public static string DiscordTestRejected(string language) => Pick(language,
        "Discord rejected the message. Check the webhook URL.",
        "Discord rechazó el mensaje. Comprueba la URL del webhook.",
        "Discord ha rebutjat el missatge. Comprova la URL del webhook.");

    public static string WhatsAppNotConfigured(string language) => Pick(language,
        "WhatsApp is not enabled or has no phone number configured.",
        "WhatsApp no está activado o no tiene un número de teléfono configurado.",
        "WhatsApp no està activat o no té un número de telèfon configurat.");

    public static string WhatsAppCloudApiValid(string phoneNumber, string language) => Format(language,
        "Cloud API credentials are valid. Registered number: {0}",
        "Las credenciales de la Cloud API son válidas. Número registrado: {0}",
        "Les credencials de la Cloud API són vàlides. Número registrat: {0}",
        phoneNumber);

    public static string WhatsAppCloudApiInvalidFallback(string language) => Pick(language,
        "Could not verify the Cloud API credentials.",
        "No se pudieron verificar las credenciales de la Cloud API.",
        "No s'han pogut verificar les credencials de la Cloud API.");

    public static string WhatsAppLinkOnly(string waMeUrl, string language) => Format(language,
        "Cloud API not configured — link-only mode. Test link: {0}",
        "Cloud API no configurada — modo solo enlace. Enlace de prueba: {0}",
        "Cloud API no configurada — mode només enllaç. Enllaç de prova: {0}",
        waMeUrl);

    public static string ArrWebhookAlreadyConfigured(string language) => Pick(language,
        "A JellyNotify connection already exists on this instance — nothing was created.",
        "Ya existe una conexión de JellyNotify en esta instancia — no se ha creado ninguna nueva.",
        "Ja existeix una connexió de JellyNotify en aquesta instància — no se n'ha creat cap de nova.");

    public static string ArrWebhookRepaired(string language) => Pick(language,
        "Found the connection but its event checkboxes (Grab/Download/Upgrade) or URL were out of date — fixed it. Instant notifications are now enabled.",
        "Se encontró la conexión pero sus casillas de evento (Grab/Download/Upgrade) o su URL estaban desactualizadas — se ha corregido. Las notificaciones instantáneas se han activado.",
        "S'ha trobat la connexió però les seves caselles d'esdeveniment (Grab/Download/Upgrade) o la seva URL estaven desactualitzades — s'ha corregit. Les notificacions instantànies s'han activat.");

    public static string ArrWebhookRepairFailed(string reason, string language) => Format(language,
        "Found an existing JellyNotify connection with missing event checkboxes, but couldn't fix it automatically ({0}). Enable Grab, Download, and Upgrade manually in Settings → Connect → JellyNotify.",
        "Se encontró una conexión de JellyNotify existente con casillas de evento sin marcar, pero no se pudo corregir automáticamente ({0}). Activa Grab, Download y Upgrade a mano en Settings → Connect → JellyNotify.",
        "S'ha trobat una connexió de JellyNotify existent amb caselles d'esdeveniment sense marcar, però no s'ha pogut corregir automàticament ({0}). Activa Grab, Download i Upgrade a mà a Settings → Connect → JellyNotify.",
        reason);

    public static string ArrWebhookSchemaNotFound(string language) => Pick(language,
        "This version doesn't expose the Webhook notification template via its API — set it up manually using the Copy webhook URL button.",
        "Esta versión no expone la plantilla de notificación Webhook por su API — configúralo a mano usando el botón de copiar URL del webhook.",
        "Aquesta versió no exposa la plantilla de notificació Webhook per la seva API — configura-ho a mà fent servir el botó de copiar URL del webhook.");

    public static string ArrWebhookCreateFailed(string reason, string language) => Format(language,
        "Couldn't create the connection automatically ({0}). Set it up manually instead using the Copy webhook URL button.",
        "No se pudo crear la conexión automáticamente ({0}). Configúralo a mano usando el botón de copiar URL del webhook.",
        "No s'ha pogut crear la connexió automàticament ({0}). Configura-ho a mà fent servir el botó de copiar URL del webhook.",
        reason);

    public static string ArrWebhookCreateSuccess(string language) => Pick(language,
        "Created successfully. Instant notifications are now enabled.",
        "Creado correctamente. Las notificaciones instantáneas se han activado.",
        "Creat correctament. Les notificacions instantànies s'han activat.");

    public static string ArrWebhookCapabilityConfirmed(string language) => Pick(language,
        "The webhook can be created automatically when you save.",
        "El webhook se podrá crear automáticamente al guardar la configuración.",
        "El webhook es podrà crear automàticament en guardar la configuració.");

    public static string ArrWebhookCapabilityFailed(string reason, string language) => Format(language,
        "The automatic webhook couldn't be verified ({0}). Set it up manually instead: enable Grab, Download, and Upgrade in Settings → Connect → Webhook, using the Copy webhook URL button.",
        "No se pudo verificar el webhook automático ({0}). Configúralo a mano: activa Grab, Download y Upgrade en Settings → Connect → Webhook, usando el botón de copiar URL del webhook.",
        "No s'ha pogut verificar el webhook automàtic ({0}). Configura'l a mà: activa Grab, Download i Upgrade a Settings → Connect → Webhook, fent servir el botó de copiar URL del webhook.",
        reason);

    public static string ArrWebhookSchemaUnsupported(string language) => Pick(language,
        "this Sonarr/Radarr version doesn't expose the Webhook notification template via its API",
        "esta versión de Sonarr/Radarr no expone la plantilla de notificación Webhook por su API",
        "aquesta versió de Sonarr/Radarr no exposa la plantilla de notificació Webhook per la seva API");

    public static string ArrWebhookTestNoDetail(string language) => Pick(language,
        "no additional detail",
        "sin más detalle",
        "sense més detall");

    public static string SeerrWebhookCapabilityConfirmed(string language) => Pick(language,
        "The webhook can be configured automatically when you save.",
        "El webhook se podrá configurar automáticamente al guardar la configuración.",
        "El webhook es podrà configurar automàticament en guardar la configuració.");

    public static string SeerrWebhookCapabilityFailed(string reason, string language) => Format(language,
        "The automatic webhook couldn't be verified ({0}). Set it up manually instead in Settings → Notifications → Webhook, using the Copy webhook URL button.",
        "No se pudo verificar el webhook automático ({0}). Configúralo a mano en Settings → Notifications → Webhook, usando el botón de copiar URL del webhook.",
        "No s'ha pogut verificar el webhook automàtic ({0}). Configura'l a mà a Settings → Notifications → Webhook, fent servir el botó de copiar URL del webhook.",
        reason);

    public static string SeerrWebhookConflict(string language) => Pick(language,
        "Seerr already has a different webhook configured (not JellyNotify's) — it was left untouched to avoid breaking it. Set it up manually if you want to replace it.",
        "Seerr ya tiene configurado otro webhook (que no es de JellyNotify) — no se ha tocado para no romperlo. Configúralo a mano si quieres sustituirlo.",
        "Seerr ja té configurat un altre webhook (que no és de JellyNotify) — no s'ha tocat per no trencar-lo. Configura'l a mà si vols substituir-lo.");

    public static string SeerrWebhookAlreadyConfigured(string language) => Pick(language,
        "It was already configured — nothing was changed.",
        "Ya estaba configurado — no se ha cambiado nada.",
        "Ja estava configurat — no s'ha canviat res.");

    public static string SeerrWebhookCreateFailed(string reason, string language) => Format(language,
        "Couldn't configure it automatically ({0}). Set it up manually instead using the Copy webhook URL button.",
        "No se pudo configurar automáticamente ({0}). Configúralo a mano usando el botón de copiar URL del webhook.",
        "No s'ha pogut configurar automàticament ({0}). Configura-ho a mà fent servir el botó de copiar URL del webhook.",
        reason);

    public static string SeerrWebhookCreateSuccess(string language) => Pick(language,
        "Configured successfully. Instant notifications are now enabled.",
        "Configurado correctamente. Las notificaciones instantáneas se han activado.",
        "Configurat correctament. Les notificacions instantànies s'han activat.");

    private static string Pick(string language, string en, string es, string ca)
    {
        var isSpanish = string.Equals(language, "es-ES", StringComparison.OrdinalIgnoreCase);
        var isCatalan = string.Equals(language, "ca", StringComparison.OrdinalIgnoreCase);
        return isCatalan ? ca : isSpanish ? es : en;
    }

    private static string Format(string language, string enTemplate, string esTemplate, string caTemplate, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Pick(language, enTemplate, esTemplate, caTemplate), args);
}
