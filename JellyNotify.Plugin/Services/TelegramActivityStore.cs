namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Remembers the most recent Telegram chat ID seen by <see cref="TelegramLinkingService"/>'s
/// background poller. Used by the admin "Detect automatically" action for the Global chat ID
/// field — reading from this shared, already-polled state instead of making a second,
/// independent call to Telegram's getUpdates. A second independent call would race the
/// background poller: whichever one calls getUpdates first advances Telegram's server-side
/// offset and "consumes" the update, so the other call sees an empty result.
/// </summary>
public interface ITelegramActivityStore
{
    /// <summary>Gets the most recently observed chat ID, or null if none has been seen yet.</summary>
    string? LastChatId { get; }

    /// <summary>Records a chat ID as the most recently observed one.</summary>
    void RecordChatId(string chatId);
}

/// <inheritdoc />
public sealed class TelegramActivityStore : ITelegramActivityStore
{
    private volatile string? _lastChatId;

    /// <inheritdoc />
    public string? LastChatId => _lastChatId;

    /// <inheritdoc />
    public void RecordChatId(string chatId) => _lastChatId = chatId;
}
