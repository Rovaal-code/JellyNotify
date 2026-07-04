using System.Text.Json;
using Jellyfin.Plugin.JellyNotify.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Store;

/// <summary>
/// Thread-safe, JSON-file-backed user notification preference store.
/// Returns sensible defaults (all notifications enabled) when no preferences
/// have been saved for a user.
/// </summary>
public sealed class JsonUserPreferenceStore : IUserPreferenceStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly ILogger<JsonUserPreferenceStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private List<UserNotificationPreference>? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonUserPreferenceStore"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public JsonUserPreferenceStore(ILogger<JsonUserPreferenceStore> logger)
    {
        _logger = logger;
        var dataPath = Plugin.Instance!.DataFolderPath;
        _filePath = Path.Combine(dataPath, "preferences.json");
    }

    /// <inheritdoc />
    public async Task<UserNotificationPreference> GetByUserAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var existing = all.FirstOrDefault(p =>
                string.Equals(p.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                return existing;
            }

            // Return defaults: in-app UI enabled. External channels have no separate
            // on/off flag anymore — being connected (see UserChannelBinding) is the
            // only switch, so there's nothing to default here for Telegram/Discord/WhatsApp.
            return new UserNotificationPreference
            {
                JellyfinUserId = userId,
                NotifyOnRequest = true,
                NotifyOnApproval = true,
                NotifyOnDownload = true,
                NotifyOnAvailable = true,
                NotifyOnIssue = true,
                NotifyOnPartiallyAvailable = true,
                JellyfinUiEnabled = true,
                SoundEnabled = true,
                Language = "auto"
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(UserNotificationPreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var existingIndex = all.FindIndex(p =>
                string.Equals(p.JellyfinUserId, preference.JellyfinUserId, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                all[existingIndex] = preference;
            }
            else
            {
                all.Add(preference);
            }

            await SaveAsync(all).ConfigureAwait(false);
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
    /// Lazily loads the preference list from disk into the in-memory cache.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private async Task<List<UserNotificationPreference>> LoadAsync()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_filePath))
        {
            _cache = new List<UserNotificationPreference>();
            return _cache;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            _cache = JsonSerializer.Deserialize<List<UserNotificationPreference>>(json, SerializerOptions)
                     ?? new List<UserNotificationPreference>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize {File}; starting with empty preferences", _filePath);
            _cache = new List<UserNotificationPreference>();
        }

        return _cache;
    }

    /// <summary>
    /// Persists the preference list to disk and updates the in-memory cache.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private async Task SaveAsync(List<UserNotificationPreference> preferences)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(preferences, SerializerOptions);
        await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
        _cache = preferences;
    }
}
