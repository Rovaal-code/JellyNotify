using System.Collections.Concurrent;

namespace Jellyfin.Plugin.JellyNotify;

/// <summary>
/// In-memory download progress tracker.
/// Keyed by "{instanceName}:{downloadId}" for unique identification across multiple *arr instances.
/// </summary>
public sealed class DownloadProgressStore : IDownloadProgressStore
{
    private readonly ConcurrentDictionary<string, string> _progress = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string? GetProgress(string downloadKey) =>
        _progress.TryGetValue(downloadKey, out var status) ? status : null;

    /// <inheritdoc />
    public void SetProgress(string downloadKey, string status) =>
        _progress[downloadKey] = status;

    /// <inheritdoc />
    public void Remove(string downloadKey) =>
        _progress.TryRemove(downloadKey, out _);

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllKeys() =>
        _progress.Keys.ToList().AsReadOnly();
}

/// <summary>
/// In-memory notification deduplication store.
/// Uses a time-based expiry to avoid duplicate notifications within a configurable window.
/// </summary>
public sealed class NotificationDeduplicationStore : INotificationDeduplicationStore
{
    private readonly ConcurrentDictionary<string, DateTime> _records = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsDuplicate(string key)
    {
        if (_records.TryGetValue(key, out var expiry))
        {
            if (DateTime.UtcNow < expiry)
            {
                return true;
            }

            // Expired — remove it
            _records.TryRemove(key, out _);
        }

        return false;
    }

    /// <inheritdoc />
    public void Record(string key, TimeSpan window) =>
        _records[key] = DateTime.UtcNow.Add(window);

    /// <inheritdoc />
    public void Clear() =>
        _records.Clear();
}
