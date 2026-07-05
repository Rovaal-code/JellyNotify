using System.Security.Claims;
using Jellyfin.Plugin.JellyNotify;
using Jellyfin.Plugin.JellyNotify.Api;
using Jellyfin.Plugin.JellyNotify.Configuration;
using Jellyfin.Plugin.JellyNotify.Models;
using Jellyfin.Plugin.JellyNotify.Services;
using Jellyfin.Plugin.JellyNotify.Store;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellyNotify.Tests;

/// <summary>
/// Tests for <see cref="NotificationDeduplicationStore"/>.
/// </summary>
public sealed class DeduplicationStoreTests
{
    [Fact]
    public void IsDuplicate_ReturnsFalse_WhenKeyNotRecorded()
    {
        var store = new NotificationDeduplicationStore();
        Assert.False(store.IsDuplicate("some-key"));
    }

    [Fact]
    public void IsDuplicate_ReturnsTrue_AfterRecord()
    {
        var store = new NotificationDeduplicationStore();
        store.Record("dup-key", TimeSpan.FromMinutes(10));
        Assert.True(store.IsDuplicate("dup-key"));
    }

    [Fact]
    public void IsDuplicate_ReturnsFalse_AfterExpiry()
    {
        var store = new NotificationDeduplicationStore();
        store.Record("expired-key", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(10); // let it expire
        Assert.False(store.IsDuplicate("expired-key"));
    }

    [Fact]
    public void Clear_RemovesAllRecords()
    {
        var store = new NotificationDeduplicationStore();
        store.Record("key1", TimeSpan.FromMinutes(10));
        store.Record("key2", TimeSpan.FromMinutes(10));
        store.Clear();
        Assert.False(store.IsDuplicate("key1"));
        Assert.False(store.IsDuplicate("key2"));
    }
}

/// <summary>
/// Tests for <see cref="DownloadProgressStore"/>.
/// </summary>
public sealed class DownloadProgressStoreTests
{
    [Fact]
    public void GetProgress_ReturnsNull_WhenNotTracked()
    {
        var store = new DownloadProgressStore();
        Assert.Null(store.GetProgress("unknown-key"));
    }

    [Fact]
    public void SetAndGet_ReturnsExpectedStatus()
    {
        var store = new DownloadProgressStore();
        store.SetProgress("key1", "downloading");
        Assert.Equal("downloading", store.GetProgress("key1"));
    }

    [Fact]
    public void SetProgress_OverwritesPreviousStatus()
    {
        var store = new DownloadProgressStore();
        store.SetProgress("key1", "downloading");
        store.SetProgress("key1", "imported");
        Assert.Equal("imported", store.GetProgress("key1"));
    }

    [Fact]
    public void Remove_ClearsProgress()
    {
        var store = new DownloadProgressStore();
        store.SetProgress("key1", "downloading");
        store.Remove("key1");
        Assert.Null(store.GetProgress("key1"));
    }

    [Fact]
    public void GetAllKeys_ReturnsTrackedKeys()
    {
        var store = new DownloadProgressStore();
        store.SetProgress("key-a", "status-a");
        store.SetProgress("key-b", "status-b");
        var keys = store.GetAllKeys();
        Assert.Contains("key-a", keys);
        Assert.Contains("key-b", keys);
    }
}

/// <summary>
/// Tests for <see cref="NotificationEvent"/> model structure.
/// </summary>
public sealed class NotificationEventTests
{
    [Fact]
    public void NewNotification_HasUniqueId()
    {
        var n1 = new NotificationEvent();
        var n2 = new NotificationEvent();
        Assert.NotEqual(n1.Id, n2.Id);
    }

    [Fact]
    public void NewNotification_IsUnreadByDefault()
    {
        var n = new NotificationEvent();
        Assert.False(n.IsRead);
        Assert.Null(n.ReadAt);
    }

    [Fact]
    public void NewNotification_HasCreatedAtSet()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var n = new NotificationEvent();
        Assert.True(n.CreatedAt >= before);
    }
}

/// <summary>
/// Tests for <see cref="UserNotificationPreference"/> defaults.
/// </summary>
public sealed class UserNotificationPreferenceTests
{
    [Fact]
    public void DefaultPreference_HasAllNotificationsEnabled()
    {
        var pref = new UserNotificationPreference();
        Assert.True(pref.NotifyOnRequest);
        Assert.True(pref.NotifyOnApproval);
        Assert.True(pref.NotifyOnDownload);
        Assert.True(pref.NotifyOnAvailable);
        Assert.True(pref.NotifyOnIssue);
        Assert.True(pref.JellyfinUiEnabled);
    }

}

/// <summary>
/// Tests for <see cref="ExternalIds"/> structure.
/// </summary>
public sealed class ExternalIdsTests
{
    [Fact]
    public void ExternalIds_AllPropertiesDefaultNull()
    {
        var ids = new ExternalIds();
        Assert.Null(ids.TmdbId);
        Assert.Null(ids.TvdbId);
        Assert.Null(ids.ImdbId);
        Assert.Null(ids.SonarrSeriesId);
        Assert.Null(ids.RadarrMovieId);
    }
}

/// <summary>
/// Tests for <see cref="PluginConfiguration"/> defaults.
/// </summary>
public sealed class PluginConfigurationTests
{
    [Fact]
    public void DefaultConfig_HasNotificationsEnabled()
    {
        var config = new Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration();
        Assert.True(config.NotificationSettings.Enabled);
    }

    [Fact]
    public void DefaultConfig_HasReasonableDefaults()
    {
        var config = new Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration();
        Assert.Equal(10, config.NotificationSettings.DeduplicationWindowMinutes);
        Assert.Equal(200, config.MaxNotificationsPerUser);
        Assert.Equal(30, config.NotificationRetentionDays);
    }

    [Fact]
    public void DefaultConfig_SeerrDisabledByDefault()
    {
        var config = new Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration();
        Assert.False(config.SeerrSettings.Enabled);
    }

    [Fact]
    public void DefaultConfig_EmptyInstanceLists()
    {
        var config = new Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration();
        Assert.Empty(config.SonarrInstances);
        Assert.Empty(config.RadarrInstances);
    }
}

/// <summary>
/// Tests for secret preservation logic in <see cref="AdminController"/>.
/// </summary>
public sealed class SecretPreservationTests
{
    [Fact]
    public void PreserveSecrets_RetainsExistingSecrets_WhenIncomingIsEmpty()
    {
        var existing = new Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration
        {
            SeerrSettings = new Jellyfin.Plugin.JellyNotify.Configuration.SeerrSettings { ApiKey = "persisted-seerr-key" },
            ExternalChannelSettings = new Jellyfin.Plugin.JellyNotify.Configuration.ExternalChannelSettings
            {
                DiscordSettings = new Jellyfin.Plugin.JellyNotify.Configuration.DiscordChannelSettings { WebhookUrl = "persisted-discord-webhook" },
                TelegramSettings = new Jellyfin.Plugin.JellyNotify.Configuration.TelegramChannelSettings { BotToken = "persisted-telegram-token" }
            },
            SonarrInstances = new List<Jellyfin.Plugin.JellyNotify.Configuration.ArrInstanceConfig>
            {
                new() { Id = "sonarr1", ApiKey = "persisted-sonarr-key" }
            },
            RadarrInstances = new List<Jellyfin.Plugin.JellyNotify.Configuration.ArrInstanceConfig>
            {
                new() { Id = "radarr1", ApiKey = "persisted-radarr-key" }
            }
        };

        var incoming = new Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration
        {
            SeerrSettings = new Jellyfin.Plugin.JellyNotify.Configuration.SeerrSettings { ApiKey = "" },
            ExternalChannelSettings = new Jellyfin.Plugin.JellyNotify.Configuration.ExternalChannelSettings
            {
                DiscordSettings = new Jellyfin.Plugin.JellyNotify.Configuration.DiscordChannelSettings { WebhookUrl = null! },
                TelegramSettings = new Jellyfin.Plugin.JellyNotify.Configuration.TelegramChannelSettings { BotToken = "  " }
            },
            SonarrInstances = new List<Jellyfin.Plugin.JellyNotify.Configuration.ArrInstanceConfig>
            {
                new() { Id = "sonarr1", ApiKey = "" }
            },
            RadarrInstances = new List<Jellyfin.Plugin.JellyNotify.Configuration.ArrInstanceConfig>
            {
                new() { Id = "radarr1", ApiKey = null! }
            }
        };

        Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration.PreserveSecrets(existing, incoming);

        Assert.Equal("persisted-seerr-key", incoming.SeerrSettings.ApiKey);
        Assert.Equal("persisted-discord-webhook", incoming.ExternalChannelSettings.DiscordSettings.WebhookUrl);
        Assert.Equal("persisted-telegram-token", incoming.ExternalChannelSettings.TelegramSettings.BotToken);
        Assert.Equal("persisted-sonarr-key", incoming.SonarrInstances[0].ApiKey);
        Assert.Equal("persisted-radarr-key", incoming.RadarrInstances[0].ApiKey);
    }
}

/// <summary>
/// Tests for localization and i18n configurations.
/// </summary>
public sealed class I18nTests
{
    [Fact]
    public void DefaultLanguage_IsAutoByDefault()
    {
        var config = new Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration();
        Assert.Equal("auto", config.DefaultLanguage);
    }

    [Fact]
    public void UserLanguage_IsAutoByDefault()
    {
        var prefs = new UserNotificationPreference();
        Assert.Equal("auto", prefs.Language);
    }

    [Fact]
    public void FrontendLocales_ContainEnglishAndSpanishRequiredStrings()
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var localesDir = Path.Combine(rootDir, "JellyNotify.Plugin", "Web", "locales");
        var en = File.ReadAllText(Path.Combine(localesDir, "en-US.json"));
        var es = File.ReadAllText(Path.Combine(localesDir, "es-ES.json"));
        var ca = File.ReadAllText(Path.Combine(localesDir, "ca.json"));

        Assert.Contains("Notifications", en);
        Assert.Contains("Notificaciones", es);
        Assert.Contains("Notificacions", ca);
        Assert.Contains("Mark all as read", en);
        Assert.Contains("Marcar todo como leído", es);
        Assert.Contains("Marca-ho tot com a llegit", ca);
        Assert.Contains("Problem detected", en);
        Assert.Contains("Problema detectado", es);
        Assert.Contains("Problema detectat", ca);

        // All three must also carry a fully translated admin config-page vocabulary,
        // not just the bell/panel strings — this is what fixes the config page
        // never localizing at all.
        foreach (var json in new[] { en, es, ca })
        {
            Assert.Contains("\"config\"", json);
            Assert.Contains("\"tabSonarr\"", json);
            Assert.Contains("\"telegramDetectBtn\"", json);
        }
    }

    [Fact]
    public void FrontendLocales_AreServedByWebAssetsController()
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var controllerPath = Path.Combine(rootDir, "JellyNotify.Plugin", "Api", "WebAssetsController.cs");
        var controller = File.ReadAllText(controllerPath);

        Assert.Contains("web/locales/{code}.json", controller);
    }

    [Fact]
    public void FrontendUserUi_UsesPublicSettingsEndpoint()
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var jsPath = Path.Combine(rootDir, "JellyNotify.Plugin", "Web", "jellynotify.js");
        var js = File.ReadAllText(jsPath);

        Assert.Contains("apiRequest('/public-settings')", js);
        Assert.Contains("apiRequest('/Admin/config')", js);
        Assert.True(js.IndexOf("function loadConfig()", StringComparison.Ordinal) < js.IndexOf("apiRequest('/Admin/config')", StringComparison.Ordinal));
    }
}

/// <summary>
/// Tests for public non-sensitive settings.
/// </summary>
public sealed class PublicSettingsTests
{
    [Fact]
    public void PublicSettingsResponse_DoesNotExposeSecretOrInternalUrlProperties()
    {
        var propertyNames = typeof(PublicSettingsResponse)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(propertyNames, p => p.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, p => p.Contains("Webhook", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, p => p.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, p => p.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Tests for Seerr/Jellyseerr request parsing.
/// </summary>
public sealed class SeerrParsingTests
{
    [Fact]
    public void ExtractMediaTitle_UsesRealRootTitle()
    {
        var request = new SeerrRequest
        {
            Type = "movie",
            Title = "Dune: Part Two"
        };

        Assert.Equal("Dune: Part Two", MediaRequestService.ExtractMediaTitle(request));
    }

    [Fact]
    public void ExtractMediaTitle_UsesEmbeddedMediaName()
    {
        var request = new SeerrRequest
        {
            Type = "tv",
            Media = new SeerrMedia { Name = "The Bear" }
        };

        Assert.Equal("The Bear", MediaRequestService.ExtractMediaTitle(request));
    }
}

/// <summary>
/// Tests for Arr correlation behavior.
/// </summary>
public sealed class ArrCorrelationTests
{
    [Fact]
    public void FindSnapshotsForMovie_ReturnsAllUsersForSameMedia()
    {
        var snapshots = new List<RequestSnapshot>
        {
            new() { JellyfinUserId = "user-1", ExternalIds = new ExternalIds { TmdbId = "550" } },
            new() { JellyfinUserId = "user-2", ExternalIds = new ExternalIds { TmdbId = "550" } },
            new() { JellyfinUserId = "user-3", ExternalIds = new ExternalIds { TmdbId = "999" } }
        };

        var matches = ArrSyncService.FindSnapshotsForMovie(snapshots, new ArrMovie { TmdbId = 550 });

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, s => s.JellyfinUserId == "user-1");
        Assert.Contains(matches, s => s.JellyfinUserId == "user-2");
    }
}

/// <summary>
/// Tests for build and release metadata.
/// </summary>
public sealed class ReleaseMetadataTests
{
    [Fact]
    public void BuildScript_DefaultsToV011AndUpdatesRepositoryManifest()
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var buildScript = File.ReadAllText(Path.Combine(rootDir, "build.sh"));

        Assert.Contains("VERSION=\"0.1.0.1\"", buildScript);
        Assert.Contains("repository/manifest.json", buildScript);
        Assert.Contains("Rovaal-code/JellyNotify/releases/download", buildScript);
    }

    [Fact]
    public void RepositoryManifest_ContainsV011ReleaseUrl()
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var manifest = File.ReadAllText(Path.Combine(rootDir, "repository", "manifest.json"));

        Assert.Contains("\"version\": \"0.1.0.1\"", manifest);
        Assert.Contains("https://github.com/Rovaal-code/JellyNotify/releases/download/v0.1.0.1/jellynotify_0.1.0.1.zip", manifest);
    }

    [Fact]
    public void RepositoryManifest_ChecksumMatchesLocalV011Zip_WhenPackageExists()
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var zipPath = Path.Combine(rootDir, "releases", "jellynotify_0.1.0.1.zip");
        if (!File.Exists(zipPath))
        {
            return;
        }

        var manifest = File.ReadAllText(Path.Combine(rootDir, "repository", "manifest.json"));
        using var md5 = System.Security.Cryptography.MD5.Create();
        using var stream = File.OpenRead(zipPath);
        var actual = Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();

        Assert.Contains($"\"checksum\": \"{actual}\"", manifest);
    }
}

/// <summary>
/// Tests for the manifest.json file.
/// </summary>
public sealed class ManifestTests
{
    [Fact]
    public void ManifestFile_ExistsAndCanBeParsed()
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var manifestPath = Path.Combine(rootDir, "manifest.json");
        
        Assert.True(File.Exists(manifestPath), $"manifest.json should exist at {manifestPath}");
        var content = File.ReadAllText(manifestPath);
        Assert.Contains("JellyNotify", content);
        Assert.Contains("Rovaal-code/JellyNotify", content);
    }
}

/// <summary>
/// Tests for GPL-3.0 licensing and attribution introduced in v1.0.3.
/// </summary>
public sealed class LicenseTests
{
    [Fact]
    public void License_IsGpl3()
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var license = File.ReadAllText(Path.Combine(rootDir, "LICENSE"));
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", license);
        Assert.Contains("Version 3", license);
    }

    [Fact]
    public void Notice_AttributesJellyfinEnhancedAdaptations()
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var notice = File.ReadAllText(Path.Combine(rootDir, "NOTICE.md"));
        Assert.Contains("Jellyfin Enhanced", notice);
        Assert.Contains("ScriptInjectionStartupFilter", notice);
    }
}

/// <summary>
/// Tests for the WhatsApp wa.me channel settings model.
/// </summary>
public sealed class WhatsAppSettingsTests
{
    [Fact]
    public void DefaultSettings_AreDisabledWithNoPhoneNumber()
    {
        var settings = new WhatsAppChannelSettings();
        Assert.False(settings.Enabled);
        Assert.Equal(string.Empty, settings.PhoneNumber);
    }

    [Fact]
    public void PreserveSecrets_DoesNotThrow_WhenIncomingWhatsAppSettingsIsNull()
    {
        var existing = new Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration();
        var incoming = new Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration
        {
            WhatsAppSettings = null!
        };

        Jellyfin.Plugin.JellyNotify.Configuration.PluginConfiguration.PreserveSecrets(existing, incoming);

        Assert.NotNull(incoming.WhatsAppSettings);
    }
}

/// <summary>
/// Tests for the admin Diagnostics DTO — must never expose secrets.
/// </summary>
public sealed class DiagnosticsResponseTests
{
    [Fact]
    public void DiagnosticsResponse_DoesNotExposeSecretProperties()
    {
        var propertyNames = typeof(DiagnosticsResponse)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(propertyNames, p => p.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, p => p.Contains("Webhook", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, p => p.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// In-memory fake of <see cref="INotificationStore"/> for controller-level isolation tests.
/// Also records which user ID each call was scoped to, so tests can assert a call
/// never touched another user's data.
/// </summary>
internal sealed class FakeNotificationStore : INotificationStore
{
    private readonly List<NotificationEvent> _all = new();

    public string? LastMarkAllReadUserId { get; private set; }

    public string? LastClearUserId { get; private set; }

    public void Seed(NotificationEvent notification) => _all.Add(notification);

    public Task<IReadOnlyList<NotificationEvent>> GetByUserAsync(string userId) =>
        Task.FromResult<IReadOnlyList<NotificationEvent>>(
            _all.Where(n => n.JellyfinUserId == userId).ToList());

    public Task AddAsync(NotificationEvent notification)
    {
        _all.Add(notification);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<NotificationEvent> notifications)
    {
        _all.AddRange(notifications);
        return Task.CompletedTask;
    }

    public Task MarkAllReadAsync(string userId)
    {
        LastMarkAllReadUserId = userId;
        foreach (var n in _all.Where(n => n.JellyfinUserId == userId))
        {
            n.IsRead = true;
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(string userId)
    {
        LastClearUserId = userId;
        _all.RemoveAll(n => n.JellyfinUserId == userId);
        return Task.CompletedTask;
    }

    public Task<int> GetUnreadCountAsync(string userId) =>
        Task.FromResult(_all.Count(n => n.JellyfinUserId == userId && !n.IsRead));

    public Task PurgeOldAsync(int retentionDays) => Task.CompletedTask;

    public Task<int> GetTotalCountAsync(string userId) =>
        Task.FromResult(_all.Count(n => n.JellyfinUserId == userId));

    public Task<int> GetTotalCountAllUsersAsync() => Task.FromResult(_all.Count);
}

/// <summary>In-memory fake of <see cref="IUserPreferenceStore"/> for controller tests.</summary>
internal sealed class FakeUserPreferenceStore : IUserPreferenceStore
{
    private readonly Dictionary<string, UserNotificationPreference> _byUser = new();

    public Task<UserNotificationPreference> GetByUserAsync(string userId)
    {
        if (!_byUser.TryGetValue(userId, out var pref))
        {
            pref = new UserNotificationPreference { JellyfinUserId = userId };
            _byUser[userId] = pref;
        }

        return Task.FromResult(pref);
    }

    public Task UpsertAsync(UserNotificationPreference preference)
    {
        _byUser[preference.JellyfinUserId] = preference;
        return Task.CompletedTask;
    }
}

/// <summary>In-memory fake of <see cref="IUserChannelStore"/> for controller tests.</summary>
internal sealed class FakeUserChannelStore : IUserChannelStore
{
    private readonly Dictionary<string, UserChannelBinding> _byUser = new();

    public Task<UserChannelBinding?> GetByUserAsync(string userId) =>
        Task.FromResult(_byUser.TryGetValue(userId, out var b) ? b : null);

    public Task UpsertAsync(UserChannelBinding binding)
    {
        _byUser[binding.JellyfinUserId] = binding;
        return Task.CompletedTask;
    }

    public Task<UserChannelBinding?> GetByTelegramChatIdAsync(string chatId) =>
        Task.FromResult(_byUser.Values.FirstOrDefault(b => b.TelegramChatId == chatId));

    public Task<UserChannelBinding?> GetByDiscordUserIdAsync(string discordUserId) =>
        Task.FromResult(_byUser.Values.FirstOrDefault(b => b.DiscordUserId == discordUserId));

    public Task<UserChannelBinding?> GetByWhatsAppPhoneNumberAsync(string phoneNumber) =>
        Task.FromResult(_byUser.Values.FirstOrDefault(b => b.WhatsAppPhoneNumber == phoneNumber));

    public Task<string> CreateLinkTokenAsync(string userId, string channel) =>
        Task.FromResult(Guid.NewGuid().ToString("N"));

    public Task<string?> ValidateLinkTokenAsync(string token, string channel) =>
        Task.FromResult<string?>(null);
}

/// <summary>No-op fake of <see cref="INotificationDispatcher"/> for controller tests.</summary>
internal sealed class FakeNotificationDispatcher : INotificationDispatcher
{
    public Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>No-op fake of <see cref="ITelegramNotificationClient"/> for controller tests.</summary>
internal sealed class FakeTelegramNotificationClient : ITelegramNotificationClient
{
    public Task<bool> SendAsync(NotificationEvent notification, string chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> SendAsync(string htmlBody, string? thumbnailUrl, string chatId, string notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> SendHtmlAsync(string html, string chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

/// <summary>No-op fake of <see cref="IDiscordDmClient"/> for controller tests.</summary>
internal sealed class FakeDiscordDmClient : IDiscordDmClient
{
    public Task<bool> SendDirectMessageAsync(string discordUserId, string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> SendEmbedAsync(string discordUserId, string message, string? thumbnailUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

/// <summary>No-op fake of <see cref="IDiscordOAuthClient"/> for controller tests.</summary>
internal sealed class FakeDiscordOAuthClient : IDiscordOAuthClient
{
    public Task<DiscordOAuthIdentity?> ExchangeCodeAsync(string code, string redirectUri, string clientId, string clientSecret, CancellationToken cancellationToken = default) =>
        Task.FromResult<DiscordOAuthIdentity?>(null);
}

/// <summary>
/// Tests that <see cref="NotificationsController"/> never leaks one user's
/// notifications/preferences to another, and that read-all/clear are always
/// scoped to the currently authenticated caller.
/// </summary>
public sealed class NotificationsControllerIsolationTests
{
    private static NotificationsController CreateController(
        FakeNotificationStore store,
        FakeUserPreferenceStore prefStore,
        string callingUserId)
    {
        var controller = new NotificationsController(
            store,
            prefStore,
            new FakeUserChannelStore(),
            new FakeNotificationDispatcher(),
            new FakeTelegramNotificationClient(),
            new FakeDiscordDmClient(),
            new FakeDiscordOAuthClient(),
            new Mock<IUserManager>().Object,
            new Mock<MediaBrowser.Controller.IServerApplicationHost>().Object,
            NullLogger<NotificationsController>.Instance);

        var claims = new ClaimsIdentity(new[] { new Claim("Jellyfin-UserId", callingUserId) }, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(claims) }
        };

        return controller;
    }

    [Fact]
    public async Task GetNotifications_OnlyReturnsCallingUsersNotifications()
    {
        var store = new FakeNotificationStore();
        store.Seed(new NotificationEvent { JellyfinUserId = "user-1", Title = "For user 1" });
        store.Seed(new NotificationEvent { JellyfinUserId = "user-2", Title = "For user 2" });

        var controller = CreateController(store, new FakeUserPreferenceStore(), "user-1");

        var result = await controller.GetNotifications();
        var ok = Assert.IsType<ActionResult<IReadOnlyList<NotificationEvent>>>(result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<NotificationEvent>>(((Microsoft.AspNetCore.Mvc.OkObjectResult)ok.Result!).Value);

        Assert.Single(list);
        Assert.All(list, n => Assert.Equal("user-1", n.JellyfinUserId));
    }

    [Fact]
    public async Task MarkAllRead_OnlyAffectsCallingUser()
    {
        var store = new FakeNotificationStore();
        store.Seed(new NotificationEvent { JellyfinUserId = "user-1" });
        store.Seed(new NotificationEvent { JellyfinUserId = "user-2" });

        var controller = CreateController(store, new FakeUserPreferenceStore(), "user-1");

        await controller.MarkAllRead();

        Assert.Equal("user-1", store.LastMarkAllReadUserId);
        Assert.Equal(0, await store.GetUnreadCountAsync("user-1"));
        Assert.Equal(1, await store.GetUnreadCountAsync("user-2"));
    }

    [Fact]
    public async Task ClearAll_OnlyAffectsCallingUser()
    {
        var store = new FakeNotificationStore();
        store.Seed(new NotificationEvent { JellyfinUserId = "user-1" });
        store.Seed(new NotificationEvent { JellyfinUserId = "user-2" });

        var controller = CreateController(store, new FakeUserPreferenceStore(), "user-1");

        await controller.ClearAll();

        Assert.Equal("user-1", store.LastClearUserId);
        Assert.Empty(await store.GetByUserAsync("user-1"));
        Assert.Single(await store.GetByUserAsync("user-2"));
    }

    [Fact]
    public async Task UpdatePreferences_RejectsMismatchedJellyfinUserId()
    {
        var controller = CreateController(new FakeNotificationStore(), new FakeUserPreferenceStore(), "user-1");

        var result = await controller.UpdatePreferences(new UserNotificationPreference
        {
            JellyfinUserId = "user-2",
            Language = "en-US"
        });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdatePreferences_ClearsWhatsAppConnectedFromClientPayload()
    {
        var prefStore = new FakeUserPreferenceStore();
        var controller = CreateController(new FakeNotificationStore(), prefStore, "user-1");

        await controller.UpdatePreferences(new UserNotificationPreference
        {
            JellyfinUserId = "user-1",
            Language = "en-US",
            WhatsAppConnected = true
        });

        var saved = await prefStore.GetByUserAsync("user-1");
        Assert.False(saved.WhatsAppConnected);
    }
}

/// <summary>
/// Tests for <see cref="WelcomeMessageBuilder"/> — specifically that each channel gets
/// its own bold syntax, and that a hostile username/server name can't break formatting
/// (or, for Telegram's HTML parse mode, break the send itself).
/// </summary>
public sealed class WelcomeMessageBuilderTests
{
    [Fact]
    public void Telegram_BoldsBrandUserAndServer_AndHtmlEscapesThem()
    {
        var prefs = new UserNotificationPreference { Language = "es-ES" };
        var text = WelcomeMessageBuilder.Build("<script>", "My & Server", prefs, WelcomeMessageChannel.Telegram);

        Assert.Contains("<b>JellyNotify</b>", text);
        Assert.Contains("<b>&lt;script&gt;</b>", text);
        Assert.Contains("<b>My &amp; Server</b>", text);
        Assert.DoesNotContain("<script>", text);
        Assert.Contains("/status", text);
    }

    [Fact]
    public void Discord_BoldsWithDoubleAsterisks_AndEscapesMarkdown()
    {
        var prefs = new UserNotificationPreference { Language = "en-US" };
        var text = WelcomeMessageBuilder.Build("a*b", "Server_Name", prefs, WelcomeMessageChannel.Discord);

        Assert.Contains("**JellyNotify**", text);
        Assert.Contains("**a\\*b**", text);
        Assert.Contains("**Server\\_Name**", text);
        Assert.DoesNotContain("/status", text);
        Assert.DoesNotContain("estado", text);
    }

    [Fact]
    public void WhatsApp_BoldsWithSingleAsterisks_AndEscapesFormatting()
    {
        var prefs = new UserNotificationPreference { Language = "ca" };
        var text = WelcomeMessageBuilder.Build("a*b", "Server_Name", prefs, WelcomeMessageChannel.WhatsApp);

        Assert.Contains("*JellyNotify*", text);
        Assert.Contains("*a\\*b*", text);
        Assert.Contains("*Server\\_Name*", text);
        Assert.Contains("estado", text);
    }

    [Fact]
    public void Body_ListsAllSixCategoriesRegardlessOfPreferences()
    {
        var prefs = new UserNotificationPreference
        {
            Language = "en-US",
            NotifyOnRequest = false,
            NotifyOnApproval = false,
            NotifyOnDownload = false,
            NotifyOnAvailable = false,
            NotifyOnPartiallyAvailable = false,
            NotifyOnIssue = false
        };

        var text = WelcomeMessageBuilder.Build("user", "server", prefs, WelcomeMessageChannel.Discord);

        Assert.Contains("1. New content requests", text);
        Assert.Contains("6. Problems or issues", text);
    }
}

/// <summary>
/// Tests for <see cref="PosterUrlBuilder"/>.
/// </summary>
public sealed class PosterUrlBuilderTests
{
    [Fact]
    public void Build_ReturnsNull_WhenPathIsNullOrWhitespace()
    {
        Assert.Null(PosterUrlBuilder.Build(null));
        Assert.Null(PosterUrlBuilder.Build(string.Empty));
        Assert.Null(PosterUrlBuilder.Build("   "));
    }

    [Fact]
    public void Build_PrependsTmdbImageBase_WhenPathIsGiven()
    {
        Assert.Equal("https://image.tmdb.org/t/p/w500/abc123.jpg", PosterUrlBuilder.Build("/abc123.jpg"));
    }
}

/// <summary>
/// Tests for <see cref="AdminTestMessages"/> — spot-checks that each language actually
/// produces different, correctly-formatted text (not just falling back to English).
/// </summary>
public sealed class AdminTestMessagesTests
{
    [Theory]
    [InlineData("en-US", "Connected successfully. Version: 1.2.3")]
    [InlineData("es-ES", "Conectado correctamente. Versión: 1.2.3")]
    [InlineData("ca", "Connectat correctament. Versió: 1.2.3")]
    public void SeerrConnectSuccess_FormatsVersionPerLanguage(string language, string expected)
    {
        Assert.Equal(expected, AdminTestMessages.SeerrConnectSuccess("1.2.3", language));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    [InlineData("ca")]
    public void SonarrSampleWithDownloads_IncludesBothCounts(string language)
    {
        var text = AdminTestMessages.SonarrSampleWithDownloads(12, 3, language);
        Assert.Contains("12", text);
        Assert.Contains("3", text);
    }

    [Fact]
    public void WhatsAppLinkOnly_IncludesTheGivenLink()
    {
        var text = AdminTestMessages.WhatsAppLinkOnly("https://wa.me/123?text=hi", "es-ES");
        Assert.Contains("https://wa.me/123?text=hi", text);
    }
}

/// <summary>
/// Tests for <see cref="RequestStatusSummaryBuilder"/> — the /status command / "estado"
/// keyword output. Covers real titles (not generic Movie/TV Show placeholders), category
/// headers per status (with no per-item date — that was deemed redundant/noisy),
/// downloading progress/ETA/quality, and graceful fallback when data is missing.
/// </summary>
public sealed class RequestStatusSummaryBuilderTests
{
    private static RequestSnapshot Snapshot(string status, string title = "Real Title") => new()
    {
        SeerrRequestId = 1,
        JellyfinUserId = "user-1",
        MediaTitle = title,
        Status = status
    };

    [Fact]
    public void Build_ShowsRealTitle_NotGenericPlaceholder()
    {
        var snapshot = Snapshot("req:Approved|media:Processing", "One Piece");
        var text = RequestStatusSummaryBuilder.Build(new[] { snapshot }, "user-1", "es-ES");

        Assert.Contains("One Piece", text);
        Assert.DoesNotContain("- Movie", text);
        Assert.DoesNotContain("- TV Show", text);
    }

    [Fact]
    public void Build_Downloading_ShowsMostRecentlyUpdatedFirst_NotJustInsertionOrder()
    {
        // 6 older downloading items (inserted first, so a naive "take the first 5" would
        // hide the newest one behind "+2") + 1 that just started downloading a moment ago.
        // The most recently active item must always be visible, regardless of insertion order.
        var older = Enumerable.Range(0, 6).Select(i =>
        {
            var s = Snapshot("req:Approved|media:Processing", $"Older Movie {i}");
            s.SeerrRequestId = 200 + i;
            s.DownloadStartedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i);
            return s;
        });
        var justStarted = Snapshot("req:Approved|media:Processing", "Brand New Download");
        justStarted.SeerrRequestId = 999;
        justStarted.DownloadStartedAt = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);

        var all = older.Append(justStarted).ToList();
        var text = RequestStatusSummaryBuilder.Build(all, "user-1", "en-US");

        Assert.Contains("Brand New Download", text);
        Assert.Contains("(+2)", text);
    }

    [Fact]
    public void Build_PartitionsGenericPlaceholders_OutOfVisibleListAndIntoPlusCount()
    {
        // 5 generic-titled items (inserted first) + 1 real-titled item (inserted last) —
        // a naive "take the first 5" would show the 5 generics and hide the real title
        // behind "+1". The real title must appear regardless of insertion order, and all
        // 5 generic items must be folded into the trailing count.
        var generics = Enumerable.Range(0, 5).Select(i =>
        {
            var s = Snapshot("req:Approved|media:Processing", "Movie");
            s.MediaType = "movie";
            s.SeerrRequestId = 100 + i;
            return s;
        });
        var real = Snapshot("req:Approved|media:Processing", "One Piece");
        real.MediaType = "tv";
        real.SeerrRequestId = 999;

        var all = generics.Append(real).ToList();
        var text = RequestStatusSummaryBuilder.Build(all, "user-1", "en-US");

        Assert.Contains("One Piece", text);
        Assert.DoesNotContain("- Movie", text);
        Assert.Contains("(+5)", text);
    }

    [Fact]
    public void Build_IncludesYear_WhenKnown()
    {
        var snapshot = Snapshot("req:Approved|media:Available", "Dune: Part Two");
        snapshot.Year = 2024;
        snapshot.AvailableAt = new DateTime(2026, 7, 3, 12, 40, 0, DateTimeKind.Utc);

        var text = RequestStatusSummaryBuilder.Build(new[] { snapshot }, "user-1", "en-US");

        Assert.Contains("Dune: Part Two (2024)", text);
    }

    [Theory]
    [InlineData("req:Pending|media:Unknown", "Pendientes de aprobar")]
    [InlineData("req:Approved|media:Available", "Disponibles")]
    [InlineData("req:Approved|media:Partial", "Parcialmente disponibles")]
    [InlineData("req:Declined|media:Unknown", "Rechazadas")]
    [InlineData("req:Failed|media:Unknown", "Fallidas")]
    public void Build_ShowsTheRelevantCategoryHeader_PerStatus_WithNoPerItemDate(string status, string expectedCategoryLabel)
    {
        var snapshot = Snapshot(status);
        var when = new DateTime(2026, 7, 3, 12, 40, 0, DateTimeKind.Utc);
        snapshot.RequestedAt = when;
        snapshot.AvailableAt = when;
        snapshot.PartiallyAvailableAt = when;
        snapshot.FailedAt = when;

        var text = RequestStatusSummaryBuilder.Build(new[] { snapshot }, "user-1", "es-ES");

        Assert.Contains(expectedCategoryLabel, text);
        // No per-item date/timestamp anywhere — status is conveyed by the category header alone.
        Assert.DoesNotContain("03/07/2026", text);
    }

    [Fact]
    public void Build_DownloadingItem_ShowsProgressEtaAndQuality_ButNoDate()
    {
        var snapshot = Snapshot("req:Approved|media:Processing", "One Piece");
        snapshot.ArrProgress = 42.3;
        snapshot.ArrTimeLeft = "00:18:00";
        snapshot.ArrQuality = "1080p";
        snapshot.ArrLastProgressAt = new DateTime(2026, 7, 3, 12, 40, 0, DateTimeKind.Utc);

        var text = RequestStatusSummaryBuilder.Build(new[] { snapshot }, "user-1", "es-ES");

        Assert.Contains("42.3%", text);
        Assert.Contains("ETA 18min", text);
        Assert.Contains("1080p", text);
        Assert.DoesNotContain("Actualizado", text);
        Assert.DoesNotContain("03/07/2026", text);
    }

    [Fact]
    public void Build_DownloadingItem_WithoutProgressOrEta_OmitsThemEntirely_NotAsUnknown()
    {
        var snapshot = Snapshot("req:Approved|media:Processing", "One Piece");

        var text = RequestStatusSummaryBuilder.Build(new[] { snapshot }, "user-1", "en-US");

        Assert.Contains("One Piece", text);
        Assert.DoesNotContain("unknown", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("null", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_NoRequests_ReturnsFriendlyMessage()
    {
        var text = RequestStatusSummaryBuilder.Build(Array.Empty<RequestSnapshot>(), "user-1", "en-US");
        Assert.Contains("don't have any requests", text);
    }
}

/// <summary>
/// Tests for <see cref="NotificationCardFormatter"/> — the shared enrichment applied to
/// every notification (any type, any provider) before it's sent.
/// </summary>
public sealed class NotificationCardFormatterTests
{
    [Fact]
    public void Enrich_DownloadStarted_Discord_UsesDoubleAsteriskBold_AndIncludesProgressAndEta()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.DownloadStarted,
            Title = "Download started",
            ProgressPercent = 42,
            EtaRaw = "00:18:00",
            Quality = "1080p" // Quality is intentionally NOT part of the download field set anymore
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Discord);

        Assert.Contains("**Progress**\n42%", result);
        Assert.Contains("**ETA**\n18min", result);
        Assert.DoesNotContain("Quality", result);
        Assert.DoesNotContain("Date", result);
    }

    [Fact]
    public void Enrich_DownloadStarted_Telegram_UsesHtmlBoldAndEscapesLabel()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.DownloadStarted,
            Title = "Downloading",
            ProgressPercent = 42,
            EtaRaw = "00:18:00"
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Telegram);

        Assert.Contains("<b>Progress</b>\n42%", result);
        Assert.Contains("<b>ETA</b>\n18min", result);
    }

    [Fact]
    public void Enrich_DownloadStarted_WhatsApp_UsesSingleAsteriskBold()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.DownloadStarted,
            Title = "Downloading",
            ProgressPercent = 42,
            EtaRaw = "00:18:00"
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.WhatsApp);

        Assert.Contains("*Progress*\n42%", result);
        Assert.DoesNotContain("**Progress**", result);
    }

    [Fact]
    public void Enrich_MissingEta_ShowsUnknown_NotBlank()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.DownloadStarted,
            Title = "Downloading",
            ProgressPercent = 5
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Discord);

        Assert.Contains("**ETA**\nunknown", result);
    }

    [Fact]
    public void Enrich_FailedDownload_IncludesFailureReason()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.DownloadFailed,
            Title = "Download failed",
            FailureReason = "Not enough seeders"
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Discord);

        Assert.Contains("**Reason**\nNot enough seeders", result);
    }

    [Fact]
    public void Enrich_RequestDeclined_OnlyHasEstadoField_WhenFailureReasonNull()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.RequestDeclined,
            Title = "Your request was declined"
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Discord);

        Assert.Equal("❌ **Status**\nYour request was declined", result);
    }

    [Fact]
    public void Enrich_AlwaysStartsWithEstadoField_UsingTitleAndTypeIcon()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.MediaAvailable,
            Title = "Available",
            MediaTitle = "Dune: Part Two"
        };

        var result = NotificationCardFormatter.Enrich(notification, "es-ES", NotificationChannel.Discord);

        Assert.StartsWith("🎬 **Estado**\nAvailable", result);
    }

    [Fact]
    public void Enrich_RequestApproved_ShowsTituloAndAno_NoDateField()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.RequestApproved,
            Title = "Your request was approved",
            MediaTitle = "Dune: Part Two",
            Year = 2024
        };

        var result = NotificationCardFormatter.Enrich(notification, "es-ES", NotificationChannel.Discord);

        Assert.Contains("**Título**\nDune: Part Two", result);
        Assert.Contains("**Año**\n2024", result);
        Assert.DoesNotContain("Fecha", result);
        Assert.DoesNotContain("Progreso", result);
    }

    [Fact]
    public void Enrich_MediaAvailable_ShowsAudioAndSubtitles_WhenPresent()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.MediaAvailable,
            Title = "Now available",
            MediaTitle = "Dune: Part Two",
            Year = 2024,
            Quality = "WEBDL-1080p",
            AudioLanguages = "en, es",
            SubtitleLanguages = "en, es, fr"
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Discord);

        Assert.Contains("**Title**\nDune: Part Two", result);
        Assert.Contains("**Year**\n2024", result);
        Assert.Contains("**Quality**\nWEBDL-1080p", result);
        Assert.Contains("**Audio**\nen, es", result);
        Assert.Contains("**Subtitles**\nen, es, fr", result);
    }

    [Fact]
    public void Enrich_MediaAvailable_TruncatesAudioAndSubtitles_ToThreeLanguagesPlusCount()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.MediaAvailable,
            Title = "Now available",
            MediaTitle = "Dune: Part Two",
            AudioLanguages = "en, es, fr, de, it",
            SubtitleLanguages = "en, es, fr, de"
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Discord);

        Assert.Contains("**Audio**\nen, es, fr …(+2)", result);
        Assert.Contains("**Subtitles**\nen, es, fr …(+1)", result);
    }

    [Fact]
    public void Enrich_MediaAvailable_OmitsAudioAndSubtitles_WhenNull()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.MediaAvailable,
            Title = "Now available",
            MediaTitle = "Dune: Part Two"
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Discord);

        Assert.DoesNotContain("Audio", result);
        Assert.DoesNotContain("Subtitles", result);
    }

    [Fact]
    public void Enrich_MediaPartiallyAvailable_IncludesSeason()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.MediaPartiallyAvailable,
            Title = "Partially available",
            MediaTitle = "The Boys",
            Season = 4
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Discord);

        Assert.Contains("**Season**\n4", result);
    }

    [Fact]
    public void Enrich_MediaAvailable_DoesNotIncludeSeason_EvenWhenSet()
    {
        // Season only applies to MediaPartiallyAvailable per the field table — a stray
        // Season value on a fully-available notification must not leak into the card.
        var notification = new NotificationEvent
        {
            Type = NotificationType.MediaAvailable,
            Title = "Now available",
            MediaTitle = "The Boys",
            Season = 4
        };

        var result = NotificationCardFormatter.Enrich(notification, "en-US", NotificationChannel.Discord);

        Assert.DoesNotContain("Season", result);
    }

    [Fact]
    public void BuildFieldBlock_OnlyHasEstadoField_WhenTypeHasNoOtherFields()
    {
        var notification = new NotificationEvent
        {
            Type = NotificationType.DownloadFailed,
            Title = "Download failed"
            // No FailureReason set — DownloadFailed's only other field is conditional on it.
        };

        var block = NotificationCardFormatter.BuildFieldBlock(notification, "en-US", NotificationChannel.Telegram);

        Assert.Equal("💔 <b>Status</b>\nDownload failed", block);
    }
}

/// <summary>
/// Tests for <see cref="SeerrMediaDetails"/>'s computed <see cref="SeerrMediaDetails.DisplayTitle"/>
/// and <see cref="SeerrMediaDetails.Year"/> — the movie/tv detail lookup this plugin uses as a
/// title fallback (see MediaRequestService) and to show the release year in /status.
/// </summary>
public sealed class SeerrMediaDetailsTests
{
    [Fact]
    public void DisplayTitle_PrefersMovieTitle_OverTvName()
    {
        var details = new SeerrMediaDetails { Title = "Dune: Part Two", Name = "Should not be used" };
        Assert.Equal("Dune: Part Two", details.DisplayTitle);
    }

    [Fact]
    public void DisplayTitle_FallsBackToTvName_WhenNoMovieTitle()
    {
        var details = new SeerrMediaDetails { Name = "One Piece" };
        Assert.Equal("One Piece", details.DisplayTitle);
    }

    [Fact]
    public void Year_ParsesFromReleaseDate_ForMovies()
    {
        var details = new SeerrMediaDetails { ReleaseDate = "2024-02-27" };
        Assert.Equal(2024, details.Year);
    }

    [Fact]
    public void Year_ParsesFromFirstAirDate_ForTv()
    {
        var details = new SeerrMediaDetails { FirstAirDate = "1999-10-20" };
        Assert.Equal(1999, details.Year);
    }

    [Fact]
    public void Year_IsNull_WhenNoDateAvailable()
    {
        var details = new SeerrMediaDetails();
        Assert.Null(details.Year);
    }
}

/// <summary>
/// Tests for <see cref="CaptionTruncator"/> — keeps Telegram/WhatsApp photo captions under
/// their ~1024-char limit by trimming only the free-text base message, never the field block.
/// </summary>
public sealed class CaptionTruncatorTests
{
    [Fact]
    public void TruncateForPhotoCaption_NoOp_WhenUnderLimit()
    {
        var text = "Short message.\n\n📊 **Progress**: 42%";
        Assert.Equal(text, CaptionTruncator.TruncateForPhotoCaption(text, 1024));
    }

    [Fact]
    public void TruncateForPhotoCaption_TruncatesBaseMessageOnly_PreservesFieldBlock()
    {
        var baseMessage = new string('x', 2000);
        var fieldBlock = "📊 **Progress**: 42%\n⏳ **ETA**: 18min";
        var text = $"{baseMessage}\n\n{fieldBlock}";

        var result = CaptionTruncator.TruncateForPhotoCaption(text, 100);

        Assert.True(result.Length <= 100);
        Assert.EndsWith(fieldBlock, result);
        Assert.Contains("…", result);
    }

    [Fact]
    public void TruncateForPhotoCaption_HardCutoff_WhenNoFieldBlockPresent()
    {
        var text = new string('x', 2000);

        var result = CaptionTruncator.TruncateForPhotoCaption(text, 100);

        Assert.Equal(100, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void TruncateForPhotoCaption_DegenerateCase_FieldBlockAloneExceedsLimit()
    {
        var baseMessage = "Short.";
        var fieldBlock = "\n\n" + new string('y', 2000);
        var text = baseMessage + fieldBlock;

        var result = CaptionTruncator.TruncateForPhotoCaption(text, 100);

        Assert.True(result.Length <= 100);
    }
}

/// <summary>
/// Tests for <see cref="ArrSyncService.MapArrStatus"/>'s handling of the *arr import
/// statuses that used to map to the now-retired DownloadImported notification.
/// </summary>
public sealed class MapArrStatusTests
{
    [Theory]
    [InlineData("importpending")]
    [InlineData("imported")]
    [InlineData("completed")]
    public void MapArrStatus_ImportStatuses_ProduceNoNotification(string status)
    {
        var (type, title, message) = ArrSyncService.MapArrStatus(status, "One Piece", "en-US");

        Assert.Null(type);
        Assert.Equal(string.Empty, title);
        Assert.Equal(string.Empty, message);
    }

    [Fact]
    public void MapArrStatus_Downloading_StillProducesDownloadStarted()
    {
        var (type, _, _) = ArrSyncService.MapArrStatus("downloading", "One Piece", "en-US");
        Assert.Equal(NotificationType.DownloadStarted, type);
    }
}

/// <summary>
/// Tests for <see cref="SeerrWebhookPayload"/> deserialization — the shape Overseerr/
/// Jellyseerr's webhook agent sends, only ever read for its <c>request.request_id</c>.
/// </summary>
public sealed class SeerrWebhookPayloadTests
{
    [Fact]
    public void Deserialize_MediaApprovedPayload_ExtractsRequestId()
    {
        const string json = """
            {
                "notification_type": "MEDIA_APPROVED",
                "request": { "request_id": "42" }
            }
            """;

        var payload = System.Text.Json.JsonSerializer.Deserialize<SeerrWebhookPayload>(json);

        Assert.Equal("MEDIA_APPROVED", payload?.NotificationType);
        Assert.Equal("42", payload?.Request?.RequestId);
        Assert.True(int.TryParse(payload?.Request?.RequestId, out var requestId));
        Assert.Equal(42, requestId);
    }

    [Fact]
    public void Deserialize_TestNotificationPayload_HasNoRequestObject()
    {
        const string json = """
            {
                "notification_type": "TEST_NOTIFICATION"
            }
            """;

        var payload = System.Text.Json.JsonSerializer.Deserialize<SeerrWebhookPayload>(json);

        Assert.Equal("TEST_NOTIFICATION", payload?.NotificationType);
        Assert.Null(payload?.Request);
    }

    [Fact]
    public void Deserialize_UnexpectedShape_DoesNotThrow()
    {
        const string json = "{}";

        var payload = System.Text.Json.JsonSerializer.Deserialize<SeerrWebhookPayload>(json);

        Assert.NotNull(payload);
        Assert.Null(payload!.Request);
    }
}
