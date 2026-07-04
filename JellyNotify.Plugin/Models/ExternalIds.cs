using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Contains external service identifiers for correlating media across TMDB, TVDB, IMDb, Sonarr, and Radarr.
/// </summary>
public class ExternalIds
{
    /// <summary>Gets or sets the TMDB (The Movie Database) identifier.</summary>
    [JsonPropertyName("tmdbId")]
    public string? TmdbId { get; set; }

    /// <summary>Gets or sets the TVDB (TheTVDB) identifier.</summary>
    [JsonPropertyName("tvdbId")]
    public string? TvdbId { get; set; }

    /// <summary>Gets or sets the IMDb identifier.</summary>
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; }

    /// <summary>Gets or sets the Sonarr series identifier.</summary>
    [JsonPropertyName("sonarrSeriesId")]
    public int? SonarrSeriesId { get; set; }

    /// <summary>Gets or sets the Radarr movie identifier.</summary>
    [JsonPropertyName("radarrMovieId")]
    public int? RadarrMovieId { get; set; }

    /// <summary>Gets or sets the season number, if applicable.</summary>
    [JsonPropertyName("seasonNumber")]
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number, if applicable.</summary>
    [JsonPropertyName("episodeNumber")]
    public int? EpisodeNumber { get; set; }
}
