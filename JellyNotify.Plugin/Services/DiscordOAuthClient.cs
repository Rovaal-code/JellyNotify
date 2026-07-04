using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>
/// Identifies a Discord account after a successful OAuth2 "Login with Discord" exchange.
/// </summary>
public sealed class DiscordOAuthIdentity
{
    /// <summary>Gets or sets the numeric Discord User ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the Discord username (informational only).</summary>
    public string Username { get; set; } = string.Empty;
}

/// <summary>
/// Exchanges a Discord OAuth2 authorization code for the authenticated user's identity.
/// Used by the "Connect Discord" flow so the user's numeric ID is discovered automatically
/// instead of being typed in by hand.
/// </summary>
public interface IDiscordOAuthClient
{
    /// <summary>Exchanges an authorization code for the caller's Discord identity, or null on failure.</summary>
    Task<DiscordOAuthIdentity?> ExchangeCodeAsync(string code, string redirectUri, string clientId, string clientSecret, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class DiscordOAuthClient : IDiscordOAuthClient
{
    private const string ApiBase = "https://discord.com/api/v10";

    private readonly HttpClient _http;
    private readonly ILogger<DiscordOAuthClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="DiscordOAuthClient"/> class.</summary>
    public DiscordOAuthClient(HttpClient http, ILogger<DiscordOAuthClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DiscordOAuthIdentity?> ExchangeCodeAsync(string code, string redirectUri, string clientId, string clientSecret, CancellationToken cancellationToken = default)
    {
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            });

            using var tokenResponse = await _http.PostAsync($"{ApiBase}/oauth2/token", form, cancellationToken).ConfigureAwait(false);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var body = await tokenResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Discord OAuth token exchange failed with {Status}: {Body}", tokenResponse.StatusCode, body);
                return null;
            }

            var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<DiscordTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(tokenPayload?.AccessToken))
            {
                return null;
            }

            using var meRequest = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/users/@me");
            meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenPayload.AccessToken);

            using var meResponse = await _http.SendAsync(meRequest, cancellationToken).ConfigureAwait(false);
            if (!meResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Discord /users/@me returned {Status}", meResponse.StatusCode);
                return null;
            }

            var me = await meResponse.Content.ReadFromJsonAsync<DiscordUserResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(me?.Id))
            {
                return null;
            }

            return new DiscordOAuthIdentity { Id = me.Id, Username = me.Username ?? string.Empty };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discord OAuth exchange failed");
            return null;
        }
    }

    private sealed class DiscordTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private sealed class DiscordUserResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }
}
