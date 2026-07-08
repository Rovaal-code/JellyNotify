using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Represents a download queue item from Sonarr or Radarr.
/// </summary>
public class ArrQueueItem
{
    /// <summary>Gets or sets the queue item ID.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the title of the download.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the current status (e.g., "downloading", "completed").</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the tracked download status (e.g., "ok", "warning", "error").</summary>
    [JsonPropertyName("trackedDownloadStatus")]
    public string? TrackedDownloadStatus { get; set; }

    /// <summary>Gets or sets the tracked download state (e.g., "downloading", "importPending").</summary>
    [JsonPropertyName("trackedDownloadState")]
    public string? TrackedDownloadState { get; set; }

    /// <summary>Gets or sets the error message, if any.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets the status messages with warnings or errors.</summary>
    [JsonPropertyName("statusMessages")]
    public List<ArrStatusMessage>? StatusMessages { get; set; }

    /// <summary>Gets or sets the remaining download size in bytes.</summary>
    [JsonPropertyName("sizeleft")]
    public double Sizeleft { get; set; }

    /// <summary>Gets or sets the total download size in bytes.</summary>
    [JsonPropertyName("size")]
    public double Size { get; set; }

    /// <summary>Gets or sets the estimated time remaining for the download.</summary>
    [JsonPropertyName("timeleft")]
    public string? Timeleft { get; set; }

    /// <summary>Gets or sets the Sonarr series ID (Sonarr only).</summary>
    [JsonPropertyName("seriesId")]
    public int? SeriesId { get; set; }

    /// <summary>Gets or sets the Sonarr episode ID (Sonarr only).</summary>
    [JsonPropertyName("episodeId")]
    public int? EpisodeId { get; set; }

    /// <summary>Gets or sets the Radarr movie ID (Radarr only).</summary>
    [JsonPropertyName("movieId")]
    public int? MovieId { get; set; }

    /// <summary>Gets or sets the download protocol (e.g., "usenet", "torrent").</summary>
    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    /// <summary>Gets or sets the download client's identifier for this download.</summary>
    [JsonPropertyName("downloadId")]
    public string? DownloadId { get; set; }

    /// <summary>Gets or sets the quality/profile of this download (e.g. "WEBDL-1080p"). Already part of the same queue response — no extra call needed.</summary>
    [JsonPropertyName("quality")]
    public ArrQueueQuality? Quality { get; set; }
}

/// <summary>Wraps the nested quality info Sonarr/Radarr's queue API returns for each item.</summary>
public class ArrQueueQuality
{
    /// <summary>Gets or sets the actual quality descriptor.</summary>
    [JsonPropertyName("quality")]
    public ArrQualityInfo? Quality { get; set; }
}

/// <summary>The quality name/resolution Sonarr/Radarr assigned to a release.</summary>
public class ArrQualityInfo
{
    /// <summary>Gets or sets the human-readable quality name (e.g. "WEBDL-1080p", "Bluray-2160p").</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the vertical resolution in pixels (e.g. 1080, 2160), if known.</summary>
    [JsonPropertyName("resolution")]
    public int? Resolution { get; set; }
}

/// <summary>
/// The audio/subtitle track summary Sonarr/Radarr expose on an imported file's
/// <c>mediaInfo</c>. Both are "/"-separated strings straight from the API (e.g.
/// "English/Spanish"), matching the shape the moviefile/episodefile endpoints return —
/// distinct from the webhook payload, which sends them as arrays.
/// </summary>
public class ArrFileMediaInfo
{
    /// <summary>Gets or sets the audio languages, "/"-separated (e.g. "English/Japanese").</summary>
    [JsonPropertyName("audioLanguages")]
    public string? AudioLanguages { get; set; }

    /// <summary>Gets or sets the subtitle languages, "/"-separated (e.g. "English/Spanish").</summary>
    [JsonPropertyName("subtitles")]
    public string? Subtitles { get; set; }
}

/// <summary>
/// An imported movie file from Radarr's <c>api/v3/moviefile?movieId=</c> endpoint — the
/// source of quality/audio/subtitle detail for a "media available" notification that was
/// driven by Seerr's own status poll (which carries none of it) rather than the *arr import
/// webhook.
/// </summary>
public class ArrMovieFile
{
    /// <summary>Gets or sets the file's quality wrapper (same nested shape as the queue's).</summary>
    [JsonPropertyName("quality")]
    public ArrQueueQuality? Quality { get; set; }

    /// <summary>Gets or sets the parent movie ID.</summary>
    [JsonPropertyName("movieId")]
    public int MovieId { get; set; }

    /// <summary>Gets or sets the date the file was added/imported, used to pick the newest.</summary>
    [JsonPropertyName("dateAdded")]
    public DateTime? DateAdded { get; set; }

    /// <summary>Gets or sets the audio/subtitle track summary.</summary>
    [JsonPropertyName("mediaInfo")]
    public ArrFileMediaInfo? MediaInfo { get; set; }
}

/// <summary>
/// An imported episode file from Sonarr's <c>api/v3/episodefile?seriesId=</c> endpoint. Same
/// role as <see cref="ArrMovieFile"/>, for series.
/// </summary>
public class ArrEpisodeFile
{
    /// <summary>Gets or sets the file's quality wrapper.</summary>
    [JsonPropertyName("quality")]
    public ArrQueueQuality? Quality { get; set; }

    /// <summary>Gets or sets the parent series ID.</summary>
    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    /// <summary>Gets or sets the season this file belongs to.</summary>
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the date the file was added/imported, used to pick the newest.</summary>
    [JsonPropertyName("dateAdded")]
    public DateTime? DateAdded { get; set; }

    /// <summary>Gets or sets the audio/subtitle track summary.</summary>
    [JsonPropertyName("mediaInfo")]
    public ArrFileMediaInfo? MediaInfo { get; set; }
}

/// <summary>
/// Represents a status message from the *arr download queue.
/// </summary>
public class ArrStatusMessage
{
    /// <summary>Gets or sets the status message title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the detailed messages.</summary>
    [JsonPropertyName("messages")]
    public List<string>? Messages { get; set; }
}

/// <summary>
/// Paged response from the *arr queue API.
/// </summary>
public class ArrQueueResponse
{
    /// <summary>Gets or sets the current page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>Gets or sets the number of items per page.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    /// <summary>Gets or sets the total number of records.</summary>
    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }

    /// <summary>Gets or sets the queue items on this page.</summary>
    [JsonPropertyName("records")]
    public List<ArrQueueItem> Records { get; set; } = new();
}

/// <summary>
/// Represents a history record from Sonarr or Radarr.
/// </summary>
public class ArrHistoryRecord
{
    /// <summary>Gets or sets the history record ID.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the event type (e.g., "grabbed", "downloadFolderImported").</summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Gets or sets the timestamp of the event.</summary>
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    /// <summary>Gets or sets the source title of the release.</summary>
    [JsonPropertyName("sourceTitle")]
    public string SourceTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the Sonarr series ID (Sonarr only).</summary>
    [JsonPropertyName("seriesId")]
    public int? SeriesId { get; set; }

    /// <summary>Gets or sets the Sonarr episode ID (Sonarr only).</summary>
    [JsonPropertyName("episodeId")]
    public int? EpisodeId { get; set; }

    /// <summary>Gets or sets the Radarr movie ID (Radarr only).</summary>
    [JsonPropertyName("movieId")]
    public int? MovieId { get; set; }

    /// <summary>Gets or sets additional data associated with the event.</summary>
    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; set; }

    /// <summary>Gets or sets the download client's identifier for this download.</summary>
    [JsonPropertyName("downloadId")]
    public string? DownloadId { get; set; }
}

/// <summary>
/// Paged response from the *arr history API.
/// </summary>
public class ArrHistoryResponse
{
    /// <summary>Gets or sets the current page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>Gets or sets the number of items per page.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    /// <summary>Gets or sets the total number of records.</summary>
    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }

    /// <summary>Gets or sets the history records on this page.</summary>
    [JsonPropertyName("records")]
    public List<ArrHistoryRecord> Records { get; set; } = new();
}

/// <summary>
/// Represents a TV series from Sonarr.
/// </summary>
public class ArrSeries
{
    /// <summary>Gets or sets the Sonarr series ID.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the series title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the TVDB identifier.</summary>
    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }

    /// <summary>Gets or sets the IMDb identifier.</summary>
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; }

    /// <summary>Gets or sets the TMDB identifier.</summary>
    [JsonPropertyName("tmdbId")]
    public int? TmdbId { get; set; }

    /// <summary>Gets or sets the file system path for the series.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the series is monitored.</summary>
    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }
}

/// <summary>
/// Represents a movie from Radarr.
/// </summary>
public class ArrMovie
{
    /// <summary>Gets or sets the Radarr movie ID.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the movie title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the TMDB identifier.</summary>
    [JsonPropertyName("tmdbId")]
    public int? TmdbId { get; set; }

    /// <summary>Gets or sets the IMDb identifier.</summary>
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; }

    /// <summary>Gets or sets the file system path for the movie.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the movie is monitored.</summary>
    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    /// <summary>Gets or sets a value indicating whether the movie file exists on disk.</summary>
    [JsonPropertyName("hasFile")]
    public bool HasFile { get; set; }

    /// <summary>Gets or sets the release year.</summary>
    [JsonPropertyName("year")]
    public int Year { get; set; }
}

/// <summary>
/// Represents an episode from Sonarr.
/// </summary>
public class ArrEpisode
{
    /// <summary>Gets or sets the Sonarr episode ID.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the parent series ID.</summary>
    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    /// <summary>Gets or sets the season number.</summary>
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number within the season.</summary>
    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    /// <summary>Gets or sets the episode title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the episode file exists on disk.</summary>
    [JsonPropertyName("hasFile")]
    public bool HasFile { get; set; }
}

/// <summary>
/// Represents the system status response from Sonarr or Radarr.
/// </summary>
public class ArrSystemStatus
{
    /// <summary>Gets or sets the application version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets the application name (e.g., "Sonarr", "Radarr").</summary>
    [JsonPropertyName("appName")]
    public string AppName { get; set; } = string.Empty;
}

/// <summary>
/// Represents a single configuration field within a Sonarr/Radarr notification (Connect)
/// resource, e.g. the "url" field of a Webhook implementation. <see cref="Value"/> is typed
/// as <c>object?</c> so a field's value (string, bool, number depending on the field) round-trips
/// through JSON unchanged when a schema template is cloned and re-submitted with only one field
/// overridden.
/// </summary>
public class ArrNotificationField
{
    /// <summary>Gets or sets the field name (e.g. "url", "method").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the field's value.</summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

/// <summary>
/// Represents a Sonarr/Radarr notification (Connect) resource — both the schema template
/// returned by <c>GET /api/v3/notification/schema</c> and the resource created via
/// <c>POST /api/v3/notification</c>. Deliberately models only <see cref="OnDownload"/> and
/// <see cref="OnUpgrade"/> — the two events JellyNotify's arr webhook actually processes — since
/// Sonarr/Radarr default any event flag not present in the request body to <c>false</c>.
/// </summary>
public class ArrNotificationResource
{
    /// <summary>
    /// Gets or sets the notification's ID. Sonarr/Radarr's own resource always sends this as a
    /// plain (non-nullable) int — 0 for schema templates and not-yet-created candidates, a real
    /// value for existing ones — so this must stay non-nullable: serializing it as JSON
    /// <c>null</c> (which a nullable int defaults to when left unset) makes Sonarr/Radarr's own
    /// model binder reject the request with "The JSON value could not be converted to
    /// System.Int32", since their side expects a plain int there, not a nullable one.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the display name of this Connect entry.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the implementation identifier (e.g. "Webhook").</summary>
    [JsonPropertyName("implementation")]
    public string Implementation { get; set; } = string.Empty;

    /// <summary>Gets or sets the implementation's display name.</summary>
    [JsonPropertyName("implementationName")]
    public string ImplementationName { get; set; } = string.Empty;

    /// <summary>Gets or sets the settings config contract name (e.g. "WebhookSettings").</summary>
    [JsonPropertyName("configContract")]
    public string ConfigContract { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this notification fires on download/import.</summary>
    [JsonPropertyName("onDownload")]
    public bool OnDownload { get; set; }

    /// <summary>Gets or sets a value indicating whether this notification fires on upgrade.</summary>
    [JsonPropertyName("onUpgrade")]
    public bool OnUpgrade { get; set; }

    /// <summary>Gets or sets a value indicating whether this notification fires the instant a release is grabbed — the only way to know "download started" without polling the queue.</summary>
    [JsonPropertyName("onGrab")]
    public bool OnGrab { get; set; }

    /// <summary>Gets or sets the implementation-specific configuration fields (e.g. url, method).</summary>
    [JsonPropertyName("fields")]
    public List<ArrNotificationField> Fields { get; set; } = new();

    /// <summary>Gets or sets the tag IDs associated with this notification.</summary>
    [JsonPropertyName("tags")]
    public List<int> Tags { get; set; } = new();
}

/// <summary>
/// One entry of the validation-failure array Sonarr/Radarr return when
/// <c>POST /api/v3/notification/test</c> fails (e.g. the webhook URL couldn't be reached).
/// </summary>
public class ArrValidationFailure
{
    /// <summary>Gets or sets the name of the field that failed validation.</summary>
    [JsonPropertyName("propertyName")]
    public string? PropertyName { get; set; }

    /// <summary>Gets or sets the human-readable failure reason.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
