# Notice

As of v1.0.3, JellyNotify is licensed under the **GNU General Public License v3.0**
(see `LICENSE`). This change was made because v1.0.3 adapts real, non-trivial code
from [Jellyfin Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced), which is
itself GPL-3.0 licensed. Under GPL-3.0's copyleft terms, a work that incorporates
GPL-3.0 code must itself be distributed under GPL-3.0 (or a compatible license).

## Code adapted from Jellyfin Enhanced (GPL-3.0)

- **`JellyNotify.Plugin/Services/ScriptInjectionStartupFilter.cs`** — adapted from
  Jellyfin Enhanced's `Jellyfin.Plugin.JellyfinEnhanced/Services/ScriptInjectionStartupFilter.cs`.
  This is the `IStartupFilter` middleware that injects the plugin's client
  `<script>` tag into `index.html` at request time, which is what makes the
  notification bell/panel appear globally in Jellyfin Web instead of only on the
  plugin's own configuration page.
- **Controller-served config-page assets** — `JellyNotify.Plugin/Api/WebAssetsController.cs`
  and the way `JellyNotify.Plugin/Configuration/configPage.html` loads its CSS/JS via
  `ApiClient.getUrl(...)` (instead of plain relative `<link>`/`<script src>` tags,
  which do not resolve correctly inside Jellyfin's plugin-page iframe) follow the
  same pattern used by Jellyfin Enhanced's `JellyfinEnhancedController` and
  `configPage.html`.

- **Auto-language detection** — `detectJellyfinServerLanguage()` in
  `JellyNotify.Plugin/Web/jellynotify.js` follows the same technique as Jellyfin
  Enhanced's `JE.loadTranslations()` in
  `Jellyfin.Plugin.JellyfinEnhanced/js/enhanced/translations.js`: read the
  server-side display language Jellyfin Web itself already resolved, via
  `ApiClient.getCurrentUser()` plus the `${userId}-language` `localStorage` key it
  sets, falling back to the `<html lang="...">` attribute. JellyNotify's own
  "Auto" language option relies on this instead of the browser's
  `Accept-Language`/`navigator.language`, so it matches whatever language the
  user actually has configured in Jellyfin, not just their browser.

Both adaptations were rewritten against JellyNotify's own plugin identity, routes,
and configuration model — they are not verbatim copies — but the structure and
approach are directly derived from Jellyfin Enhanced's implementation.

## Visual pattern adapted from a local reference script (not third-party)

The notification bell, badge, panel, and toast visual/structural patterns in
`JellyNotify.Plugin/Web/jellynotify.js` were adapted from a locally-authored,
previously-tested reference script (`references/working-notificator-js/notify-dispatcher-working.js`,
not published or distributed) provided by the project owner. Its `localStorage`-backed
history/snapshot logic was intentionally **not** reused — JellyNotify's real
notification history is always served from its own backend endpoints, scoped to
the authenticated user, per the project's privacy requirements.

## External asset dependency: service icons

The small service icons next to the Serr/Sonarr/Radarr/Discord/Telegram/WhatsApp
section headers and the General tab's "connected services" overview are loaded
at runtime from the [homarr-labs/dashboard-icons](https://github.com/homarr-labs/dashboard-icons)
project's CDN (`cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons`) — the same
external icon source Jellyfin Enhanced's `plugin-icons.js` already links to for
its own dashboard icons. Nothing is bundled or redistributed; the config page
just references the image URLs directly, same as any other externally-hosted
image.

## Everything else

The rest of JellyNotify (models, stores, Overseerr/Jellyseerr/Sonarr/Radarr
clients, notification dispatch, background sync, admin/user API surface) is
original code written for this project.
