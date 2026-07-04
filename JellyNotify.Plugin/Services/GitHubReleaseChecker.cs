using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>Result of checking the public GitHub repository for a newer release.</summary>
public sealed record GitHubReleaseCheckResult(bool UpdateAvailable, string? LatestVersion, string? ReleaseUrl, string? Error);

/// <summary>Checks the public JellyNotify GitHub repository for a newer release than the one currently running.</summary>
public interface IGitHubReleaseChecker
{
    /// <summary>Compares the latest published GitHub release tag against the running plugin version.</summary>
    Task<GitHubReleaseCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class GitHubReleaseChecker : IGitHubReleaseChecker
{
    private const string ApiUrl = "https://api.github.com/repos/Rovaal-code/JellyNotify/releases/latest";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly ILogger<GitHubReleaseChecker> _logger;
    private GitHubReleaseCheckResult? _cached;
    private DateTime _cachedAtUtc;

    /// <summary>Initializes a new instance of the <see cref="GitHubReleaseChecker"/> class.</summary>
    public GitHubReleaseChecker(HttpClient http, ILogger<GitHubReleaseChecker> logger)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("JellyNotify-Plugin");
        }

        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GitHubReleaseCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < CacheDuration)
        {
            return _cached;
        }

        var result = await FetchAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        _cached = result;
        _cachedAtUtc = DateTime.UtcNow;
        return result;
    }

    private async Task<GitHubReleaseCheckResult> FetchAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new GitHubReleaseCheckResult(false, null, null, $"GitHub API returned {(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<GitHubReleasePayload>(cancellationToken: cancellationToken).ConfigureAwait(false);
            var tag = payload?.TagName?.TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(tag) || !Version.TryParse(NormalizeVersion(tag), out var latestVersion))
            {
                return new GitHubReleaseCheckResult(false, null, null, "Could not parse the latest release tag.");
            }

            return new GitHubReleaseCheckResult(latestVersion > currentVersion, tag, payload?.HtmlUrl, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub release check failed");
            return new GitHubReleaseCheckResult(false, null, null, ex.Message);
        }
    }

    /// <summary>
    /// GitHub release tags here are 2-3 segments (e.g. "1.0.5"), but the running
    /// assembly version always has 4 (e.g. "1.0.5.0") — pad so <see cref="Version"/>
    /// comparison works instead of throwing on segment-count mismatch.
    /// </summary>
    private static string NormalizeVersion(string tag)
    {
        var parts = tag.Split('.');
        return parts.Length switch
        {
            1 => $"{tag}.0.0.0",
            2 => $"{tag}.0.0",
            3 => $"{tag}.0",
            _ => tag
        };
    }

    private sealed class GitHubReleasePayload
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
