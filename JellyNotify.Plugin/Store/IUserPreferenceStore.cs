using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify.Store;

/// <summary>
/// Provides persistence for per-user notification preferences.
/// </summary>
public interface IUserPreferenceStore
{
    /// <summary>
    /// Gets the notification preferences for the specified Jellyfin user.
    /// If no preferences have been saved, returns a default instance with all notifications enabled.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <returns>The user's notification preferences.</returns>
    Task<UserNotificationPreference> GetByUserAsync(string userId);

    /// <summary>
    /// Inserts or updates the notification preferences for a user.
    /// Matching is done by <see cref="UserNotificationPreference.JellyfinUserId"/>.
    /// </summary>
    /// <param name="preference">The preferences to upsert.</param>
    Task UpsertAsync(UserNotificationPreference preference);
}
