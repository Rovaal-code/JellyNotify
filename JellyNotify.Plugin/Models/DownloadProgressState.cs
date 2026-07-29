using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Everything the *arr queue poll remembers about one tracked download, for one user.
/// <para>
/// This used to be a single stage string compared against the next poll's stage, which was
/// enough while the only rule was "notify when the stage changes". It isn't enough for the
/// current rules, all of which need memory beyond the last observation:
/// </para>
/// <list type="bullet">
/// <item>"Downloading" may only follow a "Download started" that actually went out, so
/// <see cref="StartedNotified"/> has to be sticky — a download that warns and then recovers
/// must not re-announce that it started.</item>
/// <item>A stalled warning needs a consecutive-cycle count (<see cref="WarningStreak"/>), since
/// a seedless torrent flaps in and out of *arr's warning state and a bare stage comparison
/// notified on every flap.</item>
/// <item>The stall watchdog needs to know when the percentage last actually moved
/// (<see cref="LastMovementAtUtc"/>) and must stay quiet forever afterwards
/// (<see cref="StalledNotified"/>).</item>
/// </list>
/// <para>
/// Persisted to disk rather than held in memory: every flag here exists to suppress a repeat
/// notification, so losing them on a Jellyfin restart would re-announce the current stage of
/// everything still in the queue — and re-warn about the same zombie download on every restart,
/// which is the exact thing the watchdog is meant to stop. The dedup window can't cover it,
/// since restarts are hours apart.
/// </para>
/// </summary>
public sealed class DownloadProgressState
{
    /// <summary>Gets or sets the last computed stage token (e.g. <c>downloading:started</c>). Reported as the notification's previous/new state and useful when reading the file by hand; the flags below, not this, decide what gets sent.</summary>
    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether "Download started" has been sent for this download.</summary>
    [JsonPropertyName("startedNotified")]
    public bool StartedNotified { get; set; }

    /// <summary>Gets or sets a value indicating whether the mid-download "Downloading" ping has been sent.</summary>
    [JsonPropertyName("halfNotified")]
    public bool HalfNotified { get; set; }

    /// <summary>Gets or sets a value indicating whether an *arr-reported warning has been sent. Sticky, so a sustained warning is announced once rather than on every flap.</summary>
    [JsonPropertyName("warningNotified")]
    public bool WarningNotified { get; set; }

    /// <summary>Gets or sets a value indicating whether a failure has been sent.</summary>
    [JsonPropertyName("failedNotified")]
    public bool FailedNotified { get; set; }

    /// <summary>Gets or sets a value indicating whether the stall watchdog has fired. Terminal: nothing further is sent for this download unless it starts moving again.</summary>
    [JsonPropertyName("stalledNotified")]
    public bool StalledNotified { get; set; }

    /// <summary>Gets or sets how many consecutive polls have seen *arr reporting a warning. Reset to zero the moment the download is seen transferring normally, which is what stops the warning/recovery ping-pong from notifying.</summary>
    [JsonPropertyName("warningStreak")]
    public int WarningStreak { get; set; }

    /// <summary>Gets or sets the last observed completion percentage, used to tell real movement from a download sitting still.</summary>
    [JsonPropertyName("lastProgress")]
    public double? LastProgress { get; set; }

    /// <summary>Gets or sets when the percentage last changed. The stall watchdog measures against this, not against when the download was added, so a download that transfers for an hour and then dies is caught from the moment it died.</summary>
    [JsonPropertyName("lastMovementAtUtc")]
    public DateTime? LastMovementAtUtc { get; set; }

    /// <summary>
    /// Compares every tracked field. Used by the store to tell an actual change from the common
    /// case of a poll observing exactly what it saw last time, so a download that sits at the
    /// same percentage doesn't rewrite the file on every cycle.
    /// </summary>
    /// <param name="other">The state to compare against.</param>
    /// <returns><see langword="true"/> if both states are field-for-field identical.</returns>
    public bool Matches(DownloadProgressState? other) =>
        other is not null
        && string.Equals(Stage, other.Stage, StringComparison.Ordinal)
        && StartedNotified == other.StartedNotified
        && HalfNotified == other.HalfNotified
        && WarningNotified == other.WarningNotified
        && FailedNotified == other.FailedNotified
        && StalledNotified == other.StalledNotified
        && WarningStreak == other.WarningStreak
        && LastProgress == other.LastProgress
        && LastMovementAtUtc == other.LastMovementAtUtc;

    /// <summary>Creates an independent copy, so a transition can be computed without mutating the stored state until it's accepted.</summary>
    /// <returns>A shallow clone of this state.</returns>
    public DownloadProgressState Clone() => new()
    {
        Stage = Stage,
        StartedNotified = StartedNotified,
        HalfNotified = HalfNotified,
        WarningNotified = WarningNotified,
        FailedNotified = FailedNotified,
        StalledNotified = StalledNotified,
        WarningStreak = WarningStreak,
        LastProgress = LastProgress,
        LastMovementAtUtc = LastMovementAtUtc
    };
}
