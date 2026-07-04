using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyNotify.Services;

/// <summary>Result of validating the configured WhatsApp Cloud API credentials.</summary>
public sealed record WhatsAppCredentialCheck(bool Success, string? DisplayPhoneNumber, string? Error);

/// <summary>
/// Sends messages via the Meta WhatsApp Business Cloud API. Requires a permanent/long-lived
/// access token and a Phone Number ID from a Meta for Developers WhatsApp app — see the
/// step-by-step setup guide in the plugin's Channels tab.
/// </summary>
public interface IWhatsAppCloudApiClient
{
    /// <summary>
    /// Sends a free-form text message to the given E.164 phone number (no leading '+').
    /// When <paramref name="imageUrl"/> is given, sends it as an image message with
    /// <paramref name="text"/> as the caption instead of a plain text message, falling
    /// back to text-only if the image send fails (e.g. Meta couldn't fetch the URL).
    /// </summary>
    Task<bool> SendTextMessageAsync(string toPhoneE164, string text, string? imageUrl = null, CancellationToken cancellationToken = default);

    /// <summary>Calls the Graph API to confirm the configured access token and phone number ID are valid.</summary>
    Task<WhatsAppCredentialCheck> VerifyCredentialsAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WhatsAppCloudApiClient : IWhatsAppCloudApiClient
{
    private const string ApiBase = "https://graph.facebook.com/v21.0";

    private readonly HttpClient _http;
    private readonly ILogger<WhatsAppCloudApiClient> _logger;

    /// <summary>Initializes a new instance of the <see cref="WhatsAppCloudApiClient"/> class.</summary>
    public WhatsAppCloudApiClient(HttpClient http, ILogger<WhatsAppCloudApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SendTextMessageAsync(string toPhoneE164, string text, string? imageUrl = null, CancellationToken cancellationToken = default)
    {
        var settings = Plugin.Instance!.Configuration.WhatsAppSettings;
        if (string.IsNullOrWhiteSpace(settings.AccessToken) || string.IsNullOrWhiteSpace(settings.PhoneNumberId))
        {
            return false;
        }

        try
        {
            object payload = imageUrl is not null
                ? new
                {
                    messaging_product = "whatsapp",
                    to = toPhoneE164,
                    type = "image",
                    image = new { link = imageUrl, caption = text }
                }
                : new
                {
                    messaging_product = "whatsapp",
                    to = toPhoneE164,
                    type = "text",
                    text = new { body = text }
                };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/{settings.PhoneNumberId}/messages")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("WhatsApp Cloud API send returned {Status} for {To}: {Body}", response.StatusCode, toPhoneE164, body);

                // If sending as an image failed (bad/unreachable poster URL), still
                // deliver the text so the user doesn't miss the notification entirely.
                if (imageUrl is not null)
                {
                    return await SendTextMessageAsync(toPhoneE164, text, null, cancellationToken).ConfigureAwait(false);
                }

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp message to {To}", toPhoneE164);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<WhatsAppCredentialCheck> VerifyCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var settings = Plugin.Instance!.Configuration.WhatsAppSettings;
        if (string.IsNullOrWhiteSpace(settings.AccessToken) || string.IsNullOrWhiteSpace(settings.PhoneNumberId))
        {
            return new WhatsAppCredentialCheck(false, null, "Access token or Phone Number ID is not configured.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/{settings.PhoneNumberId}?fields=verified_name,display_phone_number");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("WhatsApp Cloud API credential check returned {Status}: {Body}", response.StatusCode, body);
                return new WhatsAppCredentialCheck(false, null, $"Meta rejected the request ({(int)response.StatusCode}). Check the access token and Phone Number ID.");
            }

            var payload = System.Text.Json.JsonSerializer.Deserialize<PhoneNumberInfo>(body);
            return new WhatsAppCredentialCheck(true, payload?.DisplayPhoneNumber, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp Cloud API credential check failed");
            return new WhatsAppCredentialCheck(false, null, ex.Message);
        }
    }

    private sealed class PhoneNumberInfo
    {
        [JsonPropertyName("display_phone_number")]
        public string? DisplayPhoneNumber { get; set; }

        [JsonPropertyName("verified_name")]
        public string? VerifiedName { get; set; }
    }
}
