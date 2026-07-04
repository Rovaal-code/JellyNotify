namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Builds a full, publicly loadable poster image URL from the TMDB-relative path
/// Seerr's movie/tv detail endpoints return (e.g. "/abc123.jpg"). TMDB's image CDN
/// is public and needs no API key to load images from, unlike the Seerr/TMDB API
/// calls that fetched the path in the first place.
/// </summary>
public static class PosterUrlBuilder
{
    private const string ImageBase = "https://image.tmdb.org/t/p/w500";

    /// <summary>Builds the full poster URL, or null if there is no path to build from.</summary>
    public static string? Build(string? posterPath) =>
        string.IsNullOrWhiteSpace(posterPath) ? null : $"{ImageBase}{posterPath}";
}
