using Jellyfin.Plugin.JellyNotify.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Fills in the quality/audio/subtitle detail for a "media available" notification that was
/// driven by Seerr's own status poll rather than the Sonarr/Radarr import webhook. Seerr
/// carries none of that detail, so this looks the imported file up directly in whichever
/// configured *arr instance owns the matching movie/series (correlated by external ID). The
/// webhook path already has this data inline and does not need it.
/// </summary>
public interface IArrMediaInfoLookup
{
    /// <summary>Looks up quality/audio/subtitle detail for the given media, or null if nothing usable is found.</summary>
    Task<ArrMediaInfoResult?> LookupAsync(string mediaType, ExternalIds? externalIds, CancellationToken cancellationToken = default);
}

/// <summary>The quality/audio/subtitle detail resolved from an imported *arr file.</summary>
public sealed record ArrMediaInfoResult(string? Quality, string? AudioLanguages, string? SubtitleLanguages)
{
    /// <summary>Gets a value indicating whether at least one field was resolved — a result with nothing in it is not worth using.</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Quality)
        || !string.IsNullOrWhiteSpace(AudioLanguages)
        || !string.IsNullOrWhiteSpace(SubtitleLanguages);
}

/// <inheritdoc />
public sealed class ArrMediaInfoLookup : IArrMediaInfoLookup
{
    private readonly ISonarrApiClient _sonarr;
    private readonly IRadarrApiClient _radarr;
    private readonly ILogger<ArrMediaInfoLookup> _logger;

    /// <summary>Initializes a new instance of the <see cref="ArrMediaInfoLookup"/> class.</summary>
    public ArrMediaInfoLookup(ISonarrApiClient sonarr, IRadarrApiClient radarr, ILogger<ArrMediaInfoLookup> logger)
    {
        _sonarr = sonarr;
        _radarr = radarr;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ArrMediaInfoResult?> LookupAsync(string mediaType, ExternalIds? externalIds, CancellationToken cancellationToken = default)
    {
        if (externalIds is null)
        {
            return null;
        }

        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return null;
        }

        try
        {
            return string.Equals(mediaType, "movie", StringComparison.OrdinalIgnoreCase)
                ? await LookupMovieAsync(config, externalIds, cancellationToken).ConfigureAwait(false)
                : await LookupSeriesAsync(config, externalIds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A lookup failure must never sink the notification itself — it just goes out
            // without the extra quality fields, exactly as it did before this existed.
            _logger.LogWarning(ex, "Could not resolve *arr media info for {MediaType}", mediaType);
            return null;
        }
    }

    private async Task<ArrMediaInfoResult?> LookupMovieAsync(Configuration.PluginConfiguration config, ExternalIds ids, CancellationToken cancellationToken)
    {
        foreach (var instance in config.RadarrInstances.Where(i => i.Enabled))
        {
            var movies = await _radarr.GetAllMoviesAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
            var movie = movies.FirstOrDefault(m => MatchesMovie(m, ids));
            if (movie is null || !movie.HasFile)
            {
                continue;
            }

            var files = await _radarr.GetMovieFilesAsync(instance.ServerUrl, instance.ApiKey, movie.Id, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
            var newest = files.OrderByDescending(f => f.DateAdded ?? DateTime.MinValue).FirstOrDefault();
            var result = Extract(newest?.Quality, newest?.MediaInfo);
            if (result.HasAny)
            {
                return result;
            }
        }

        return null;
    }

    private async Task<ArrMediaInfoResult?> LookupSeriesAsync(Configuration.PluginConfiguration config, ExternalIds ids, CancellationToken cancellationToken)
    {
        foreach (var instance in config.SonarrInstances.Where(i => i.Enabled))
        {
            var series = await _sonarr.GetAllSeriesAsync(instance.ServerUrl, instance.ApiKey, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
            var match = series.FirstOrDefault(s => MatchesSeries(s, ids));
            if (match is null)
            {
                continue;
            }

            var files = await _sonarr.GetEpisodeFilesAsync(instance.ServerUrl, instance.ApiKey, match.Id, instance.IgnoreSslErrors, cancellationToken).ConfigureAwait(false);
            // The most recently imported episode is the best representative of "what just
            // became available" — a series can hold a mix of qualities across old seasons.
            var newest = files.OrderByDescending(f => f.DateAdded ?? DateTime.MinValue).FirstOrDefault();
            var result = Extract(newest?.Quality, newest?.MediaInfo);
            if (result.HasAny)
            {
                return result;
            }
        }

        return null;
    }

    private static bool MatchesMovie(ArrMovie movie, ExternalIds ids) =>
        (movie.TmdbId.HasValue && ids.TmdbId == movie.TmdbId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        || (!string.IsNullOrWhiteSpace(movie.ImdbId) && string.Equals(ids.ImdbId, movie.ImdbId, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesSeries(ArrSeries series, ExternalIds ids) =>
        (series.TvdbId.HasValue && ids.TvdbId == series.TvdbId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        || (series.TmdbId.HasValue && ids.TmdbId == series.TmdbId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        || (!string.IsNullOrWhiteSpace(series.ImdbId) && string.Equals(ids.ImdbId, series.ImdbId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pulls quality/audio/subtitle detail out of an imported file, normalizing the *arr
    /// file API's "/"-separated language lists into the comma-separated form the rest of the
    /// pipeline (and the card's language-capping) already expects. Internal for test visibility.
    /// </summary>
    internal static ArrMediaInfoResult Extract(ArrQueueQuality? quality, ArrFileMediaInfo? mediaInfo) =>
        new(
            NullIfBlank(quality?.Quality?.Name),
            NormalizeLanguages(mediaInfo?.AudioLanguages),
            NormalizeLanguages(mediaInfo?.Subtitles));

    private static string? NormalizeLanguages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // *arr lists one entry per track, so the same language repeats (e.g. "spa/spa/spa"
        // for three Spanish audio tracks). Collapse to distinct languages, first-seen order.
        var distinct = new List<string>();
        foreach (var part in raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!distinct.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                distinct.Add(part);
            }
        }

        return distinct.Count > 0 ? string.Join(", ", distinct) : null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
