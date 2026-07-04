using Jellyfin.Plugin.JellyNotify.Models;

namespace Jellyfin.Plugin.JellyNotify.Store;

/// <summary>
/// Provides persistence for user channel bindings (Telegram, Discord, WhatsApp)
/// and manages the link-token flow used to associate external messaging accounts
/// with Jellyfin users.
/// </summary>
public interface IUserChannelStore
{
    /// <summary>
    /// Gets the channel binding for the specified Jellyfin user.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <returns>The user's channel binding, or null if not found.</returns>
    Task<UserChannelBinding?> GetByUserAsync(string userId);

    /// <summary>
    /// Inserts or updates a channel binding. Matching is done by <see cref="UserChannelBinding.JellyfinUserId"/>.
    /// </summary>
    /// <param name="binding">The binding to upsert.</param>
    Task UpsertAsync(UserChannelBinding binding);

    /// <summary>
    /// Finds the channel binding associated with the given Telegram chat ID.
    /// </summary>
    /// <param name="chatId">The Telegram chat ID.</param>
    /// <returns>The matching binding, or null if not found.</returns>
    Task<UserChannelBinding?> GetByTelegramChatIdAsync(string chatId);

    /// <summary>
    /// Finds the channel binding associated with the given Discord user ID.
    /// </summary>
    /// <param name="discordUserId">The Discord user ID.</param>
    /// <returns>The matching binding, or null if not found.</returns>
    Task<UserChannelBinding?> GetByDiscordUserIdAsync(string discordUserId);

    /// <summary>
    /// Finds the channel binding associated with the given WhatsApp phone number.
    /// </summary>
    /// <param name="phoneNumber">The WhatsApp phone number (E.164, no leading '+').</param>
    /// <returns>The matching binding, or null if not found.</returns>
    Task<UserChannelBinding?> GetByWhatsAppPhoneNumberAsync(string phoneNumber);

    /// <summary>
    /// Creates a short-lived link token for the specified user and channel.
    /// The token can be used by the user in the external messaging platform
    /// to associate their account with their Jellyfin identity.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID requesting the link.</param>
    /// <param name="channel">The channel name (e.g., "telegram", "discord", "whatsapp").</param>
    /// <returns>The generated link token string.</returns>
    Task<string> CreateLinkTokenAsync(string userId, string channel);

    /// <summary>
    /// Validates a link token for the specified channel.
    /// If valid and not expired, returns the Jellyfin user ID it was issued for.
    /// The token is consumed (removed) upon successful validation.
    /// </summary>
    /// <param name="token">The link token to validate.</param>
    /// <param name="channel">The channel name (e.g., "telegram", "discord", "whatsapp").</param>
    /// <returns>The Jellyfin user ID if the token is valid, or null if invalid/expired.</returns>
    Task<string?> ValidateLinkTokenAsync(string token, string channel);
}
