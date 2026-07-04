using Jellyfin.Plugin.JellyNotify.Services;
using Jellyfin.Plugin.JellyNotify.Store;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Registers all JellyNotify services into the Jellyfin dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // --- HTTP Clients ---
        // Typed HttpClient for Overseerr/Jellyseerr API communication.
        serviceCollection.AddHttpClient<ISeerrApiClient, SeerrApiClient>();

        // Typed HttpClient for Sonarr API communication.
        serviceCollection.AddHttpClient<ISonarrApiClient, SonarrApiClient>();

        // Typed HttpClient for Radarr API communication.
        serviceCollection.AddHttpClient<IRadarrApiClient, RadarrApiClient>();

        // Typed HttpClient for Discord webhook delivery.
        serviceCollection.AddHttpClient<IDiscordNotificationClient, DiscordNotificationClient>();

        // Typed HttpClient for Telegram bot API delivery.
        serviceCollection.AddHttpClient<ITelegramNotificationClient, TelegramNotificationClient>();

        // Typed HttpClient for Discord bot REST API (per-user DM delivery).
        serviceCollection.AddHttpClient<IDiscordDmClient, DiscordDmClient>();

        // Typed HttpClient for Discord OAuth2 ("Login with Discord" connect flow).
        serviceCollection.AddHttpClient<IDiscordOAuthClient, DiscordOAuthClient>();

        // Typed HttpClient for the WhatsApp Business Cloud API.
        serviceCollection.AddHttpClient<IWhatsAppCloudApiClient, WhatsAppCloudApiClient>();

        // Named HttpClient used by the Telegram linking poller (see hosted service below).
        serviceCollection.AddHttpClient(nameof(TelegramLinkingService));

        // GitHub release checker caches its result in-memory across calls, so it must be a
        // singleton (not the transient instance AddHttpClient<T,T> would create) — built from
        // a named HttpClient via the factory instead.
        serviceCollection.AddHttpClient(nameof(GitHubReleaseChecker));
        serviceCollection.AddSingleton<IGitHubReleaseChecker>(sp => new GitHubReleaseChecker(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GitHubReleaseChecker)),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GitHubReleaseChecker>>()));

        // --- Persistent Stores ---
        // JSON-backed store for per-user notifications.
        serviceCollection.AddSingleton<INotificationStore, JsonNotificationStore>();

        // JSON-backed store for Seerr request snapshots (change detection).
        serviceCollection.AddSingleton<IRequestSnapshotStore, JsonRequestSnapshotStore>();

        // JSON-backed store for Telegram/Discord channel bindings.
        serviceCollection.AddSingleton<IUserChannelStore, JsonUserChannelStore>();

        // JSON-backed store for per-user notification preferences.
        serviceCollection.AddSingleton<IUserPreferenceStore, JsonUserPreferenceStore>();

        // --- In-Memory Stores ---
        // In-memory store tracking notification deduplication state.
        serviceCollection.AddSingleton<INotificationDeduplicationStore, NotificationDeduplicationStore>();

        // In-memory store tracking download progress from Sonarr/Radarr.
        serviceCollection.AddSingleton<IDownloadProgressStore, DownloadProgressStore>();

        // --- Core Services ---
        // Orchestrates notification routing across all configured channels.
        serviceCollection.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

        // Manages media request lifecycle with Seerr.
        serviceCollection.AddSingleton<IMediaRequestService, MediaRequestService>();

        // Coordinates polling and event handling for Sonarr/Radarr instances.
        serviceCollection.AddSingleton<IArrSyncService, ArrSyncService>();

        // Shared state between the Telegram linking poller and the admin "Detect
        // automatically" action — see TelegramActivityStore.cs.
        serviceCollection.AddSingleton<ITelegramActivityStore, TelegramActivityStore>();

        // --- Background Service ---
        // Long-running hosted service that drives periodic polling and event processing.
        serviceCollection.AddHostedService<JellyNotifyBackgroundService>();

        // Short-interval poller that completes the "Connect Telegram" deep link flow.
        serviceCollection.AddHostedService<TelegramLinkingService>();

        // --- Web injection ---
        // Injects the bell/panel <script> tag into jellyfin-web's index.html at
        // request time. See NOTICE.md for attribution (adapted from Jellyfin Enhanced).
        serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();
    }
}
