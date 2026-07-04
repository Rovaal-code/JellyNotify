using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify.Store;

/// <summary>
/// Provides persistence for Seerr request snapshots used for change detection.
/// </summary>
public interface IRequestSnapshotStore
{
    /// <summary>
    /// Gets all stored request snapshots.
    /// </summary>
    /// <returns>A list of all request snapshots.</returns>
    Task<IReadOnlyList<RequestSnapshot>> GetAllAsync();

    /// <summary>
    /// Gets a request snapshot by its Seerr request ID.
    /// </summary>
    /// <param name="seerrRequestId">The Seerr request ID.</param>
    /// <returns>The matching snapshot, or null if not found.</returns>
    Task<RequestSnapshot?> GetBySeerrRequestIdAsync(int seerrRequestId);

    /// <summary>
    /// Inserts or updates a request snapshot. Matching is done by <see cref="RequestSnapshot.SeerrRequestId"/>.
    /// </summary>
    /// <param name="snapshot">The snapshot to upsert.</param>
    Task UpsertAsync(RequestSnapshot snapshot);

    /// <summary>
    /// Removes a request snapshot by its internal ID.
    /// </summary>
    /// <param name="id">The internal snapshot ID.</param>
    Task RemoveAsync(string id);

    /// <summary>
    /// Checks whether the initial baseline snapshot has been completed.
    /// </summary>
    /// <returns>True if the baseline has been completed.</returns>
    Task<bool> HasBaselineAsync();

    /// <summary>
    /// Marks the initial baseline snapshot as complete.
    /// Subsequent calls to <see cref="HasBaselineAsync"/> will return true.
    /// </summary>
    Task SetBaselineCompleteAsync();

    /// <summary>
    /// Clears all stored snapshots and the baseline-complete flag, so the next
    /// sync cycle treats every currently-existing request as a fresh baseline
    /// (i.e. does not notify for pre-existing items, but will notify on the
    /// next real change).
    /// </summary>
    Task ResetBaselineAsync();
}
