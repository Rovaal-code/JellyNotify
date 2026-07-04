# 🧩 JellyNotify API Reference

All routes are rooted at `/JellyNotify`. Every route below is grouped by who is
actually allowed to call it — that's the detail that matters most, since none
of these are meant to be a public integration surface; they exist to back the
plugin's own UI and its inbound webhooks.

---

## 👤 User endpoints

Require an authenticated Jellyfin session. Used by the notification bell/panel
and the per-user settings view — never by an external caller.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/notifications` | List the calling user's notifications |
| `GET` | `/notifications/unread-count` | Unread badge count |
| `POST` | `/notifications/mark-all-read` | Mark every notification read |
| `DELETE` | `/notifications` | Clear the calling user's notifications |
| `GET` | `/preferences` | Get the calling user's notification preferences |
| `PUT` | `/preferences` | Update preferences |
| `POST` | `/preferences/resolved-language` | Report the language the client auto-resolved, for server-side use in real notifications |
| `POST` | `/preferences/bell-opened` | Marks that the guided tour has been seen |
| `GET` | `/public-settings` | Non-sensitive feature flags + language defaults (see the README's Privacy model section) |
| `POST` | `/notifications/test` | Send a test notification to the calling user |
| `POST` | `/whatsapp/connect` / `/whatsapp/disconnect` | Link or unlink the calling user's WhatsApp |
| `POST` | `/telegram/connect` / `/telegram/disconnect` | Link or unlink the calling user's Telegram |
| `POST` | `/discord/connect-url` | Build the calling user's Discord OAuth consent URL |
| `POST` | `/discord/disconnect` | Unlink the calling user's Discord |

---

## 🔐 Admin endpoints

Require an elevated (administrator) Jellyfin session.

| Method | Route | Purpose |
|---|---|---|
| `GET` / `PUT` | `/Admin/config` | Read or save the full plugin configuration (secrets redacted on read — see the README's Privacy model section) |
| `POST` | `/Admin/test/seerr` | Test the Overseerr/Jellyseerr connection |
| `POST` | `/Admin/test/seerr/sample` | Preview a real Seerr-based sample notification |
| `POST` | `/Admin/test/sonarr/{instanceId}` / `/Admin/test/radarr/{instanceId}` | Test one Sonarr/Radarr instance's connection |
| `POST` | `/Admin/test/sonarr/{instanceId}/sample` / `/Admin/test/radarr/{instanceId}/sample` | Preview a real sample notification for that instance |
| `POST` | `/Admin/test/telegram` / `/Admin/test/discord` / `/Admin/test/whatsapp` | Send a test message on that channel |
| `POST` | `/Admin/telegram/detect-chat-id` | Auto-detect the global Telegram chat ID from the bot's own message history |
| `POST` | `/Admin/test-notification` | Send a full test notification through the normal dispatch path |
| `POST` | `/Admin/sync-now` | Trigger an immediate poll cycle, outside the regular schedule |
| `POST` | `/Admin/reset-baseline` | Re-baseline Seerr snapshots (see Diagnostics tab) |
| `GET` | `/Admin/diagnostics` | Web-injection status, sync health, version check |
| `GET` | `/Admin/notifications/{userId}` | Read any user's notifications, for support/debugging |

---

## ⚡ Inbound webhooks (unauthenticated)

Called by third-party servers, not by Jellyfin Web — there is no Jellyfin
session to authenticate here. Each one is instead gated by an unguessable
secret embedded in the URL path itself (see each service's own setup guide in
the config page for its generated URL).

| Method | Route | Caller |
|---|---|---|
| `GET` | `/whatsapp/webhook` | Meta's one-time verification handshake |
| `POST` | `/whatsapp/webhook` | Meta — inbound WhatsApp message delivery |
| `POST` | `/seerr/webhook/{secret}` | Overseerr/Jellyseerr's own webhook notification agent |
| `POST` | `/arr/webhook/{secret}` | Sonarr/Radarr's Connect webhook (one shared secret covers every configured instance) |

---

## 📄 Static assets (unauthenticated)

Served directly from embedded resources — no session, no secrets, same
content for every caller. This is what makes the bell/panel and the config
page work at all; see the README's Global web injection section.

| Method | Route |
|---|---|
| `GET` | `/script` |
| `GET` | `/web/jellynotify.css` |
| `GET` | `/web/locales/{code}.json` |
| `GET` | `/Configuration/configPage.css` |
| `GET` | `/Configuration/configPage.js` |
