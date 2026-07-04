using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify.Store;

/// <summary>
/// Provides persistence for user notification events.
/// All operations that accept a userId parameter are scoped to that specific Jellyfin user.
/// </summary>
public interface INotificationStore
{
    /// <summary>
    /// Gets all notifications for the specified user, ordered by most recent first.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <returns>A list of notification events for the user.</returns>
    Task<IReadOnlyList<NotificationEvent>> GetByUserAsync(string userId);

    /// <summary>
    /// Adds a single notification event to the store.
    /// </summary>
    /// <param name="notification">The notification event to add.</param>
    Task AddAsync(NotificationEvent notification);

    /// <summary>
    /// Adds multiple notification events to the store in a single operation.
    /// </summary>
    /// <param name="notifications">The notification events to add.</param>
    Task AddRangeAsync(IEnumerable<NotificationEvent> notifications);

    /// <summary>
    /// Marks all unread notifications for the specified user as read.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    Task MarkAllReadAsync(string userId);

    /// <summary>
    /// Removes all notifications for the specified user.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    Task ClearAsync(string userId);

    /// <summary>
    /// Gets the count of unread notifications for the specified user.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <returns>The number of unread notifications.</returns>
    Task<int> GetUnreadCountAsync(string userId);

    /// <summary>
    /// Removes notifications older than the specified retention period across all users.
    /// </summary>
    /// <param name="retentionDays">The number of days to retain notifications.</param>
    Task PurgeOldAsync(int retentionDays);

    /// <summary>
    /// Gets the total count of notifications for the specified user.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <returns>The total number of notifications.</returns>
    Task<int> GetTotalCountAsync(string userId);

    /// <summary>
    /// Gets the total count of notifications stored across all users. Used only
    /// by the admin Diagnostics view — never exposed to non-admin endpoints.
    /// </summary>
    /// <returns>The total number of notifications stored.</returns>
    Task<int> GetTotalCountAllUsersAsync();
}
