using System.Collections.Concurrent;
using System.Text.Json;
using Jellyfin.Plugin.JellyNotify.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Store;

/// <summary>
/// JSON-file-backed implementation of <see cref="IDownloadProgressStore"/>.
/// <para>
/// Deliberately not in-memory. Every flag in <see cref="DownloadProgressState"/> exists to stop
/// a notification being sent twice, so state that dies with the process means Jellyfin restarts
/// re-announce whatever is currently downloading, and re-warn about the same stuck download
/// forever. The file is small — one entry per in-flight download per subscribed user, pruned as
/// soon as a download leaves the queue.
/// </para>
/// </summary>
public sealed class JsonDownloadProgressStore : IDownloadProgressStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, DownloadProgressState> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;
    private readonly ILogger<JsonDownloadProgressStore> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    private bool _loaded;
    private bool _dirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonDownloadProgressStore"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="filePath">Overrides the on-disk location. Only supplied by tests; dependency injection leaves it null so the plugin's own data folder is used.</param>
    public JsonDownloadProgressStore(ILogger<JsonDownloadProgressStore> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? Path.Combine(Plugin.Instance!.DataFolderPath, "download-progress.json");
    }

    /// <inheritdoc />
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;

            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, DownloadProgressState>>(json, SerializerOptions);
            if (loaded is null)
            {
                return;
            }

            foreach (var (key, state) in loaded)
            {
                _cache[key] = state;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Starting from empty is the safe failure mode: at worst the current stage of each
            // in-flight download is announced once more. Refusing to poll would be worse.
            _logger.LogError(ex, "Failed to read {File}; starting with empty download progress state", _filePath);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public DownloadProgressState? Get(string downloadKey) =>
        _cache.TryGetValue(downloadKey, out var state) ? state : null;

    /// <inheritdoc />
    public void Set(string downloadKey, DownloadProgressState state)
    {
        // A download sitting at the same percentage produces an identical state every cycle.
        // Caching it unconditionally would be correct but would rewrite the file every minute
        // for as long as anything is downloading, so only a real change marks it dirty.
        if (_cache.TryGetValue(downloadKey, out var existing) && existing.Matches(state))
        {
            return;
        }

        _cache[downloadKey] = state;
        _dirty = true;
    }

    /// <inheritdoc />
    public void Remove(string downloadKey)
    {
        if (_cache.TryRemove(downloadKey, out _))
        {
            _dirty = true;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllKeys() => _cache.Keys.ToList().AsReadOnly();

    /// <inheritdoc />
    public async Task FlushAsync()
    {
        if (!_dirty)
        {
            return;
        }

        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                _cache.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                SerializerOptions);
            await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left dirty on purpose so the next cycle retries the write.
            _dirty = true;
            _logger.LogError(ex, "Failed to write {File}; download progress state was not persisted", _filePath);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _ioLock.Dispose();
}
