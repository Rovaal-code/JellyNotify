# 🔔 JellyNotify

**Real-time, per-user notifications for Jellyfin.** Requests, downloads, and availability — delivered the moment they happen, to the person who actually asked for the content.

[![License: GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](LICENSE)
[![Latest release](https://img.shields.io/github/v/release/Rovaal-code/JellyNotify)](https://github.com/Rovaal-code/JellyNotify/releases)
[![Jellyfin 10.11+](https://img.shields.io/badge/Jellyfin-10.11%2B-8b5cf6)](#)

JellyNotify connects to **Overseerr/Jellyseerr**, **Sonarr**, and **Radarr**, and notifies each Jellyfin user about *their own* requests — in-app, and optionally on **Telegram**, **Discord**, or **WhatsApp**.

---

## ✨ Features

#### 🔔 Notifications
- A bell injected globally into Jellyfin Web — unread badge, toasts, and a short guided tour on first open
- Per-user language and notification preferences, with real posters on every channel

#### 🔗 Integrations
- Overseerr/Jellyseerr request tracking, plus Sonarr and Radarr (multiple instances supported)
- Optional **instant delivery via webhook**, alongside regular polling — one toggle and a one-click *Copy webhook URL* button per service

#### 💬 Personal channels
- **Telegram**, **Discord**, and **WhatsApp** — connect in one click, confirmed by an automatic welcome message
- A separate global, admin-only Discord webhook for server-wide announcements
- `/status` (Telegram) or `estado` (WhatsApp) answers instantly from JellyNotify's own cache — zero extra load on Seerr/Sonarr/Radarr

#### ⚙️ Admin tools
- Step-by-step setup guides and **Test** buttons for every channel
- **Sample notification** buttons that preview real data before anything is fully wired up
- A Diagnostics tab for sync health, plus a check for newer releases

#### 🔒 Privacy
- Every notification is scoped to the user who made the request
- API keys, tokens, and webhook URLs never reach the regular user UI

---

## 📦 Installation

### Via repository (recommended)

1. **Dashboard → Plugins → Repositories → Add repository**
2. Paste this URL:
   ```text
   https://raw.githubusercontent.com/Rovaal-code/JellyNotify/main/repository/manifest.json
   ```
3. Go to **Catalog**, find JellyNotify, install, and restart Jellyfin.

### Manual

1. Download the ZIP from [Releases](https://github.com/Rovaal-code/JellyNotify/releases)
2. Extract into your Jellyfin plugins folder, e.g. `/var/lib/jellyfin/plugins/JellyNotify_0.1.0.2/`
3. Restart Jellyfin.

### Build from source

```bash
git clone https://github.com/Rovaal-code/JellyNotify.git
cd JellyNotify
bash build.sh
```

Requires the **.NET 9 SDK**. The installable ZIP lands in `releases/`.

---

## ⚙️ Configuration

After installing, open **Dashboard → Plugins → JellyNotify**.

| Service | What you'll need |
|---|---|
| **Overseerr/Jellyseerr** | Server URL, type, API key |
| **Sonarr/Radarr** | One or more instances — name, URL, API key, polling interval |
| **Notifications** | Default language, deduplication window, retention |

Sonarr/Radarr instances are correlated to requests by TMDb, TVDb, and IMDb IDs — no manual mapping needed.

---

## 🔒 Privacy model

Regular users only ever call non-admin endpoints (`GET /JellyNotify/public-settings` and friends) — these return availability flags and language defaults, never API keys, tokens, or internal service URLs.

If JellyNotify can't reliably determine which Jellyfin user a request or download belongs to, it skips that notification rather than guessing.

<details>
<summary><strong>💬 How the three personal channels connect</strong></summary>

All three follow the same shape: click **Connect**, finish pairing in a new tab, and the binding is only saved once that step is actually confirmed — never just from the click.

- **Telegram** — opens `t.me/{botUsername}?start={token}`; a background poller picks up the resulting message and saves the chat ID.
- **Discord** — needs a bot token *and* an OAuth2 Client ID/Secret. Clicking Connect opens Discord's own consent screen; JellyNotify never sees the password. The bot must share a server with the user first (Discord's own anti-spam rule) — invite it once from the Channels tab.
- **WhatsApp** — opens a prefilled `wa.me` chat with a one-time token; Meta's webhook confirms it back. Without Cloud API credentials, it falls back to link-only mode (no automatic replies).

The first successful connection sends a one-time welcome message listing that user's enabled categories.

</details>

<details>
<summary><strong>🌐 Global web injection</strong></summary>

JellyNotify injects its client script into `index.html` at request time (same mechanism as [Jellyfin Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced) — see `NOTICE.md`), which is what makes the bell appear everywhere in Jellyfin Web, not just the plugin's own page. Disable it from **General → Enable global web injection** if you ever need to troubleshoot. Check **Diagnostics** to confirm it's active, or the browser console for `[JellyNotify] loaded`.

</details>

For the full endpoint list, see **[docs/API.md](docs/API.md)**.

---

## 📜 License

**GPL-3.0** — see [`LICENSE`](LICENSE) and [`NOTICE.md`](NOTICE.md). Required since v1.0.3 adapts real code from [Jellyfin Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced), itself GPL-3.0.

---

## 🇪🇸 Español

JellyNotify es un plugin independiente para Jellyfin que añade notificaciones personales por usuario. El idioma base del proyecto es inglés, pero la interfaz incluye traducción al español (`es-ES`) y al catalán (`ca`). La opción "Auto" sigue el idioma que cada usuario ya tenga configurado en su propio Jellyfin.
