using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// Provides access to the Sonarr API.
/// </summary>
public interface ISonarrApiClient
{
    /// <summary>Tests the Sonarr connection and returns the system status.</summary>
    Task<ArrSystemStatus?> TestConnectionAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets all series from Sonarr.</summary>
    Task<IReadOnlyList<ArrSeries>> GetAllSeriesAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets a series by its ID.</summary>
    Task<ArrSeries?> GetSeriesByIdAsync(string serverUrl, string apiKey, int seriesId, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets the current download queue from Sonarr.</summary>
    Task<ArrQueueResponse?> GetQueueAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets the imported episode files for a series, carrying quality + audio/subtitle mediainfo not available from Seerr's own status.</summary>
    Task<IReadOnlyList<ArrEpisodeFile>> GetEpisodeFilesAsync(string serverUrl, string apiKey, int seriesId, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets download history from Sonarr.</summary>
    Task<ArrHistoryResponse?> GetHistoryAsync(string serverUrl, string apiKey, int page = 1, int pageSize = 50, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets every configured Connect notification from Sonarr.</summary>
    Task<IReadOnlyList<ArrNotificationResource>> GetNotificationsAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets the Connect notification schema templates (one per implementation, e.g. "Webhook").</summary>
    Task<IReadOnlyList<ArrNotificationResource>> GetNotificationSchemasAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Creates a new Connect notification in Sonarr.</summary>
    Task<(ArrNotificationResource? Created, string? Error)> CreateNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource notification, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Asks Sonarr to send a real test call for a notification — works even before it's been created (id=0), so this can confirm webhook deliverability ahead of actually creating anything.</summary>
    Task<(bool Success, string? Error)> TestNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource candidate, bool ignoreSsl = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides access to the Radarr API.
/// </summary>
public interface IRadarrApiClient
{
    /// <summary>Tests the Radarr connection and returns the system status.</summary>
    Task<ArrSystemStatus?> TestConnectionAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets all movies from Radarr.</summary>
    Task<IReadOnlyList<ArrMovie>> GetAllMoviesAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets a movie by its ID.</summary>
    Task<ArrMovie?> GetMovieByIdAsync(string serverUrl, string apiKey, int movieId, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets the current download queue from Radarr.</summary>
    Task<ArrQueueResponse?> GetQueueAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets the imported file(s) for a movie, carrying quality + audio/subtitle mediainfo not available from Seerr's own status.</summary>
    Task<IReadOnlyList<ArrMovieFile>> GetMovieFilesAsync(string serverUrl, string apiKey, int movieId, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets download history from Radarr.</summary>
    Task<ArrHistoryResponse?> GetHistoryAsync(string serverUrl, string apiKey, int page = 1, int pageSize = 50, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets every configured Connect notification from Radarr.</summary>
    Task<IReadOnlyList<ArrNotificationResource>> GetNotificationsAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Gets the Connect notification schema templates (one per implementation, e.g. "Webhook").</summary>
    Task<IReadOnlyList<ArrNotificationResource>> GetNotificationSchemasAsync(string serverUrl, string apiKey, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Creates a new Connect notification in Radarr.</summary>
    Task<(ArrNotificationResource? Created, string? Error)> CreateNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource notification, bool ignoreSsl = false, CancellationToken cancellationToken = default);

    /// <summary>Asks Radarr to send a real test call for a notification — works even before it's been created (id=0), so this can confirm webhook deliverability ahead of actually creating anything.</summary>
    Task<(bool Success, string? Error)> TestNotificationAsync(string serverUrl, string apiKey, ArrNotificationResource candidate, bool ignoreSsl = false, CancellationToken cancellationToken = default);
}
