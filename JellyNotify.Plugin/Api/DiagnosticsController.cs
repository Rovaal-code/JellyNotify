using Jellyfin.Plugin.JellyNotify.Services;
using Jellyfin.Plugin.JellyNotify.Store;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Api;

/// <summary>
/// Admin-only diagnostics for JellyNotify: web-injection status, sync health,
/// and manual sync/baseline-reset triggers. Everything here requires
/// administrator elevation — none of it is exposed to regular users.
/// </summary>
[ApiController]
[Route("JellyNotify/Admin")]
[Authorize(Policy = "RequiresElevation")]
[Produces("application/json")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IServerApplicationHost _serverApplicationHost;
    private readonly INotificationStore _notificationStore;
    private readonly IMediaRequestService _mediaRequestService;
    private readonly IArrSyncService _arrSyncService;
    private readonly IRequestSnapshotStore _snapshotStore;
    private readonly IGitHubReleaseChecker _releaseChecker;
    private readonly ILogger<DiagnosticsController> _logger;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticsController"/> class.</summary>
    public DiagnosticsController(
        IServerApplicationHost serverApplicationHost,
        INotificationStore notificationStore,
        IMediaRequestService mediaRequestService,
        IArrSyncService arrSyncService,
        IRequestSnapshotStore snapshotStore,
        IGitHubReleaseChecker releaseChecker,
        ILogger<DiagnosticsController> logger)
    {
        _serverApplicationHost = serverApplicationHost;
        _notificationStore = notificationStore;
        _mediaRequestService = mediaRequestService;
        _arrSyncService = arrSyncService;
        _snapshotStore = snapshotStore;
        _releaseChecker = releaseChecker;
        _logger = logger;
    }

    /// <summary>Gets plugin health/diagnostics for the admin config page.</summary>
    [HttpGet("diagnostics")]
    [ProducesResponseType(typeof(DiagnosticsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DiagnosticsResponse>> GetDiagnostics()
    {
        var plugin = Plugin.Instance!;
        var config = plugin.Configuration;

        var releaseCheck = await _releaseChecker.CheckAsync(plugin.Version).ConfigureAwait(false);

        var response = new DiagnosticsResponse
        {
            PluginVersion = plugin.Version.ToString(),
            WebInjectionActive = ScriptInjectionStartupFilter.HasInjectedOnce && !config.DisableScriptInjectionMiddleware,
            ServerVersion = _serverApplicationHost.ApplicationVersionString,
            LastSuccessfulSyncUtc = config.LastSuccessfulSyncUtc,
            LastSyncError = config.LastSyncError,
            TotalNotificationCount = await _notificationStore.GetTotalCountAllUsersAsync().ConfigureAwait(false),
            SeerrConfigured = config.SeerrSettings.Enabled && !string.IsNullOrWhiteSpace(config.SeerrSettings.ApiKey),
            SonarrInstanceCount = config.SonarrInstances.Count(i => i.Enabled),
            RadarrInstanceCount = config.RadarrInstances.Count(i => i.Enabled),
            UpdateAvailable = releaseCheck.UpdateAvailable,
            LatestVersion = releaseCheck.LatestVersion,
            ReleaseUrl = releaseCheck.ReleaseUrl,
            UpdateCheckError = releaseCheck.Error,
        };

        // See JsonCamelCase in AdminController.cs: Jellyfin's default MVC JSON
        // options serialize PascalCase, but jellynotify.js reads every field as
        // camelCase, so this is returned as explicitly-serialized raw JSON
        // content instead of via Ok(response).
        return Content(System.Text.Json.JsonSerializer.Serialize(response, JsonCamelCase.Options), "application/json");
    }

    /// <summary>Triggers an immediate Seerr + Sonarr/Radarr sync cycle (fire-and-forget).</summary>
    [HttpPost("sync-now")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult SyncNow()
    {
        _ = RunSyncNowAsync();
        return Accepted();
    }

    private async Task RunSyncNowAsync()
    {
        try
        {
            await _mediaRequestService.PollAndProcessAsync().ConfigureAwait(false);
            await _arrSyncService.PollAllAsync().ConfigureAwait(false);
            _logger.LogInformation("JellyNotify: manual sync-now completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyNotify: manual sync-now failed");
        }
    }

    /// <summary>Resets the Seerr request-snapshot baseline so the next sync treats current requests as the new starting point.</summary>
    [HttpPost("reset-baseline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetBaseline()
    {
        await _snapshotStore.ResetBaselineAsync().ConfigureAwait(false);
        _logger.LogInformation("JellyNotify: baseline reset by admin");
        return NoContent();
    }
}

/// <summary>Diagnostics DTO for the admin config page.</summary>
public sealed class DiagnosticsResponse
{
    /// <summary>Gets or sets the running plugin version.</summary>
    public string PluginVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the global web-injection middleware has successfully injected the script.</summary>
    public bool WebInjectionActive { get; set; }

    /// <summary>Gets or sets the Jellyfin server version.</summary>
    public string? ServerVersion { get; set; }

    /// <summary>Gets or sets the UTC timestamp of the last successful background sync.</summary>
    public DateTime? LastSuccessfulSyncUtc { get; set; }

    /// <summary>Gets or sets the last background sync error, if any.</summary>
    public string? LastSyncError { get; set; }

    /// <summary>Gets or sets the total number of notifications stored across all users.</summary>
    public int TotalNotificationCount { get; set; }

    /// <summary>Gets or sets a value indicating whether Seerr is enabled and has an API key configured.</summary>
    public bool SeerrConfigured { get; set; }

    /// <summary>Gets or sets the number of enabled Sonarr instances.</summary>
    public int SonarrInstanceCount { get; set; }

    /// <summary>Gets or sets the number of enabled Radarr instances.</summary>
    public int RadarrInstanceCount { get; set; }

    /// <summary>Gets or sets a value indicating whether a newer release is available on GitHub.</summary>
    public bool UpdateAvailable { get; set; }

    /// <summary>Gets or sets the latest release version found on GitHub, if any.</summary>
    public string? LatestVersion { get; set; }

    /// <summary>Gets or sets the URL of the latest GitHub release.</summary>
    public string? ReleaseUrl { get; set; }

    /// <summary>Gets or sets an error message if the GitHub release check failed (rate limit, network, etc.).</summary>
    public string? UpdateCheckError { get; set; }
}
