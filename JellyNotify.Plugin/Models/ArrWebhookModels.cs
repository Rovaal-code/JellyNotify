using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Root payload Sonarr/Radarr's "Connect" webhook sends on events like Download/Upgrade.
/// Only the subset of fields JellyNotify actually reads is modeled here — Sonarr and
/// Radarr's real payloads carry many more (release info, download client, etc.).
/// </summary>
public sealed class ArrWebhookPayload
{
    /// <summary>Gets or sets the event type (e.g. "Download", "Upgrade", "Grab", "Test", "HealthIssue"). Only Download/Upgrade/Grab are processed — everything else is ignored.</summary>
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    /// <summary>Gets or sets the series info (Sonarr events only).</summary>
    [JsonPropertyName("series")]
    public ArrWebhookSeries? Series { get; set; }

    /// <summary>Gets or sets the movie info (Radarr events only).</summary>
    [JsonPropertyName("movie")]
    public ArrWebhookMovie? Movie { get; set; }

    /// <summary>Gets or sets the episode(s) this event applies to (Sonarr events only).</summary>
    [JsonPropertyName("episodes")]
    public List<ArrWebhookEpisode>? Episodes { get; set; }

    /// <summary>Gets or sets the imported episode file, including embedded MediaInfo (Sonarr events only).</summary>
    [JsonPropertyName("episodeFile")]
    public ArrWebhookMediaFile? EpisodeFile { get; set; }

    /// <summary>Gets or sets the imported movie file, including embedded MediaInfo (Radarr events only).</summary>
    [JsonPropertyName("movieFile")]
    public ArrWebhookMediaFile? MovieFile { get; set; }

    /// <summary>Gets or sets the release that was grabbed (Grab events only) — no file exists yet, so this is the only quality info available at this point.</summary>
    [JsonPropertyName("release")]
    public ArrWebhookRelease? Release { get; set; }
}

/// <summary>The release info embedded in a Grab event — unlike Download/Upgrade's file quality, this is a plain string, not a nested quality object.</summary>
public sealed class ArrWebhookRelease
{
    /// <summary>Gets or sets the quality/profile name (e.g. "WEBDL-1080p").</summary>
    [JsonPropertyName("quality")]
    public string? Quality { get; set; }
}

/// <summary>Series info embedded in a Sonarr webhook payload.</summary>
public sealed class ArrWebhookSeries
{
    /// <summary>Gets or sets the series title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the TVDB identifier, used to correlate to a <see cref="RequestSnapshot"/>.</summary>
    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; }
}

/// <summary>Movie info embedded in a Radarr webhook payload.</summary>
public sealed class ArrWebhookMovie
{
    /// <summary>Gets or sets the movie title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the TMDB identifier, used to correlate to a <see cref="RequestSnapshot"/>.</summary>
    [JsonPropertyName("tmdbId")]
    public int? TmdbId { get; set; }
}

/// <summary>A single episode referenced by a Sonarr webhook payload.</summary>
public sealed class ArrWebhookEpisode
{
    /// <summary>Gets or sets the season number.</summary>
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }
}

/// <summary>Quality info embedded in an episode/movie file.</summary>
public sealed class ArrWebhookQualityWrapper
{
    /// <summary>Gets or sets the inner quality descriptor.</summary>
    [JsonPropertyName("quality")]
    public ArrWebhookQuality? Quality { get; set; }
}

/// <summary>The quality name itself.</summary>
public sealed class ArrWebhookQuality
{
    /// <summary>Gets or sets the quality/profile name (e.g. "WEBDL-1080p").</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>An imported episode or movie file, as embedded in a Sonarr/Radarr webhook payload.</summary>
public sealed class ArrWebhookMediaFile
{
    /// <summary>Gets or sets the quality this file was imported at.</summary>
    [JsonPropertyName("quality")]
    public ArrWebhookQualityWrapper? Quality { get; set; }

    /// <summary>Gets or sets the file's embedded media info — this is what supplies Audio/Subtítulos for free, without an extra API call.</summary>
    [JsonPropertyName("mediaInfo")]
    public ArrWebhookMediaInfo? MediaInfo { get; set; }
}

/// <summary>Audio/subtitle track info embedded in a Sonarr/Radarr webhook's file payload.</summary>
public sealed class ArrWebhookMediaInfo
{
    /// <summary>Gets or sets the audio track languages.</summary>
    [JsonPropertyName("audioLanguages")]
    public List<string>? AudioLanguages { get; set; }

    /// <summary>Gets or sets the subtitle track languages.</summary>
    [JsonPropertyName("subtitles")]
    public List<string>? Subtitles { get; set; }
}
