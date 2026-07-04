using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Plugin.JellyNotify.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Store;

/// <summary>
/// Thread-safe, JSON-file-backed user channel binding store.
/// Manages the association between Jellyfin user accounts and external
/// messaging channels (Telegram, Discord, WhatsApp), including the
/// link-token verification flow.
/// </summary>
public sealed class JsonUserChannelStore : IUserChannelStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Characters used to generate link tokens (alphanumeric, no ambiguous chars).
    /// </summary>
    private const string TokenCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Length of generated link tokens.
    /// </summary>
    private const int TokenLength = 8;

    /// <summary>
    /// Duration after which a link token expires.
    /// </summary>
    private static readonly TimeSpan TokenExpiry = TimeSpan.FromMinutes(15);

    private readonly string _filePath;
    private readonly ILogger<JsonUserChannelStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private List<UserChannelBinding>? _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonUserChannelStore"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public JsonUserChannelStore(ILogger<JsonUserChannelStore> logger)
    {
        _logger = logger;
        var dataPath = Plugin.Instance!.DataFolderPath;
        _filePath = Path.Combine(dataPath, "channel-bindings.json");
    }

    /// <inheritdoc />
    public async Task<UserChannelBinding?> GetByUserAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all.FirstOrDefault(b =>
                string.Equals(b.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(UserChannelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var existingIndex = all.FindIndex(b =>
                string.Equals(b.JellyfinUserId, binding.JellyfinUserId, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                all[existingIndex] = binding;
            }
            else
            {
                all.Add(binding);
            }

            await SaveAsync(all).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<UserChannelBinding?> GetByTelegramChatIdAsync(string chatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all.FirstOrDefault(b =>
                string.Equals(b.TelegramChatId, chatId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<UserChannelBinding?> GetByDiscordUserIdAsync(string discordUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discordUserId);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all.FirstOrDefault(b =>
                string.Equals(b.DiscordUserId, discordUserId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<UserChannelBinding?> GetByWhatsAppPhoneNumberAsync(string phoneNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            return all.FirstOrDefault(b =>
                string.Equals(b.WhatsAppPhoneNumber, phoneNumber, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateLinkTokenAsync(string userId, string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var token = GenerateToken();

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var binding = all.FirstOrDefault(b =>
                string.Equals(b.JellyfinUserId, userId, StringComparison.OrdinalIgnoreCase));

            if (binding is null)
            {
                binding = new UserChannelBinding
                {
                    Id = Guid.NewGuid().ToString("N"),
                    JellyfinUserId = userId,
                    PendingLinkTokens = new Dictionary<string, PendingLinkToken>()
                };
                all.Add(binding);
            }

            binding.PendingLinkTokens ??= new Dictionary<string, PendingLinkToken>();

            // Remove any existing token for this channel before adding a new one
            var normalizedChannel = channel.ToLowerInvariant();
            var keysToRemove = binding.PendingLinkTokens
                .Where(kvp => string.Equals(kvp.Value.Channel, normalizedChannel, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                binding.PendingLinkTokens.Remove(key);
            }

            binding.PendingLinkTokens[token] = new PendingLinkToken
            {
                Channel = normalizedChannel,
                CreatedAt = DateTime.UtcNow
            };

            await SaveAsync(all).ConfigureAwait(false);

            _logger.LogDebug(
                "Created link token for user {UserId} on channel {Channel}",
                userId,
                normalizedChannel);

            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string?> ValidateLinkTokenAsync(string token, string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var normalizedChannel = channel.ToLowerInvariant();

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadAsync().ConfigureAwait(false);
            var modified = false;
            string? matchedUserId = null;

            foreach (var binding in all)
            {
                if (binding.PendingLinkTokens is null)
                {
                    continue;
                }

                // Purge all expired tokens while iterating
                var expiredKeys = binding.PendingLinkTokens
                    .Where(kvp => DateTime.UtcNow - kvp.Value.CreatedAt > TokenExpiry)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    binding.PendingLinkTokens.Remove(key);
                    modified = true;
                }

                if (binding.PendingLinkTokens.TryGetValue(token, out var pendingToken)
                    && string.Equals(pendingToken.Channel, normalizedChannel, StringComparison.OrdinalIgnoreCase))
                {
                    // Token found and channel matches — consume it
                    binding.PendingLinkTokens.Remove(token);
                    matchedUserId = binding.JellyfinUserId;
                    modified = true;
                    break;
                }
            }

            if (modified)
            {
                await SaveAsync(all).ConfigureAwait(false);
            }

            if (matchedUserId is not null)
            {
                _logger.LogInformation(
                    "Successfully validated link token for user {UserId} on channel {Channel}",
                    matchedUserId,
                    normalizedChannel);
            }

            return matchedUserId;
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
    /// Generates a cryptographically random alphanumeric token.
    /// </summary>
    private static string GenerateToken()
    {
        Span<char> result = stackalloc char[TokenLength];
        Span<byte> randomBytes = stackalloc byte[TokenLength];
        RandomNumberGenerator.Fill(randomBytes);

        for (var i = 0; i < TokenLength; i++)
        {
            result[i] = TokenCharacters[randomBytes[i] % TokenCharacters.Length];
        }

        return new string(result);
    }

    /// <summary>
    /// Lazily loads the binding list from disk into the in-memory cache.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private async Task<List<UserChannelBinding>> LoadAsync()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_filePath))
        {
            _cache = new List<UserChannelBinding>();
            return _cache;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            _cache = JsonSerializer.Deserialize<List<UserChannelBinding>>(json, SerializerOptions)
                     ?? new List<UserChannelBinding>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize {File}; starting with empty binding list", _filePath);
            _cache = new List<UserChannelBinding>();
        }

        return _cache;
    }

    /// <summary>
    /// Persists the binding list to disk and updates the in-memory cache.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private async Task SaveAsync(List<UserChannelBinding> bindings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(bindings, SerializerOptions);
        await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
        _cache = bindings;
    }
}

// PendingLinkToken is defined in Models/UserChannelBinding.cs
