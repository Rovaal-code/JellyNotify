using System;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyNotify.Api;

/// <summary>
/// Serves JellyNotify's static web assets (the globally-injected client script and
/// its stylesheet) directly from embedded resources via controller routes.
///
/// These routes are intentionally NOT behind [Authorize]: the client script is
/// requested by index.html before a user logs in (same as Jellyfin core assets),
/// and the config page stylesheet is loaded by an unauthenticated iframe bootstrap
/// in some Jellyfin Web versions. None of the responses here contain configuration
/// data or secrets — only static JS/CSS — so this is not a privacy or security
/// concern (mirrors Jellyfin Enhanced's equivalent asset controller, see NOTICE.md).
/// </summary>
[ApiController]
[Route("JellyNotify")]
public sealed class WebAssetsController : ControllerBase
{
    private const string ResourcePrefix = "Jellyfin.Plugin.JellyNotify.Web.";

    /// <summary>Serves the client script injected globally into index.html.</summary>
    [HttpGet("script")]
    public ActionResult GetScript() => GetEmbeddedResource("jellynotify.js", "application/javascript");

    /// <summary>Serves the shared stylesheet for the bell/panel and the config page.</summary>
    [HttpGet("web/jellynotify.css")]
    public ActionResult GetCss() => GetEmbeddedResource("jellynotify.css", "text/css");

    /// <summary>Config-page stylesheet — same file as the bell/panel CSS, one source of truth.</summary>
    [HttpGet("Configuration/configPage.css")]
    public ActionResult GetConfigPageCss() => GetEmbeddedResource("jellynotify.css", "text/css");

    /// <summary>Config-page-only helper script (form binding, tabs, admin actions).</summary>
    [HttpGet("Configuration/configPage.js")]
    public ActionResult GetConfigPageScript() => GetEmbeddedResource("jellynotify.js", "application/javascript");

    /// <summary>
    /// Serves a bundled locale file (Web/locales/{code}.json) — shared by the
    /// bell/panel UI and the admin config page. The code is restricted to a safe
    /// character set before being used to build the embedded resource name.
    /// </summary>
    [HttpGet("web/locales/{code}.json")]
    public ActionResult GetLocale(string code)
    {
        if (string.IsNullOrEmpty(code) || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z-]{2,20}$"))
        {
            return NotFound();
        }

        return GetEmbeddedResource($"locales.{code}.json", "application/json");
    }

    private ActionResult GetEmbeddedResource(string fileName, string contentType)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache, must-revalidate";
        return File(stream, contentType);
    }
}
