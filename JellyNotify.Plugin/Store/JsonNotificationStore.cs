using System.Text.Json;
using Jellyfin.Plugin.JellyNotify.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Store;

/// <summary>
/// Thread-safe, JSON-file-backed notification store.
/// All read/write operations are scoped to the requesting Jellyfin user ID
/// to ensure strict user isolation.
/// </summary>
public sealed class JsonNotificationStore : INotificationStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly ILogger<JsonNotificationStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private List<NotificationEvent>? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonNotificationStore"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public JsonNotificationStore(ILogger<JsonNotificationStore> logger)
    {
        _logger = logger;
        var dataPath = Plugin.Instance!.DataFolderPath;
        _filePath = Path.Combine(dataPath, "notifications.json");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationEvent>> GetByUserAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all
                .Where(n => string.Equals(n.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.CreatedAt)
                .ToList()
                .AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task AddAsync(NotificationEvent notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            all.Add(notification);
            EnforceMaxPerUser(all, notification.JellyfinUserId);
            await SaveAsync(all).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<NotificationEvent> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        var items = notifications.ToList();
        if (items.Count == 0)
        {
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            all.AddRange(items);

            // Enforce limits for each affected user
            var affectedUsers = items.Select(n => n.JellyfinUserId).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var userId in affectedUsers)
            {
                EnforceMaxPerUser(all, userId);
            }

            await SaveAsync(all).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task MarkAllReadAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var modified = false;

            foreach (var notification in all)
            {
                if (string.Equals(notification.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase)
                    && !notification.IsRead)
                {
                    notification.IsRead = true;
                    notification.ReadAt = now;
                    modified = true;
                }
            }

            if (modified)
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
    public async Task ClearAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var before = all.Count;
            all.RemoveAll(n => string.Equals(n.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase));

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
    public async Task<int> GetUnreadCountAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all.Count(n =>
                string.Equals(n.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase)
                && !n.IsRead);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task PurgeOldAsync(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "Retention days must be positive.");
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var before = all.Count;
            all.RemoveAll(n => n.CreatedAt < cutoff);

            if (all.Count != before)
            {
                _logger.LogInformation(
                    "Purged {Count} notifications older than {Days} days",
                    before - all.Count,
                    retentionDays);
                await SaveAsync(all).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> GetTotalCountAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all.Count(n =>
                string.Equals(n.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> GetTotalCountAllUsersAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all.Count;
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
    /// Lazily loads the notification list from disk into the in-memory cache.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private async Task<List<NotificationEvent>> LoadAsync()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_filePath))
        {
            _cache = new List<NotificationEvent>();
            return _cache;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            _cache = JsonSerializer.Deserialize<List<NotificationEvent>>(json, SerializerOptions)
                     ?? new List<NotificationEvent>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize {File}; starting with empty notification list", _filePath);
            _cache = new List<NotificationEvent>();
        }

        return _cache;
    }

    /// <summary>
    /// Persists the notification list to disk and updates the in-memory cache.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private async Task SaveAsync(List<NotificationEvent> notifications)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(notifications, SerializerOptions);
        await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
        _cache = notifications;
    }

    /// <summary>
    /// Trims the oldest notifications for a given user when the per-user limit is exceeded.
    /// Uses MaxNotificationsPerUser from the plugin configuration.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private void EnforceMaxPerUser(List<NotificationEvent> all, string userId)
    {
        var config = Plugin.Instance!.Configuration;
        var max = config.MaxNotificationsPerUser;
        if (max <= 0)
        {
            return;
        }

        var userNotifications = all
            .Where(n => string.Equals(n.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.CreatedAt)
            .ToList();

        if (userNotifications.Count <= max)
        {
            return;
        }

        var toRemove = new HashSet<string>(
            userNotifications.Skip(max).Select(n => n.Id));

        all.RemoveAll(n =>
            string.Equals(n.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase)
            && toRemove.Contains(n.Id));

        _logger.LogDebug(
            "Trimmed {Count} excess notifications for user {UserId} (max {Max})",
            toRemove.Count,
            userId,
            max);
    }
}
