using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyNotify.Models;

/// <summary>
/// Maps a Jellyfin user to their linked notification channel accounts
/// (Telegram, Discord, WhatsApp).
/// </summary>
public class UserChannelBinding
{
    /// <summary>Gets or sets the unique identifier for this binding.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the Jellyfin user ID.</summary>
    [JsonPropertyName("jellyfinUserId")]
    public string JellyfinUserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Telegram chat ID for this user.</summary>
    [JsonPropertyName("telegramChatId")]
    public string? TelegramChatId { get; set; }

    /// <summary>Gets or sets the Discord user ID for this user.</summary>
    [JsonPropertyName("discordUserId")]
    public string? DiscordUserId { get; set; }

    /// <summary>Gets or sets the WhatsApp phone number for this user.</summary>
    [JsonPropertyName("whatsAppPhoneNumber")]
    public string? WhatsAppPhoneNumber { get; set; }

    /// <summary>Gets or sets the timestamp when the Telegram account was linked.</summary>
    [JsonPropertyName("telegramLinkedAt")]
    public DateTime? TelegramLinkedAt { get; set; }

    /// <summary>Gets or sets the timestamp when the Discord account was linked.</summary>
    [JsonPropertyName("discordLinkedAt")]
    public DateTime? DiscordLinkedAt { get; set; }

    /// <summary>Gets or sets the timestamp when the WhatsApp account was linked.</summary>
    [JsonPropertyName("whatsAppLinkedAt")]
    public DateTime? WhatsAppLinkedAt { get; set; }

    /// <summary>Gets or sets pending link tokens keyed by token string, used during the account linking flow.</summary>
    [JsonPropertyName("pendingLinkTokens")]
    public Dictionary<string, PendingLinkToken>? PendingLinkTokens { get; set; }
}

/// <summary>
/// Represents a pending link token awaiting validation from an external messaging channel.
/// Defined in UserChannelBinding.cs for JSON serialization coherence.
/// </summary>
public sealed class PendingLinkToken
{
    /// <summary>Gets or sets the channel this token is for (e.g., "telegram", "discord").</summary>
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC timestamp when this token was created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
