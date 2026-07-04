using System.Text.Json;
using Jellyfin.Plugin.JellyNotify.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Store;

/// <summary>
/// Thread-safe, JSON-file-backed request snapshot store.
/// Tracks Seerr request state for change detection and notification generation.
/// Uses a separate flag file to indicate baseline completion.
/// </summary>
public sealed class JsonRequestSnapshotStore : IRequestSnapshotStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly string _baselineFlagPath;
    private readonly ILogger<JsonRequestSnapshotStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private List<RequestSnapshot>? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRequestSnapshotStore"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public JsonRequestSnapshotStore(ILogger<JsonRequestSnapshotStore> logger)
    {
        _logger = logger;
        var dataPath = Plugin.Instance!.DataFolderPath;
        _filePath = Path.Combine(dataPath, "request-snapshots.json");
        _baselineFlagPath = Path.Combine(dataPath, "baseline-complete.flag");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RequestSnapshot>> GetAllAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all.AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RequestSnapshot?> GetBySeerrRequestIdAsync(int seerrRequestId)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all.FirstOrDefault(s => s.SeerrRequestId == seerrRequestId);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(RequestSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var existingIndex = all.FindIndex(s => s.SeerrRequestId == snapshot.SeerrRequestId);

            if (existingIndex >= 0)
            {
                all[existingIndex] = snapshot;
            }
            else
            {
                all.Add(snapshot);
            }

            await SaveAsync(all).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var before = all.Count;
            all.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal));

            if (all.Count != before)
            {
                await SaveAsync(all).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public Task<bool> HasBaselineAsync()
    {
        return Task.FromResult(File.Exists(_baselineFlagPath));
    }

    /// <inheritdoc />
    public async Task SetBaselineCompleteAsync()
    {
        var directory = Path.GetDirectoryName(_baselineFlagPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            _baselineFlagPath,
            $"Baseline completed at {DateTime.UtcNow:O}").ConfigureAwait(false);

        _logger.LogInformation("Baseline snapshot marked as complete");
    }

    /// <inheritdoc />
    public async Task ResetBaselineAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveAsync(new List<RequestSnapshot>()).ConfigureAwait(false);

            if (File.Exists(_baselineFlagPath))
            {
                File.Delete(_baselineFlagPath);
            }

            _logger.LogInformation("Request snapshot baseline reset — next sync will not notify pre-existing requests");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }

    /// <summary>
    /// Lazily loads the snapshot list from disk into the in-memory cache.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private async Task<List<RequestSnapshot>> LoadAsync()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_filePath))
        {
            _cache = new List<RequestSnapshot>();
            return _cache;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            _cache = JsonSerializer.Deserialize<List<RequestSnapshot>>(json, SerializerOptions)
                     ?? new List<RequestSnapshot>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize {File}; starting with empty snapshot list", _filePath);
            _cache = new List<RequestSnapshot>();
        }

        return _cache;
    }

    /// <summary>
    /// Persists the snapshot list to disk and updates the in-memory cache.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private async Task SaveAsync(List<RequestSnapshot> snapshots)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(snapshots, SerializerOptions);
        await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
        _cache = snapshots;
    }
}
