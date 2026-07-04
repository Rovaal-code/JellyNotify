using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Injects the JellyNotify client &lt;script&gt; tag into jellyfin-web's index.html
/// at request time, via ASP.NET middleware registered through <see cref="IStartupFilter"/>.
///
/// Adapted from Jellyfin Enhanced's ScriptInjectionStartupFilter (GPL-3.0) — see
/// NOTICE.md at the repository root for attribution. Jellyfin serves index.html as a
/// plain static file with no native script-injection hook, so the plugin injects its
/// own script by running middleware ahead of the static-file handler. This keeps
/// script injection self-contained and does not require writing to the web folder
/// (which is wiped on every jellyfin-web update and may not even be writable in
/// container deployments).
///
/// The filter is deliberately defensive and additive:
///   - only ever touches the web index.html response;
///   - idempotent: no-ops if the script tag is already present;
///   - on any error it serves the original response unchanged, never throwing
///     into the pipeline;
///   - can be disabled via the DisableScriptInjectionMiddleware config flag.
/// </summary>
public sealed class ScriptInjectionStartupFilter : IStartupFilter
{
    private readonly ILogger<ScriptInjectionStartupFilter> _logger;
    private int _loggedOnce;

    /// <summary>Initializes a new instance of the <see cref="ScriptInjectionStartupFilter"/> class.</summary>
    public ScriptInjectionStartupFilter(ILogger<ScriptInjectionStartupFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the middleware has successfully injected the script at least once.</summary>
    public static bool HasInjectedOnce { get; private set; }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Registered before the rest of the pipeline (next(app)) so this runs
            // outermost — stripping Accept-Encoding below then reliably yields an
            // uncompressed response we can read and rewrite.
            app.Use(InvokeAsync);
            next(app);
        };
    }

    private async Task InvokeAsync(HttpContext context, Func<Task> nextMw)
    {
        if (!IsIndexRequest(context.Request.Path.Value))
        {
            await nextMw().ConfigureAwait(false);
            return;
        }

        // Only GET produces a body we can rewrite. HEAD/OPTIONS/etc. must pass
        // straight through so the host emits correct headers.
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await nextMw().ConfigureAwait(false);
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config == null || config.DisableScriptInjectionMiddleware)
        {
            await nextMw().ConfigureAwait(false);
            return;
        }

        // Normalize the request so the static handler returns a complete, plain-text
        // 200 we can rewrite: drop Accept-Encoding (no compression) and Range/If-Range
        // (a 206 partial response would otherwise pass through un-injected with a wrong
        // total length).
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("Range");
        context.Request.Headers.Remove("If-Range");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await nextMw().ConfigureAwait(false);
        }
        catch
        {
            // A downstream failure is not ours to swallow. Discard the partially
            // buffered body and rethrow so the host's exception handler can still
            // render a clean error page.
            context.Response.Body = originalBody;
            throw;
        }

        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);

        var isHtml = context.Response.StatusCode == 200
            && (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false);

        if (!isHtml)
        {
            // 304, redirects, non-HTML — pass straight through unchanged.
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        string html;
        using (var reader = new StreamReader(buffer, Encoding.UTF8, true, 1024, leaveOpen: true))
        {
            html = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        try
        {
            var plugin = Plugin.Instance!;
            // Idempotency guard keyed on the controller endpoint, so we never
            // double-inject across restarts or reloads.
            var alreadyInjected = html.IndexOf("/JellyNotify/script", StringComparison.OrdinalIgnoreCase) >= 0;
            var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

            if (!alreadyInjected && bodyClose >= 0)
            {
                var tag = plugin.BuildScriptTag();
                html = html.Substring(0, bodyClose) + tag + "\n" + html.Substring(bodyClose);

                HasInjectedOnce = true;
                if (Interlocked.Exchange(ref _loggedOnce, 1) == 0)
                {
                    _logger.LogInformation("[JellyNotify] Web injection active — injected client script via request-time middleware (IStartupFilter).");
                }
            }
        }
        catch (Exception ex)
        {
            // Never break index.html — serve whatever we have.
            _logger.LogWarning(ex, "[JellyNotify] Script injection middleware error (serving original HTML)");
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html;charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        // The body changed, so any validators set by the static-file handler are
        // no longer valid; range requests on the rewritten document aren't supported
        // (Range requests are already stripped on the way in).
        context.Response.Headers.Remove("ETag");
        context.Response.Headers.Remove("Last-Modified");
        context.Response.Headers.Remove("Accept-Ranges");
        // Explicitly forbid caching this rewritten document. Without this, a browser
        // is free to decide on its own to keep serving a stale index.html — from
        // before a plugin update injected different content — past the point where a
        // normal refresh would otherwise have picked up the change. This doesn't fix
        // an already-open SPA tab (it never re-requests index.html at all until a
        // real reload), but it does close this separate, avoidable staleness path.
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        await originalBody.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    // Matches the web app shell however it is requested: bare "/web", "/web/"
    // (SPA serve), and explicit "/web/index.html". EndsWith keeps this correct
    // when Jellyfin is hosted under a base-url prefix (e.g. /jellyfin/web/).
    private static bool IsIndexRequest(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web", StringComparison.OrdinalIgnoreCase);
    }
}
