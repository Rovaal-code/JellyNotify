using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.JellyNotify.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// The main JellyNotify plugin class.
/// Registers the plugin with Jellyfin and serves the configuration web pages.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The unique plugin identifier.
    /// </summary>
    public static readonly Guid PluginGuid = Guid.Parse("d7e3f1a2-4b5c-6d8e-9f0a-1b2c3d4e5f6a");

    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{Plugin}"/> interface.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        Instance = this;
        _logger.LogInformation("JellyNotify plugin v{Version} loaded", Version);
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "JellyNotify";

    /// <inheritdoc />
    public override string Description => "Notification and media request management plugin for Jellyfin with Overseerr/Jellyseerr, Sonarr, and Radarr integration.";

    /// <inheritdoc />
    public override Guid Id => PluginGuid;

    /// <summary>
    /// Gets the assembly-qualified resource prefix for embedded resources.
    /// </summary>
    private static string ResourcePrefix => "Jellyfin.Plugin.JellyNotify";

    /// <summary>
    /// Cache-busting key: plugin version plus the DLL's last-write timestamp, so
    /// every build yields a distinct value even when the version is unchanged
    /// (local dev/testing). Falls back to the bare version if the assembly
    /// location can't be read (e.g. single-file hosting).
    /// </summary>
    internal string ScriptCacheKey
    {
        get
        {
            var version = Version?.ToString() ?? "unknown";
            try
            {
                var location = typeof(Plugin).Assembly.Location;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    var ticks = new FileInfo(location).LastWriteTimeUtc.Ticks;
                    return $"{version}-{ticks}";
                }
            }
            catch (IOException)
            {
                // Fall through to the bare version below.
            }
            catch (UnauthorizedAccessException)
            {
                // Fall through to the bare version below.
            }

            return version;
        }
    }

    /// <summary>
    /// Builds the &lt;script&gt; tag injected into index.html by
    /// <see cref="Services.ScriptInjectionStartupFilter"/>. Single source of truth so
    /// the injection middleware and any diagnostics reporting never drift.
    /// </summary>
    internal string BuildScriptTag()
    {
        var cacheKey = ScriptCacheKey;
        return $"<script plugin=\"{Name}\" version=\"{cacheKey}\" src=\"../JellyNotify/script?v={cacheKey}\" defer></script>";
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "jellynotify",
                DisplayName = "JellyNotify",
                EnableInMainMenu = true,
                MenuIcon = "notifications",
                EmbeddedResourcePath = $"{ResourcePrefix}.Configuration.configPage.html",
            },
        };
    }

    /// <summary>
    /// Updates the plugin configuration and saves it.
    /// </summary>
    /// <param name="configuration">The new configuration to apply.</param>
    public void SavePluginConfiguration(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        UpdateConfiguration(configuration);
        _logger.LogInformation("JellyNotify configuration updated");
    }
}
