/**
 * JellyNotify – Main JavaScript
 * Manages the notification bell, panel, toasts,
 * and the plugin configuration page.
 *
 * Security principles:
 * - API keys are NEVER exposed to the frontend.
 * - All calls to Seerr/Sonarr/Radarr are handled server-side.
 * - Endpoints use the Jellyfin session for authentication.
 */

(function () {
    'use strict';

    // Guard against double-injection: the global <script> tag is injected into
    // index.html by the server, and Jellyfin Web's SPA router never reloads that
    // tag — but defend against any duplicate injection path anyway so we never
    // end up with two bells, two poll timers, or two MutationObservers.
    //
    // This guard must NOT apply on the config page: Jellyfin Web loads plugin
    // config pages into the *same* window/document as the rest of the SPA
    // (not an isolated iframe), so by the time this file loads a second time
    // via /JellyNotify/Configuration/configPage.js, window.__jellyNotifyLoaded
    // is already true from the globally-injected bell script. Returning early
    // here would silently skip init()/initConfigPage() and leave every button
    // on the config page (tabs, save, test) dead with no visible error.
    const isConfigPage = !!document.getElementById('jellynotify-config-page');
    if (!isConfigPage) {
        if (window.__jellyNotifyLoaded) return;
        window.__jellyNotifyLoaded = true;
    }

    // ─── Constants ────────────────────────────────────────────────────
    const API_BASE = '/JellyNotify';
    const POLL_INTERVAL_MS = 30_000; // 30 seconds
    const WEBHOOK_STATUS_POLL_INTERVAL_MS = 5_000; // 5 seconds — the Test button that triggers this lives in Seerr/Sonarr/Radarr's own UI, not this page, so there's no click here to hang the refresh off of
    const TOAST_DURATION_MS = 5_000;
    const MAX_TOAST_STACK = 5;

    // ─── i18n Locales ─────────────────────────────────────────────────
    // Locale strings live in Web/locales/{code}.json (served by WebAssetsController),
    // not inline here — this covers both the bell/panel UI and the admin config
    // page's own labels (under the "config" key of each file), fetched and cached
    // per language so switching languages actually changes every piece of text
    // instead of leaving most of the page in whatever language it first rendered in.
    const SUPPORTED_LANGS = ['en-US', 'es-ES', 'ca'];
    const _localeCache = {};

    // Minimal built-in fallback in case the locale file can't be fetched at all
    // (e.g. a network hiccup) — enough to keep the bell functional in English.
    const FALLBACK_LOCALE = {
        notifications: 'Notifications', markAllRead: 'Mark all as read', clear: 'Clear', close: 'Close',
        noNotifications: 'No notifications', loading: 'Loading…', loadError: 'Error loading notifications',
        justNow: 'Just now', secondsAgo: '{0} seconds ago', minutesAgo: '{0} min ago', hoursAgo: '{0}h ago',
        daysAgo: '{0}d ago', settings: 'Settings', back: 'Back', save: 'Save', config: {}
    };

    /** Formats a `{0}`/`{1}`-style template string with positional args. */
    function fmt(template, ...args) {
        if (!template) return '';
        return template.replace(/\{(\d+)\}/g, (_, i) => (args[i] !== undefined ? args[i] : ''));
    }

    async function loadLocale(code) {
        if (_localeCache[code]) return _localeCache[code];

        try {
            const response = await fetch(`${API_BASE}/web/locales/${code}.json`);
            if (response.ok) {
                const data = await response.json();
                _localeCache[code] = data;
                return data;
            }
        } catch (e) {
            console.warn('[JellyNotify] Failed to load locale', code, e);
        }

        return FALLBACK_LOCALE;
    }
    // ─── State ────────────────────────────────────────────────────────
    let _currentLang = 'en-US';
    let _userPrefs = null;
    let _adminConfig = null;
    let _publicSettings = null;
    let _isFirstPoll = true;
    let _showingSettings = false;

    let _bellBtn = null;
    let _badge = null;
    let _panel = null;
    let _toastContainer = null;
    let _pollTimer = null;
    let _lastUnreadCount = 0;
    let _panelOpen = false;

    // ─── Icons per notification type ─────────────────────────────────
    const TYPE_ICONS = {
        RequestCreated: '📋',
        RequestApproved: '✅',
        RequestDeclined: '❌',
        RequestFailed: '⚠️',
        DownloadStarted: '⬇️',
        DownloadProgress: '📊',
        DownloadWarning: '⚠️',
        DownloadFailed: '💔',
        MediaAvailable: '🎬',
        MediaPartiallyAvailable: '🎞️',
        IssueWarning: '🔔',
        IssueResolved: '🎉',
        TestNotification: '🔔',
    };

    // ─── Utilities ────────────────────────────────────────────────────

    function getStrings() {
        return _localeCache[_currentLang] || FALLBACK_LOCALE;
    }

    /** Maps an arbitrary BCP-47-ish code to one of our three shipped locales, or null if none match. */
    function normalizeToSupportedLang(code) {
        if (!code) return null;
        const lower = code.toLowerCase();
        if (lower.startsWith('ca')) return 'ca';
        if (lower.startsWith('es')) return 'es-ES';
        if (lower.startsWith('en')) return 'en-US';
        return null;
    }

    /**
     * Reads the Jellyfin server/user's own configured display language — the same
     * technique Jellyfin Enhanced uses. Jellyfin Web stores the user's chosen
     * language in localStorage under `${userId}-language` and always reflects it
     * in <html lang="...">, so reading these gives the real server-side language
     * instead of the browser's Accept-Language header (which may not match what
     * the user actually picked in Jellyfin).
     */
    async function detectJellyfinServerLanguage() {
        try {
            let user = ApiClient.getCurrentUser ? ApiClient.getCurrentUser() : null;
            if (user instanceof Promise) {
                user = await user;
            }
            const userId = user?.Id;
            if (userId) {
                const stored = localStorage.getItem(`${userId}-language`);
                if (stored) return stored;
            }
        } catch (e) {
            // Fall through to the <html lang> attribute below.
        }

        return document.documentElement.lang || null;
    }

    async function resolveLanguage() {
        let resolvedFromAuto = false;

        try {
            _userPrefs = await apiRequest('/preferences');
            let lang = _userPrefs?.language || 'auto';

            if (lang === 'auto') {
                resolvedFromAuto = true;
                const serverLang = normalizeToSupportedLang(await detectJellyfinServerLanguage());
                if (serverLang) {
                    lang = serverLang;
                } else {
                    if (!_publicSettings) {
                        try {
                            _publicSettings = await apiRequest('/public-settings');
                        } catch (e) {}
                    }
                    lang = normalizeToSupportedLang(_publicSettings?.defaultLanguage) || 'en-US';
                }
            }

            _currentLang = normalizeToSupportedLang(lang) || 'en-US';
        } catch (err) {
            _currentLang = 'en-US';
        }

        await loadLocale(_currentLang);

        // The server has no browser to resolve "auto" from on its own (bot welcome
        // messages, test notifications) — silently report back whatever this tab just
        // resolved it to, so those server-only sends have something better than English
        // to fall back to. Only when it actually changes, and never blocks the UI.
        if (resolvedFromAuto && _userPrefs && _userPrefs.resolvedLanguage !== _currentLang) {
            apiRequest('/preferences/resolved-language', {
                method: 'POST',
                body: JSON.stringify({ language: _currentLang })
            }).catch(() => {});
        }
    }

    /**
     * Makes an authenticated request to the JellyNotify API.
     * Uses the Jellyfin session credentials.
     */
    async function apiRequest(path, options = {}) {
        const url = `${API_BASE}${path}`;
        const response = await fetch(url, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                'X-MediaBrowser-Token': ApiClient.accessToken(),
                ...(options.headers || {})
            }
        });
        if (!response.ok) {
            throw new Error(`JellyNotify API error: ${response.status} ${response.statusText}`);
        }
        if (response.status !== 204) {
            return response.json();
        }
        return null;
    }

    function formatRelativeTime(isoString) {
        const date = new Date(isoString);
        const now = new Date();
        const diffMs = now - date;
        const diffSec = Math.floor(diffMs / 1000);
        const diffMin = Math.floor(diffMs / 60000);
        const diffHours = Math.floor(diffMin / 60);
        const diffDays = Math.floor(diffHours / 24);

        const strings = getStrings();
        if (diffSec < 5) return strings.justNow;
        if (diffMin < 1) return fmt(strings.secondsAgo, diffSec);
        if (diffMin < 60) return fmt(strings.minutesAgo, diffMin);
        if (diffHours < 24) return fmt(strings.hoursAgo, diffHours);
        if (diffDays < 7) return fmt(strings.daysAgo, diffDays);
        return date.toLocaleDateString(_currentLang, { day: '2-digit', month: 'short' });
    }

    function getIcon(type) {
        return TYPE_ICONS[type] || '🔔';
    }

    function translateNotificationTitle(title) {
        const strings = getStrings();
        const map = {
            'Request registered': strings.requestRegistered,
            'Request approved': strings.requestApproved,
            'Request declined': strings.requestDeclined,
            'Request failed': strings.requestFailed,
            'Download started': strings.downloadStarted,
            'Download warning': strings.downloadWarning,
            'Download failed': strings.downloadFailed,
            'Media available': strings.mediaAvailable,
            'Media imported': strings.mediaAvailable,
            'Media partially available': strings.mediaPartiallyAvailable,
            'Problem detected': strings.problemDetected
        };

        return map[title] || title;
    }

    // ─── Bell UI injection ────────────────────────────────────────────

    /**
     * Finds (or creates) the container the bell button should be injected into.
     *
     * Jellyfin 10.11+'s "experimental" layout (now the default) replaces the
     * legacy AngularJS header with a React/MUI AppBar+Toolbar. The legacy
     * `.headerRight` element is still present in the DOM for backwards
     * compatibility, but it sits inside a `display:none` wrapper, so injecting
     * into it silently produces an invisible bell. Adapted from Jellyfin
     * Enhanced's getHeaderRightContainer() (GPL-3.0, see NOTICE.md).
     */
    function getHeaderRightContainer() {
        const legacy = document.querySelector('.headerRight, .skinHeader-withBackground .headerRight, header .headerRight');
        if (legacy && legacy.offsetParent !== null) return legacy;

        const userMenuButton = document.querySelector('[aria-controls="app-user-menu"]');
        const toolbar = userMenuButton?.closest('.MuiToolbar-root') || document.querySelector('.MuiAppBar-root .MuiToolbar-root');
        if (!toolbar) return null;

        let userMenuBox = userMenuButton;
        while (userMenuBox && userMenuBox.parentElement !== toolbar) {
            userMenuBox = userMenuBox.parentElement;
        }
        const buttonsTray = userMenuBox?.previousElementSibling;
        if (buttonsTray) return buttonsTray;

        // No user-menu available (e.g. public/video pages) - fall back to a
        // synthetic container appended to the toolbar itself.
        let container = toolbar.querySelector(':scope > .jn-header-container');
        if (!container) {
            container = document.createElement('div');
            container.className = 'jn-header-container';
            toolbar.appendChild(container);
        }
        return container;
    }

    // injectBellUI is async (it awaits resolveLanguage() before creating the
    // button), and it's called both from a retry timer and from a
    // MutationObserver that fires on every DOM change. Without this flag,
    // several overlapping calls can all pass the "does #jn-bell-btn exist?"
    // check before the first one finishes awaiting and actually creates the
    // button, each inserting its own duplicate.
    let _injectingBell = false;

    /**
     * Whether Jellyfin has an authenticated session right now. Checked synchronously
     * via ApiClient.accessToken() (empty/falsy before login) rather than
     * getCurrentUser() (async in some Jellyfin Web versions) since this is called on
     * every retry tick and every DOM mutation — it needs to be cheap. Without this,
     * the login screen's own AppBar/Toolbar (which has no user-menu button, same as
     * a public/video page) gets matched by getHeaderRightContainer()'s synthetic-
     * container fallback, so the bell would flash in on the login page and then
     * disappear the moment React tears that screen down after a real login.
     */
    function hasActiveSession() {
        try {
            return !!(window.ApiClient && ApiClient.accessToken && ApiClient.accessToken());
        } catch (e) {
            return false;
        }
    }

    async function injectBellUI() {
        if (!hasActiveSession()) return;

        const toolbar = getHeaderRightContainer();
        if (!toolbar || document.getElementById('jn-bell-btn') || _injectingBell) return;
        _injectingBell = true;

        try {
            await injectBellUIInner(toolbar);
        } finally {
            _injectingBell = false;
        }
    }

    async function injectBellUIInner(toolbar) {
        await resolveLanguage();
        const strings = getStrings();

        // Create bell button
        _bellBtn = document.createElement('button');
        _bellBtn.id = 'jn-bell-btn';
        _bellBtn.title = strings.notifications;
        _bellBtn.setAttribute('aria-label', strings.notifications);
        _bellBtn.innerHTML = `
            <svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                <path d="M12 22c1.1 0 2-.9 2-2h-4c0 1.1.9 2 2 2zm6-6V11c0-3.07-1.64-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5S10.5 3.17 10.5 4v.68C7.63 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z"/>
            </svg>
        `;

        // Badge
        _badge = document.createElement('span');
        _badge.id = 'jn-badge';
        _badge.style.display = 'none';
        _bellBtn.appendChild(_badge);

        if (!_userPrefs?.hasOpenedBell) {
            _bellBtn.classList.add('jn-bell-pulse');
        }

        _bellBtn.addEventListener('click', togglePanel);
        toolbar.insertBefore(_bellBtn, toolbar.firstChild);

        // Create toast container
        if (!document.getElementById('jn-toast-container')) {
            _toastContainer = document.createElement('div');
            _toastContainer.id = 'jn-toast-container';
            document.body.appendChild(_toastContainer);
        } else {
            _toastContainer = document.getElementById('jn-toast-container');
        }

        // Start polling
        startPolling();

        console.log('[JellyNotify] loaded');
    }

    // ─── Panel ────────────────────────────────────────────────────────

    function togglePanel() {
        if (_panelOpen) {
            closePanel();
        } else {
            openPanel();
        }
    }

    async function openPanel() {
        if (_panel) _panel.remove();
        _panelOpen = true;
        _showingSettings = false;

        await resolveLanguage();
        const strings = getStrings();
        const isFirstOpen = !_userPrefs?.hasOpenedBell;

        if (isFirstOpen) {
            _bellBtn?.classList.remove('jn-bell-pulse');
            if (_userPrefs) _userPrefs.hasOpenedBell = true;
            apiRequest('/preferences/bell-opened', { method: 'POST' }).catch(() => {});
        }

        _panel = document.createElement('div');
        _panel.id = 'jn-panel';
        _panel.innerHTML = `
            <div class="jn-panel-header">
                <span class="jn-panel-title" id="jn-panel-title-text">${escapeHtml(strings.notifications)}</span>
                <div class="jn-panel-actions">
                    <button class="jn-panel-action-btn" id="jn-settings-toggle-btn" title="${escapeHtml(strings.settings)}">⚙️</button>
                    <button class="jn-panel-action-btn" id="jn-mark-all-btn">${escapeHtml(strings.markAllRead)}</button>
                    <button class="jn-panel-action-btn" id="jn-clear-btn">${escapeHtml(strings.clear)}</button>
                    <button class="jn-panel-close-btn" id="jn-close-panel-btn" title="${escapeHtml(strings.close)}">✕</button>
                </div>
            </div>
            <div class="jn-notif-list" id="jn-notif-list">
                <div class="jn-notif-empty" id="jn-loading">${escapeHtml(strings.loading)}</div>
            </div>
            <div class="jn-panel-footer">
                <span class="jn-panel-footer-text">JellyNotify</span>
            </div>
        `;

        document.body.appendChild(_panel);

        document.getElementById('jn-close-panel-btn').addEventListener('click', closePanel);
        document.getElementById('jn-mark-all-btn').addEventListener('click', markAllRead);
        document.getElementById('jn-clear-btn').addEventListener('click', clearAll);
        document.getElementById('jn-settings-toggle-btn').addEventListener('click', toggleSettingsView);

        // Close panel on outside click
        setTimeout(() => {
            document.addEventListener('click', handleOutsideClick);
        }, 100);

        await renderNotifications();

        if (isFirstOpen) {
            showGuidedTour();
        }
    }

    function closePanel() {
        _panelOpen = false;
        document.removeEventListener('click', handleOutsideClick);
        endGuidedTour();

        if (_panel) {
            _panel.classList.add('jn-panel-closing');
            setTimeout(() => {
                if (_panel) {
                    _panel.remove();
                    _panel = null;
                }
            }, 220);
        }
    }

    function handleOutsideClick(e) {
        if (e.target.closest?.('#jn-tour-tooltip')) return;
        if (_panel && !_panel.contains(e.target) && e.target !== _bellBtn && !_bellBtn.contains(e.target)) {
            closePanel();
        }
    }

    // ─── First-run guided tour ────────────────────────────────────────
    // Shown once, the very first time a user ever opens the panel (see
    // hasOpenedBell). Three steps, each highlighting a real element already on
    // screen rather than a separate mockup, so what's explained is exactly what's
    // there. Step 3 switches to the settings view first so the user actually sees
    // the language/notification-category/external-channel options being described,
    // not just the gear button that leads to them.
    let _tourStepIndex = 0;

    function tourSteps() {
        return [
            { getTarget: () => document.getElementById('jn-panel-title-text'), textKey: 'tourStep1' },
            { getTarget: () => document.getElementById('jn-mark-all-btn'), textKey: 'tourStep2' },
            {
                getTarget: () => document.querySelector('.jn-user-settings-form'),
                textKey: 'tourStep3',
                before: async () => {
                    if (!_showingSettings) {
                        await toggleSettingsView();
                    }
                }
            }
        ];
    }

    async function showGuidedTour() {
        _tourStepIndex = 0;
        await renderTourStep();
    }

    async function renderTourStep() {
        const steps = tourSteps();
        const step = steps[_tourStepIndex];
        if (!step) {
            endGuidedTour();
            return;
        }

        if (step.before) {
            await step.before();
        }

        const target = step.getTarget();
        if (!target) {
            endGuidedTour();
            return;
        }

        endGuidedTour();

        const strings = getStrings();
        target.classList.add('jn-tour-highlight');

        const tooltip = document.createElement('div');
        tooltip.id = 'jn-tour-tooltip';
        const isLastStep = _tourStepIndex === steps.length - 1;
        tooltip.innerHTML = `
            <div class="jn-tour-text">${escapeHtml(strings[step.textKey] || '')}</div>
            <div class="jn-tour-footer">
                <span class="jn-tour-step-count">${_tourStepIndex + 1} / ${steps.length}</span>
                <div class="jn-tour-actions">
                    <button class="jn-tour-skip-btn" type="button">${escapeHtml(strings.tourSkip)}</button>
                    <button class="jn-tour-next-btn" type="button">${escapeHtml(isLastStep ? strings.tourDone : strings.tourNext)}</button>
                </div>
            </div>
        `;
        document.body.appendChild(tooltip);
        positionTourTooltip(tooltip, target);

        tooltip.querySelector('.jn-tour-skip-btn').addEventListener('click', endGuidedTour);
        tooltip.querySelector('.jn-tour-next-btn').addEventListener('click', async () => {
            _tourStepIndex++;
            await renderTourStep();
        });
    }

    function positionTourTooltip(tooltip, target) {
        const rect = target.getBoundingClientRect();
        const margin = 10;

        let top = rect.bottom + margin;
        if (top + tooltip.offsetHeight > window.innerHeight - margin) {
            // Not enough room below — place above the target instead.
            top = rect.top - tooltip.offsetHeight - margin;
        }

        const maxLeft = window.innerWidth - tooltip.offsetWidth - margin;
        const left = Math.min(Math.max(rect.left, margin), Math.max(maxLeft, margin));

        tooltip.style.top = `${Math.max(top, margin)}px`;
        tooltip.style.left = `${left}px`;
    }

    function endGuidedTour() {
        document.getElementById('jn-tour-tooltip')?.remove();
        document.querySelectorAll('.jn-tour-highlight').forEach(el => el.classList.remove('jn-tour-highlight'));
    }

    async function renderNotifications() {
        const listEl = document.getElementById('jn-notif-list');
        if (!listEl) return;

        const strings = getStrings();
        try {
            const notifications = await apiRequest('/notifications');

            // Keeps the periodic poll's own "did the count go up?" check in sync with
            // whatever the admin/user just actually saw here — without this, opening the
            // panel shows the current state fine, but the next scheduled poll tick still
            // compares against the old (stale) count and re-fires the badge/toast for
            // something already viewed, moments after the panel closes.
            updateBadge((notifications || []).filter(n => !n.isRead).length);

            if (!notifications || notifications.length === 0) {
                listEl.innerHTML = `<div class="jn-notif-empty">${escapeHtml(strings.noNotifications)}</div>`;
                return;
            }

            listEl.innerHTML = notifications.map(n => `
                <div class="jn-notif-item ${n.isRead ? '' : 'unread'}" data-id="${n.id}" data-type="${n.type}">
                    ${n.thumbnailUrl
                        ? `<img class="jn-notif-poster" src="${escapeHtml(n.thumbnailUrl)}" alt="" loading="lazy" onerror="this.outerHTML='<div class=&quot;jn-notif-icon&quot;>${getIcon(n.type)}</div>'">`
                        : `<div class="jn-notif-icon">${getIcon(n.type)}</div>`}
                    <div class="jn-notif-body">
                        <div class="jn-notif-title">${escapeHtml(translateNotificationTitle(n.title))}</div>
                        <div class="jn-notif-message">${escapeHtml(n.message)}</div>
                        <div class="jn-notif-time">${formatRelativeTime(n.createdAt)}</div>
                    </div>
                </div>
            `).join('');

            // Attached once — listEl itself survives across re-renders (only its
            // innerHTML is replaced above), so this only needs wiring the first time.
            if (!listEl.dataset.jnClickWired) {
                listEl.dataset.jnClickWired = '1';
                listEl.addEventListener('click', (e) => {
                    if (e.target.closest('.jn-notif-item')) {
                        openJellyfinEnhancedRequests();
                    }
                });
            }

        } catch (err) {
            listEl.innerHTML = `<div class="jn-notif-empty">${escapeHtml(strings.loadError)}</div>`;
            console.error('[JellyNotify] Error loading notifications:', err);
        }
    }

    async function toggleSettingsView() {
        _showingSettings = !_showingSettings;
        const listEl = document.getElementById('jn-notif-list');
        const titleEl = document.getElementById('jn-panel-title-text');
        const markAllBtn = document.getElementById('jn-mark-all-btn');
        const clearBtn = document.getElementById('jn-clear-btn');
        const settingsToggleBtn = document.getElementById('jn-settings-toggle-btn');
        const strings = getStrings();

        if (_showingSettings) {
            titleEl.textContent = strings.settings;
            markAllBtn.style.display = 'none';
            clearBtn.style.display = 'none';
            settingsToggleBtn.textContent = '◀';
            settingsToggleBtn.title = strings.back;

            // Render settings form
            await renderUserSettings(listEl);
        } else {
            titleEl.textContent = strings.notifications;
            markAllBtn.style.display = '';
            clearBtn.style.display = '';
            settingsToggleBtn.textContent = '⚙️';
            settingsToggleBtn.title = strings.settings;

            listEl.innerHTML = `<div class="jn-notif-empty">${escapeHtml(strings.loading)}</div>`;
            await renderNotifications();
        }
    }

    async function renderUserSettings(container) {
        const strings = getStrings();
        container.innerHTML = `<div class="jn-notif-empty">${escapeHtml(strings.loading)}</div>`;

        try {
            _userPrefs = await apiRequest('/preferences');
            _publicSettings = await apiRequest('/public-settings');

            const hasTelegram = _publicSettings?.telegramAvailable;
            const hasDiscord = _publicSettings?.discordAvailable;
            const hasWhatsApp = _publicSettings?.whatsappAvailable;

            container.innerHTML = `
                <div class="jn-user-settings-form">
                    <div class="jn-settings-field">
                        <label class="jn-settings-label">${escapeHtml(strings.languageLabel)}</label>
                        <select id="jn-pref-language" class="jn-settings-select">
                            <option value="auto" ${_userPrefs.language === 'auto' ? 'selected' : ''}>Auto</option>
                            <option value="es-ES" ${_userPrefs.language === 'es-ES' ? 'selected' : ''}>Español (Castellano)</option>
                            <option value="ca" ${_userPrefs.language === 'ca' ? 'selected' : ''}>Català</option>
                            <option value="en-US" ${_userPrefs.language === 'en-US' ? 'selected' : ''}>English</option>
                        </select>
                    </div>
                    <div class="jn-settings-checkbox-group">
                        <label class="jn-settings-checkbox-label">
                            <input type="checkbox" id="jn-pref-notif-req" ${_userPrefs.notifyOnRequest ? 'checked' : ''}>
                            <span>${escapeHtml(strings.notifyOnRequest)}</span>
                        </label>
                        <label class="jn-settings-checkbox-label">
                            <input type="checkbox" id="jn-pref-notif-app" ${_userPrefs.notifyOnApproval ? 'checked' : ''}>
                            <span>${escapeHtml(strings.notifyOnApproval)}</span>
                        </label>
                        <label class="jn-settings-checkbox-label">
                            <input type="checkbox" id="jn-pref-notif-dl" ${_userPrefs.notifyOnDownload ? 'checked' : ''}>
                            <span>${escapeHtml(strings.notifyOnDownload)}</span>
                        </label>
                        <label class="jn-settings-checkbox-label">
                            <input type="checkbox" id="jn-pref-notif-av" ${_userPrefs.notifyOnAvailable ? 'checked' : ''}>
                            <span>${escapeHtml(strings.notifyOnAvailable)}</span>
                        </label>
                        <label class="jn-settings-checkbox-label">
                            <input type="checkbox" id="jn-pref-inapp" ${_userPrefs.jellyfinUiEnabled ? 'checked' : ''}>
                            <span>${escapeHtml(strings.inAppUi)}</span>
                        </label>
                        <label class="jn-settings-checkbox-label">
                            <input type="checkbox" id="jn-pref-sound" ${_userPrefs.soundEnabled ? 'checked' : ''}>
                            <span>${escapeHtml(strings.soundLabel)}</span>
                        </label>
                    </div>
                    ${channelConnectRow('telegram', hasTelegram, _userPrefs.telegramConnected, strings.connectTelegram, strings.disconnectTelegram)}
                    ${channelConnectRow('discord', hasDiscord, _userPrefs.discordConnected, strings.connectDiscord, strings.disconnectDiscord)}
                    ${channelConnectRow('whatsapp', hasWhatsApp, _userPrefs.whatsAppConnected, strings.connectWhatsapp, strings.disconnectWhatsapp)}
                    <button class="jn-settings-save-btn" id="jn-pref-save-btn">${escapeHtml(strings.save)}</button>
                    <div id="jn-pref-result-msg" class="jn-pref-msg" style="display:none"></div>
                </div>
            `;

            document.getElementById('jn-pref-save-btn').addEventListener('click', saveUserSettings);
            wireChannelButtons('whatsapp', hasWhatsApp, connectWhatsApp, disconnectWhatsApp);
            wireChannelButtons('telegram', hasTelegram, connectTelegram, disconnectTelegram);
            wireChannelButtons('discord', hasDiscord, connectDiscord, disconnectDiscord);
        } catch (err) {
            container.innerHTML = `<div class="jn-notif-empty">${escapeHtml(strings.loadError)}</div>`;
        }
    }

    /**
     * Renders one channel's connect/disconnect row. Connecting/disconnecting is now the
     * only on/off switch for that channel (no separate "Send to X" checkbox) — being
     * connected implies notifications are sent, disconnecting stops them. When the admin
     * hasn't configured a channel at all, its Connect button still shows (so users always
     * see all three options) but greyed out; clicking it explains why instead of trying
     * to connect.
     */
    function channelConnectRow(name, available, connected, connectLabel, disconnectLabel) {
        if (connected) {
            return `<div id="jn-${name}-connect-row"><button class="jn-whatsapp-disconnect-btn" id="jn-${name}-disconnect-btn">${escapeHtml(disconnectLabel)}</button></div>`;
        }

        const disabledClass = available ? '' : ' jn-channel-btn-disabled';
        return `<div id="jn-${name}-connect-row"><button class="jn-whatsapp-connect-btn${disabledClass}" id="jn-${name}-connect-btn">${escapeHtml(connectLabel)}</button></div>`;
    }

    /** Wires a channel's connect/disconnect buttons; an unavailable channel's Connect button shows the "not configured" message instead of starting the real flow. */
    function wireChannelButtons(name, available, connectFn, disconnectFn) {
        document.getElementById(`jn-${name}-disconnect-btn`)?.addEventListener('click', disconnectFn);
        document.getElementById(`jn-${name}-connect-btn`)?.addEventListener('click', available ? connectFn : showChannelNotConfigured);
    }

    function showChannelNotConfigured() {
        const strings = getStrings();
        const resultEl = document.getElementById('jn-pref-result-msg');
        if (resultEl) {
            resultEl.className = 'jn-pref-msg error';
            resultEl.textContent = strings.channelNotConfigured;
            resultEl.style.display = 'block';
        }
    }

    // Telegram/Discord/WhatsApp all follow the same shape now: opening a link/tab
    // in another app or site to complete pairing, then the actual bot (Telegram
    // poller, Discord OAuth callback, WhatsApp webhook) confirms it in the
    // background — there is nothing more for this tab to submit. This polls
    // /preferences for a while afterward so the panel updates itself once that
    // confirmation lands, without the user having to reopen settings manually.
    async function pollForConnection(prefField) {
        const strings = getStrings();
        const resultEl = document.getElementById('jn-pref-result-msg');
        if (resultEl) {
            resultEl.className = 'jn-pref-msg';
            resultEl.textContent = strings.waitingConnection;
            resultEl.style.display = 'block';
        }

        for (let attempt = 0; attempt < 20; attempt++) {
            await new Promise(resolve => setTimeout(resolve, 3000));
            try {
                const prefs = await apiRequest('/preferences');
                if (prefs && prefs[prefField]) {
                    _userPrefs = prefs;
                    const listEl = document.getElementById('jn-notif-list');
                    if (listEl) {
                        await renderUserSettings(listEl);
                    }
                    return;
                }
            } catch (err) {
                // Keep polling — a transient network error shouldn't abort the wait.
            }
        }

        if (resultEl) {
            resultEl.className = 'jn-pref-msg error';
            resultEl.textContent = strings.connectionTimeout;
        }
    }

    async function connectWhatsApp() {
        const strings = getStrings();
        const resultEl = document.getElementById('jn-pref-result-msg');
        try {
            const result = await apiRequest('/whatsapp/connect', { method: 'POST' });
            if (result?.waMeUrl) {
                window.open(result.waMeUrl, '_blank', 'noopener');
                await pollForConnection('whatsAppConnected');
            }
        } catch (err) {
            if (resultEl) {
                resultEl.className = 'jn-pref-msg error';
                resultEl.textContent = strings.whatsappConnectError;
                resultEl.style.display = 'block';
            }
        }
    }

    async function disconnectWhatsApp() {
        try {
            await apiRequest('/whatsapp/disconnect', { method: 'POST' });
            _userPrefs.whatsAppConnected = false;
            await renderUserSettings(document.getElementById('jn-notif-list'));
        } catch (err) {
            console.error('[JellyNotify] Error disconnecting WhatsApp:', err);
        }
    }

    async function connectTelegram() {
        const strings = getStrings();
        const resultEl = document.getElementById('jn-pref-result-msg');
        try {
            const result = await apiRequest('/telegram/connect', { method: 'POST' });
            if (result?.telegramDeepLink) {
                window.open(result.telegramDeepLink, '_blank', 'noopener');
                await pollForConnection('telegramConnected');
            }
        } catch (err) {
            if (resultEl) {
                resultEl.className = 'jn-pref-msg error';
                resultEl.textContent = strings.telegramConnectError;
                resultEl.style.display = 'block';
            }
        }
    }

    async function disconnectTelegram() {
        try {
            await apiRequest('/telegram/disconnect', { method: 'POST' });
            _userPrefs.telegramConnected = false;
            await renderUserSettings(document.getElementById('jn-notif-list'));
        } catch (err) {
            console.error('[JellyNotify] Error disconnecting Telegram:', err);
        }
    }

    async function connectDiscord() {
        const strings = getStrings();
        const resultEl = document.getElementById('jn-pref-result-msg');
        try {
            const result = await apiRequest('/discord/connect-url', { method: 'POST' });
            if (result?.authorizeUrl) {
                window.open(result.authorizeUrl, '_blank', 'noopener');
                await pollForConnection('discordConnected');
            }
        } catch (err) {
            if (resultEl) {
                resultEl.className = 'jn-pref-msg error';
                resultEl.textContent = strings.discordConnectError;
                resultEl.style.display = 'block';
            }
        }
    }

    async function disconnectDiscord() {
        try {
            await apiRequest('/discord/disconnect', { method: 'POST' });
            _userPrefs.discordConnected = false;
            await renderUserSettings(document.getElementById('jn-notif-list'));
        } catch (err) {
            console.error('[JellyNotify] Error disconnecting Discord:', err);
        }
    }

    async function saveUserSettings() {
        const strings = getStrings();
        const saveBtn = document.getElementById('jn-pref-save-btn');
        const resultEl = document.getElementById('jn-pref-result-msg');

        saveBtn.disabled = true;
        resultEl.style.display = 'none';

        const payload = {
            jellyfinUserId: _userPrefs.jellyfinUserId,
            language: document.getElementById('jn-pref-language').value,
            notifyOnRequest: document.getElementById('jn-pref-notif-req').checked,
            notifyOnApproval: document.getElementById('jn-pref-notif-app').checked,
            notifyOnDownload: document.getElementById('jn-pref-notif-dl').checked,
            notifyOnAvailable: document.getElementById('jn-pref-notif-av').checked,
            jellyfinUiEnabled: document.getElementById('jn-pref-inapp').checked,
            soundEnabled: document.getElementById('jn-pref-sound').checked
        };

        try {
            await apiRequest('/preferences', {
                method: 'PUT',
                body: JSON.stringify(payload)
            });

            // Re-resolve language and fully re-render the settings form (not just a
            // few hand-picked elements) so every label actually reflects the new
            // language once saved, including the panel title/back/save chrome.
            await resolveLanguage();
            const newStrings = getStrings();

            document.getElementById('jn-panel-title-text').textContent = newStrings.settings;
            document.getElementById('jn-settings-toggle-btn').title = newStrings.back;

            const listEl = document.getElementById('jn-notif-list');
            if (listEl) {
                await renderUserSettings(listEl);
            }

            const refreshedResultEl = document.getElementById('jn-pref-result-msg');
            if (refreshedResultEl) {
                refreshedResultEl.className = 'jn-pref-msg success';
                refreshedResultEl.textContent = newStrings.preferencesSaved;
                refreshedResultEl.style.display = 'block';
                setTimeout(() => { refreshedResultEl.style.display = 'none'; }, 3000);
            }
        } catch (err) {
            resultEl.className = 'jn-pref-msg error';
            resultEl.textContent = strings.preferencesError;
            resultEl.style.display = 'block';
            saveBtn.disabled = false;
        }
    }

    async function markAllRead() {
        try {
            await apiRequest('/notifications/mark-all-read', { method: 'POST' });
            await renderNotifications();
            updateBadge(0);
        } catch (err) {
            console.error('[JellyNotify] Error marking as read:', err);
        }
    }

    async function clearAll() {
        const strings = getStrings();
        if (!confirm(strings.confirmClear)) return;
        try {
            await apiRequest('/notifications', { method: 'DELETE' });
            await renderNotifications();
            updateBadge(0);
        } catch (err) {
            console.error('[JellyNotify] Error clearing notifications:', err);
        }
    }

    // ─── Badge ────────────────────────────────────────────────────────

    function updateBadge(count) {
        if (!_badge) return;
        _lastUnreadCount = count;
        if (count > 0) {
            _badge.textContent = count > 99 ? '99+' : count;
            _badge.style.display = 'flex';
        } else {
            _badge.style.display = 'none';
        }
    }

    // ─── Jellyfin Enhanced integration ─────────────────────────────────
    // Clicking a notification (toast or panel item) tries to open Jellyfin Enhanced's
    // own Requests/Downloads tab, which shows live Seerr/*arr progress — JellyNotify
    // has no such page of its own. Selectors adapted from a working reference script
    // the user provided (references/working-notificator-js/notify-dispatcher-working.js).
    // If nothing matches (Jellyfin Enhanced isn't installed, or doesn't expose that tab),
    // a toast explains it instead of silently doing nothing.

    const JF_ENHANCED_REQUESTS_DOCK_SELECTORS = [
        'button[data-jf-dock-key="requests"]',
        '[data-jf-dock-key="requests"]',
        'button[data-jf-request-custom-patched="1"]'
    ];

    const JF_ENHANCED_REQUESTS_FALLBACK_SELECTORS = [
        '.je-nav-downloads-item',
        'a[is="emby-linkbutton"][data-itemid="Jellyfin.Plugin.JellyfinEnhanced.DownloadsPage"]',
        '[data-itemid="Jellyfin.Plugin.JellyfinEnhanced.DownloadsPage"]',
        '[href="#/downloads"]',
        '[href*="#/downloads"]'
    ];

    function isAttachedAndVisible(el) {
        if (!el || !document.body.contains(el)) return false;
        try {
            const rect = el.getBoundingClientRect();
            const style = window.getComputedStyle(el);
            if (style.display === 'none' || style.visibility === 'hidden') return false;
            return rect.width > 0 && rect.height > 0;
        } catch (e) {
            return true;
        }
    }

    function findFirstUsableElement(selectors) {
        for (const selector of selectors) {
            try {
                const matches = document.querySelectorAll(selector);
                for (const el of matches) {
                    if (isAttachedAndVisible(el) && !el.disabled) return el;
                }
            } catch (e) {
                // Invalid/unsupported selector — skip it.
            }
        }
        return null;
    }

    function openJellyfinEnhancedRequests() {
        const dockButton = findFirstUsableElement(JF_ENHANCED_REQUESTS_DOCK_SELECTORS)
            || findFirstUsableElement(JF_ENHANCED_REQUESTS_FALLBACK_SELECTORS);

        if (dockButton) {
            dockButton.click();
            return;
        }

        try {
            const je = window.JellyfinEnhanced;
            if (je && je.downloadsPage && typeof je.downloadsPage.showPage === 'function') {
                je.downloadsPage.showPage();
                return;
            }
        } catch (e) {
            // window.JellyfinEnhanced not shaped as expected — fall through to the notice.
        }

        const strings = getStrings();
        showToast({
            type: 'info',
            title: strings.jellyfinEnhancedRequiredTitle,
            message: strings.jellyfinEnhancedRequiredMessage
        });
    }

    // ─── Toasts ───────────────────────────────────────────────────────

    function showToast(notification) {
        if (!_toastContainer) return;

        // Limit toast stack size
        const existing = _toastContainer.querySelectorAll('.jn-toast');
        if (existing.length >= MAX_TOAST_STACK) {
            existing[existing.length - 1].remove();
        }

        const toast = document.createElement('div');
        toast.className = 'jn-toast';
        toast.dataset.type = notification.type;
        toast.innerHTML = `
            <div class="jn-toast-title">${getIcon(notification.type)} ${escapeHtml(translateNotificationTitle(notification.title))}</div>
            <div class="jn-toast-message">${escapeHtml(notification.message)}</div>
        `;

        // The "Jellyfin Enhanced required" notice is itself shown via showToast() (type
        // 'info', not a real backend NotificationType) — clicking it shouldn't re-trigger
        // the same navigation attempt that produced it.
        toast.addEventListener('click', () => {
            dismissToast(toast);
            if (notification.type !== 'info') {
                openJellyfinEnhancedRequests();
            }
        });
        _toastContainer.insertBefore(toast, _toastContainer.firstChild);

        // Optional: play sound if user preference has sound enabled
        if (_userPrefs?.soundEnabled) {
            playNotificationSound();
        }

        setTimeout(() => dismissToast(toast), TOAST_DURATION_MS);
    }

    function playNotificationSound() {
        try {
            // Simple synthesized notification sound using Web Audio API
            const context = new (window.AudioContext || window.webkitAudioContext)();
            const osc = context.createOscillator();
            const gain = context.createGain();

            osc.type = 'sine';
            osc.frequency.setValueAtTime(587.33, context.currentTime); // D5
            osc.frequency.setValueAtTime(880, context.currentTime + 0.1); // A5

            gain.gain.setValueAtTime(0.1, context.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.01, context.currentTime + 0.3);

            osc.connect(gain);
            gain.connect(context.destination);

            osc.start();
            osc.stop(context.currentTime + 0.3);
        } catch (e) {
            // Audio context not allowed or failed
        }
    }

    function dismissToast(toast) {
        if (!toast.parentNode) return;
        toast.classList.add('jn-toast-dismiss');
        setTimeout(() => toast.remove(), 320);
    }

    // ─── Instant push (WebSocket) ───────────────────────────────────────
    // Jellyfin's own session WebSocket has no "custom app notification" message type of
    // its own — SessionMessageType is a fixed, closed enum (confirmed against the SDK) —
    // so the server reuses UserDataChanged as a low-traffic envelope and tags the payload
    // with a source marker (see NotificationDispatcher.PushToOpenSessionsAsync). This is
    // additive: pollNotifications() below keeps running as a fallback for tabs that missed
    // the push (offline at the moment it was sent, connected after, etc.).
    let _wsListenerWired = false;

    function wireInstantPushListener() {
        if (_wsListenerWired || typeof Events === 'undefined' || typeof ApiClient === 'undefined') return;
        _wsListenerWired = true;

        Events.on(ApiClient, 'message', (e, msg) => {
            if (!msg || msg.MessageType !== 'UserDataChanged') return;
            const data = msg.Data;
            if (!data || data.source !== 'JellyNotify') return;

            showToast({ type: data.type, title: data.title, message: data.message });
            updateBadge(_lastUnreadCount + 1);

            if (_panelOpen && !_showingSettings) {
                renderNotifications();
            }
        });
    }

    // ─── Polling ──────────────────────────────────────────────────────

    function startPolling() {
        if (_pollTimer) return;
        wireInstantPushListener();
        pollNotifications();
        _pollTimer = setInterval(pollNotifications, POLL_INTERVAL_MS);
    }

    async function pollNotifications() {
        try {
            const data = await apiRequest('/notifications/unread-count');
            const newCount = data?.unreadCount ?? 0;

            if (newCount > _lastUnreadCount) {
                // New notifications — load to show toasts
                const notifications = await apiRequest('/notifications');
                const newNotifs = notifications.filter(n => !n.isRead).slice(0, newCount - _lastUnreadCount);
                
                // Avoid showing toast on initial frontend load
                if (!_isFirstPoll) {
                    newNotifs.forEach(n => showToast(n));
                }

                // Update panel if open
                if (_panelOpen && !_showingSettings) {
                    await renderNotifications();
                }
            }

            _isFirstPoll = false;
            updateBadge(newCount);
        } catch (err) {
            // Silent in polling — don't bother users with network errors
        }
    }

    // ─── Config page ─────────────────────────────────────────────────

    async function initConfigPage() {
        if (!document.getElementById('jellynotify-config-page')) return;

        initTabs();
        bindConfigEvents();

        // Resolving the admin's own display language and translating the static
        // page labels happens before/alongside loading the saved form values -
        // without this the config page never localized at all (every label was
        // hardcoded English HTML), which is why "Auto" appeared to do nothing.
        await resolveLanguage();
        applyConfigTranslations();

        await loadConfig();
        setInterval(pollWebhookStatuses, WEBHOOK_STATUS_POLL_INTERVAL_MS);
    }

    /**
     * Applies the currently-resolved locale's `config.*` strings to every translatable
     * element on the page: [data-i18n] for plain text, [data-i18n-placeholder] for
     * input placeholders, and [data-i18n-html] for elements whose content has nested
     * tags (the setup guides' <strong>/<a>/<code> markup) that plain textContent would
     * destroy. The innerHTML source is always this plugin's own locale JSON, never
     * user input, so there is no injection risk in setting it directly.
     */
    function applyConfigTranslations(root = document) {
        const strings = getStrings();
        const cfg = strings.config || {};
        const resolve = key => (key.startsWith('config.') ? cfg[key.slice('config.'.length)] : strings[key]);

        root.querySelectorAll('[data-i18n]').forEach(el => {
            const value = resolve(el.getAttribute('data-i18n'));
            if (value) {
                el.textContent = value;
            }
        });

        root.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
            const value = resolve(el.getAttribute('data-i18n-placeholder'));
            if (value) {
                el.placeholder = value;
            }
        });

        root.querySelectorAll('[data-i18n-html]').forEach(el => {
            const value = resolve(el.getAttribute('data-i18n-html'));
            if (value) {
                el.innerHTML = value;
            }
        });

        root.querySelectorAll('[data-i18n-title]').forEach(el => {
            const value = resolve(el.getAttribute('data-i18n-title'));
            if (value) {
                el.title = value;
            }
        });
    }

    /**
     * Renders a "last webhook received" confirmation line into `el` — used so
     * clicking a Test button in Seerr/Sonarr/Radarr's own webhook settings has a
     * visible confirmation that JellyNotify actually received the call, instead of
     * it being silently swallowed with no feedback either way.
     */
    function renderWebhookStatus(el, isoTimestamp) {
        if (!el) return;
        const strings = getStrings();
        const cfg = strings.config || {};

        if (!isoTimestamp) {
            el.innerHTML = `<span class="jn-field-hint-icon">⏳</span><span>${escapeHtml(cfg.webhookNeverReceived || 'No webhook call received yet — click Test to check.')}</span>`;
            return;
        }

        const when = new Date(isoTimestamp).toLocaleString();
        const template = cfg.webhookLastReceived || 'Last webhook call received: {when}';
        el.innerHTML = `<span class="jn-field-hint-icon">✅</span><span>${escapeHtml(template.replace('{when}', when))}</span>`;
    }

    /**
     * Renders the read-only "instant notifications via webhook" connection status —
     * replaces what used to be a manual on/off checkbox. Whether this shows connected is no
     * longer something the admin sets directly here; it just reflects whatever the backend
     * already confirmed and saved (via auto-configure on save, or a successful capability
     * check), matching the same visual language as the General tab's overview cards.
     */
    function renderWebhookConnectionStatus(el, connected) {
        if (!el) return;
        const strings = getStrings();
        const cfg = strings.config || {};
        el.classList.toggle('jn-overview-connected', !!connected);
        const iconEl = el.querySelector('.jn-overview-status-icon');
        if (iconEl) {
            iconEl.textContent = connected ? '✓' : '✗';
            iconEl.setAttribute('aria-label', connected
                ? (cfg.webhookConnectedLabel || 'Connected')
                : (cfg.webhookDisconnectedLabel || 'Disconnected'));
        }
    }

    /**
     * Copies text to the clipboard, trying the Clipboard API first, then a
     * temporary off-screen textarea + execCommand('copy') — needed because the
     * Clipboard API can be blocked without a clipboard-write permission inside
     * the iframe Jellyfin loads this config page in (see the iframe note at the
     * top of this file) — and finally a native prompt() as a last resort, so
     * the value is never truly unreachable even if both copy mechanisms fail.
     */
    async function copyTextToClipboard(text) {
        if (!text) return false;

        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (err) {
            // Fall through to the legacy fallback below.
        }

        try {
            const textarea = document.createElement('textarea');
            textarea.value = text;
            textarea.style.position = 'fixed';
            textarea.style.opacity = '0';
            document.body.appendChild(textarea);
            textarea.focus();
            textarea.select();
            const ok = document.execCommand('copy');
            document.body.removeChild(textarea);
            if (ok) return true;
        } catch (err) {
            // Fall through to the prompt below.
        }

        const strings = getStrings();
        window.prompt((strings.config && strings.config.copyManualPrompt) || 'Copy this URL:', text);
        return false;
    }

    /**
     * Wires a `.jn-copy-btn` to copy whatever `getValue()` returns at click time
     * (a live lookup, not a value captured once — the *arr webhook URL is the
     * same shared value across every instance card, updated in place rather
     * than re-rendered), and briefly swaps its icon/label to confirm success.
     * Listeners are attached directly to each button rather than delegated on
     * an ancestor — Jellyfin's `emby-button` custom element does not reliably
     * let clicks bubble up to a delegated listener higher in the page.
     */
    function wireCopyButton(button, getValue) {
        if (!button) return;

        button.addEventListener('click', async () => {
            const text = getValue();
            const success = await copyTextToClipboard(text);
            if (!success) return;

            const icon = button.querySelector('.material-icons');
            const label = Array.from(button.querySelectorAll('span')).find(s => !s.classList.contains('material-icons'));
            const strings = getStrings();
            const copiedText = (strings.config && strings.config.copiedLabel) || 'Copied!';
            const originalLabel = label ? label.textContent : null;
            const originalTitle = button.title;

            button.classList.add('jn-copy-btn-done');
            if (icon) icon.textContent = 'check';
            if (label) {
                label.textContent = copiedText;
            } else {
                button.title = copiedText;
            }

            setTimeout(() => {
                button.classList.remove('jn-copy-btn-done');
                if (icon) icon.textContent = 'content_copy';
                if (label && originalLabel !== null) {
                    label.textContent = originalLabel;
                } else {
                    button.title = originalTitle;
                }
            }, 1500);
        });
    }

    // ─── Config page tabs ──────────────────────────────────────────────

    function initTabs() {
        const buttons = document.querySelectorAll('.jn-tab-btn');
        buttons.forEach(btn => {
            btn.addEventListener('click', () => activateTab(btn.dataset.tab));
        });
    }

    function activateTab(tabName) {
        document.querySelectorAll('.jn-tab-btn').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.tab === tabName);
        });
        document.querySelectorAll('.jn-tab-panel').forEach(panel => {
            panel.classList.toggle('jn-hidden', panel.dataset.panel !== tabName);
        });

        if (tabName === 'diagnostics') {
            loadDiagnostics();
        }
    }

    async function loadConfig() {
        try {
            const config = await apiRequest('/Admin/config');
            await applyConfigToForm(config);
        } catch (err) {
            console.error('[JellyNotify] Error loading config:', err);
        }
    }

    /**
     * Re-fetches just the webhook "last received" timestamps and re-renders their
     * status lines, without touching any other field on the page. Needed because
     * the event that updates these — clicking Test in Seerr's or Sonarr/Radarr's
     * own webhook settings — happens entirely outside this page, so there is no
     * click here to hang a one-off refresh off of; this page has to go check for
     * itself on a short interval instead. Best-effort: a failed fetch (e.g. a
     * transient network hiccup) is silently skipped rather than surfaced, since
     * this runs unattended in the background the whole time the page is open.
     */
    async function pollWebhookStatuses() {
        let config;
        try {
            config = await apiRequest('/Admin/config');
        } catch (err) {
            return;
        }

        if (!config) return;

        if (_adminConfig) {
            _adminConfig.seerrWebhookLastReceivedAt = config.seerrWebhookLastReceivedAt;
            _adminConfig.arrWebhookLastReceivedAt = config.arrWebhookLastReceivedAt;
        }

        renderWebhookStatus(document.getElementById('seerr-webhook-status'), config.seerrWebhookLastReceivedAt);
        document.querySelectorAll('.arr-webhook-status').forEach(el => {
            renderWebhookStatus(el, config.arrWebhookLastReceivedAt);
        });
    }

    async function applyConfigToForm(config) {
        if (!config) return;

        _adminConfig = config;
        const strings = getStrings();

        // Seerr
        setChecked('seerr-enabled', config.seerrEnabled);
        setValue('seerr-url', config.seerrServerUrl || '');
        setValue('seerr-type', String(config.seerrType === 'Jellyseerr' ? 1 : 0));
        setChecked('seerr-ignore-ssl', config.seerrIgnoreSslErrors);
        renderWebhookConnectionStatus(document.getElementById('seerr-webhook-connection-status'), config.seerrWebhookEnabled);
        const seerrWebhookBtn = document.getElementById('seerr-webhook-copy-btn');
        if (seerrWebhookBtn) seerrWebhookBtn.dataset.url = config.seerrWebhookUrl || '';
        renderWebhookStatus(document.getElementById('seerr-webhook-status'), config.seerrWebhookLastReceivedAt);

        // The shared *arr webhook toggle/URL is rendered once per Sonarr/Radarr
        // instance (see addArrInstance) rather than as a single global element —
        // addArrInstance reads config.arrWebhookEnabled/arrWebhookUrl from
        // _adminConfig (set above) each time an instance row is built.

        // Don't fill API key — only show whether it's set
        if (config.seerrHasApiKey) {
            const keyInput = document.getElementById('seerr-apikey');
            if (keyInput) keyInput.placeholder = (strings.config && strings.config.apiKeyConfiguredPlaceholder) || '(configured — leave blank to keep)';
        }
        const seerrIconUrl = `https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/svg/${config.seerrType === 'Jellyseerr' ? 'jellyseerr' : 'overseerr'}.svg`;
        const seerrIcon = document.getElementById('seerr-section-icon');
        if (seerrIcon) seerrIcon.src = seerrIconUrl;
        const seerrTabIcon = document.getElementById('seerr-tab-icon');
        if (seerrTabIcon) seerrTabIcon.src = seerrIconUrl;

        renderGeneralOverview(config);

        // Notifications
        const ns = config.notificationSettings;
        if (ns) {
            setChecked('notif-enabled', ns.enabled);
            setValue('notif-dedup-window', ns.deduplicationWindowMinutes);
            setValue('notif-downloading-threshold', ns.downloadingNotifyThresholdPercent);
        }

        // Default language
        setValue('notif-language', config.defaultLanguage || 'auto');

        // resolveLanguage() (called before this, at page init) has no saved config to read
        // yet, so on an explicit (non-"auto") Default language it can only guess from the
        // browser/Jellyfin session — meant for the bell/panel's own "auto" resolution, not
        // this page. Now that the real saved value is known, it's authoritative for what
        // language the config page itself renders in: override and re-translate if it
        // differs from that guess (this is what was making the whole page silently revert
        // to the browser's language on every reload, even though the dropdown itself still
        // correctly showed the saved choice).
        const savedLanguage = normalizeToSupportedLang(config.defaultLanguage);
        if (savedLanguage && savedLanguage !== _currentLang) {
            _currentLang = savedLanguage;
            await loadLocale(_currentLang);
            applyConfigTranslations();
        }

        setValue('notif-retention-days', config.notificationRetentionDays ?? 30);

        // Discord
        setChecked('discord-enabled', config.discordEnabled);
        if (config.discordHasWebhook) {
            const wh = document.getElementById('discord-webhook');
            if (wh) wh.placeholder = (strings.config && strings.config.apiKeyConfiguredPlaceholder) || '(configured — leave blank to keep)';
        }
        if (config.discordHasBotToken) {
            const bt = document.getElementById('discord-bottoken');
            if (bt) bt.placeholder = (strings.config && strings.config.apiKeyConfiguredPlaceholder) || '(configured — leave blank to keep)';
        }
        setValue('discord-clientid', config.discordClientId || '');
        if (config.discordHasClientSecret) {
            const cs = document.getElementById('discord-clientsecret');
            if (cs) cs.placeholder = (strings.config && strings.config.apiKeyConfiguredPlaceholder) || '(configured — leave blank to keep)';
        }
        setValue('discord-redirect-uri', `${window.location.origin}/JellyNotify/discord/oauth/callback`);

        // Telegram
        setChecked('telegram-enabled', config.telegramEnabled);
        if (config.telegramHasBotToken) {
            const bt = document.getElementById('telegram-token');
            if (bt) bt.placeholder = (strings.config && strings.config.apiKeyConfiguredPlaceholder) || '(configured — leave blank to keep)';
        }
        setValue('telegram-botusername', config.telegramBotUsername || '');

        // WhatsApp
        setChecked('whatsapp-enabled', config.whatsAppEnabled);
        setValue('whatsapp-phone', config.whatsAppPhoneNumber || '');
        if (config.whatsAppHasAccessToken) {
            const at = document.getElementById('whatsapp-access-token');
            if (at) at.placeholder = (strings.config && strings.config.apiKeyConfiguredPlaceholder) || '(configured — leave blank to keep)';
        }
        setValue('whatsapp-phone-number-id', config.whatsAppPhoneNumberId || '');
        setValue('whatsapp-verify-token', config.whatsAppVerifyToken || '');
        setValue('whatsapp-webhook-url', config.whatsAppWebhookUrl || `${window.location.origin}/JellyNotify/whatsapp/webhook`);

        // Web injection toggle (checkbox is "enabled", config flag is "disabled" — inverted)
        setChecked('script-injection-enabled', !config.disableScriptInjectionMiddleware);

        // Sonarr instances
        const sonarrContainer = document.getElementById('sonarr-instances');
        if (sonarrContainer && config.sonarrInstances) {
            sonarrContainer.innerHTML = '';
            config.sonarrInstances.forEach(inst => addArrInstance('sonarr', inst));
        }

        // Radarr instances
        const radarrContainer = document.getElementById('radarr-instances');
        if (radarrContainer && config.radarrInstances) {
            radarrContainer.innerHTML = '';
            config.radarrInstances.forEach(inst => addArrInstance('radarr', inst));
        }
    }

    /** Renders the "connected services" summary card in the General tab from the admin config already loaded. */
    function renderGeneralOverview(config) {
        const container = document.getElementById('general-overview');
        if (!container) return;

        const strings = getStrings();
        const cfg = strings.config || {};
        const iconBase = 'https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/svg';

        const services = [
            {
                name: cfg.seerrTitle || 'Serr',
                icon: `${iconBase}/${config.seerrType === 'Jellyseerr' ? 'jellyseerr' : 'overseerr'}.svg`,
                connected: !!(config.seerrEnabled && config.seerrHasApiKey && config.seerrServerUrl)
            },
            {
                name: cfg.tabSonarr || 'Sonarr',
                icon: `${iconBase}/sonarr.svg`,
                connected: (config.sonarrInstances || []).some(i => i.enabled)
            },
            {
                name: cfg.tabRadarr || 'Radarr',
                icon: `${iconBase}/radarr.svg`,
                connected: (config.radarrInstances || []).some(i => i.enabled)
            },
            {
                name: 'Discord',
                icon: `${iconBase}/discord.svg`,
                connected: !!(config.discordEnabled && (config.discordHasWebhook || config.discordHasBotToken))
            },
            {
                name: 'Telegram',
                icon: `${iconBase}/telegram.svg`,
                connected: !!(config.telegramEnabled && config.telegramHasBotToken && config.telegramBotUsername)
            },
            {
                name: 'WhatsApp',
                icon: `${iconBase}/whatsapp.svg`,
                connected: !!(config.whatsAppEnabled && config.whatsAppPhoneNumber)
            }
        ];

        container.innerHTML = services.map(s => `
            <div class="jn-overview-item ${s.connected ? 'jn-overview-connected' : ''}">
                <img class="jn-overview-icon" src="${s.icon}" alt="" aria-hidden="true">
                <span class="jn-overview-name">${escapeHtml(s.name)}</span>
                <span class="jn-overview-status-icon" aria-label="${s.connected ? 'connected' : 'not configured'}">${s.connected ? '✓' : '✗'}</span>
            </div>
        `).join('');
    }

    function bindConfigEvents() {
        // Copy-to-clipboard buttons for the two singular webhook URLs (WhatsApp,
        // Seerr) — the shared *arr one is wired per-instance in addArrInstance,
        // since it's cloned once per Sonarr/Radarr instance card.
        wireCopyButton(document.getElementById('whatsapp-webhook-copy-btn'), () =>
            document.getElementById('whatsapp-webhook-url')?.value || '');
        wireCopyButton(document.getElementById('seerr-webhook-copy-btn'), () =>
            document.getElementById('seerr-webhook-copy-btn')?.dataset.url || '');

        // Live language preview: switching the Default language selector re-applies
        // translations immediately, instead of only taking effect after a save
        // (and previously, not taking effect at all - the config page had no
        // translation mechanism whatsoever).
        document.getElementById('notif-language')?.addEventListener('change', async (e) => {
            const value = e.target.value;
            if (value === 'auto') {
                await resolveLanguage();
            } else {
                _currentLang = value;
                await loadLocale(_currentLang);
            }
            applyConfigTranslations();
        });

        // Seerr test
        const seerrTestBtn = document.getElementById('seerr-test-btn');
        if (seerrTestBtn) {
            seerrTestBtn.addEventListener('click', testSeerr);
        }
        document.getElementById('seerr-sample-btn')?.addEventListener('click', sampleSeerrNotification);
        document.getElementById('seerr-webhook-autoconfig-btn')?.addEventListener('click', autoConfigureSeerrWebhook);

        // Channel tests
        document.getElementById('discord-test-btn')?.addEventListener('click', testDiscord);
        document.getElementById('telegram-test-btn')?.addEventListener('click', testTelegram);
        document.getElementById('whatsapp-test-btn')?.addEventListener('click', testWhatsApp);
        document.getElementById('telegram-detect-chatid-btn')?.addEventListener('click', detectTelegramChatId);

        // Discord bot invite link — built entirely client-side (only needs the
        // public Client ID, no secret), since it's just a normal OAuth2 "add bot
        // to a server" URL with scope=bot.
        document.getElementById('discord-invite-btn')?.addEventListener('click', () => {
            const clientId = document.getElementById('discord-clientid')?.value?.trim();
            if (!clientId) {
                showSaveResult(false, 'Set the Discord Client ID first.');
                return;
            }
            const inviteUrl = `https://discord.com/oauth2/authorize?client_id=${encodeURIComponent(clientId)}&scope=bot&permissions=0`;
            window.open(inviteUrl, '_blank', 'noopener');
        });

        // Save config
        const saveBtn = document.getElementById('save-config-btn');
        if (saveBtn) {
            saveBtn.addEventListener('click', saveConfig);
        }

        // Test notification
        const testBtn = document.getElementById('test-notification-btn');
        if (testBtn) {
            testBtn.addEventListener('click', sendTestNotification);
        }

        // Add instances
        document.getElementById('sonarr-add-btn')?.addEventListener('click', () => addArrInstance('sonarr'));
        document.getElementById('radarr-add-btn')?.addEventListener('click', () => addArrInstance('radarr'));

        // Diagnostics actions
        document.getElementById('diag-refresh-btn')?.addEventListener('click', loadDiagnostics);
        document.getElementById('sync-now-btn')?.addEventListener('click', runSyncNow);
        document.getElementById('reset-baseline-btn')?.addEventListener('click', resetBaseline);
    }

    // ─── Diagnostics ────────────────────────────────────────────────

    async function loadDiagnostics() {
        const resultEl = document.getElementById('diag-result');
        const strings = getStrings();
        const cfg = strings.config || {};
        try {
            const diag = await apiRequest('/Admin/diagnostics');
            setText('diag-version', diag.pluginVersion);
            setDiagStatus('diag-injection', diag.webInjectionActive, diag.webInjectionActive ? 'Active' : 'Inactive');
            setText('diag-server-version', diag.serverVersion || '—');
            setText('diag-last-sync', diag.lastSuccessfulSyncUtc ? formatRelativeTime(diag.lastSuccessfulSyncUtc) : 'Never');
            setDiagStatus('diag-last-error', !diag.lastSyncError, diag.lastSyncError || 'None', true);
            setText('diag-notif-count', diag.totalNotificationCount ?? '0');

            const updateEl = document.getElementById('diag-update');
            if (updateEl) {
                if (diag.updateCheckError) {
                    updateEl.textContent = '—';
                } else if (diag.updateAvailable) {
                    updateEl.textContent = fmt(cfg.updateAvailable || 'Update available: {0}', diag.latestVersion);
                    updateEl.classList.add('jn-warn');
                } else {
                    updateEl.textContent = cfg.upToDate || 'Up to date';
                    updateEl.classList.remove('jn-warn');
                }
            }
        } catch (err) {
            if (resultEl) {
                resultEl.className = 'jn-result-msg error';
                resultEl.textContent = 'Error loading diagnostics';
                resultEl.style.display = 'block';
            }
        }
    }

    function setDiagStatus(id, isOk, text, invertColor) {
        const el = document.getElementById(id);
        if (!el) return;
        el.textContent = text;
        el.classList.remove('jn-ok', 'jn-warn', 'jn-error');
        el.classList.add(isOk ? 'jn-ok' : (invertColor ? 'jn-error' : 'jn-warn'));
    }

    async function runSyncNow() {
        const resultEl = document.getElementById('diag-result');
        try {
            await apiRequest('/Admin/sync-now', { method: 'POST' });
            showDiagResult(true, 'Sync started.');
            setTimeout(loadDiagnostics, 1500);
        } catch (err) {
            showDiagResult(false, 'Error starting sync.');
        }
    }

    async function resetBaseline() {
        const resultEl = document.getElementById('diag-result');
        try {
            await apiRequest('/Admin/reset-baseline', { method: 'POST' });
            showDiagResult(true, 'Baseline reset — the next sync will not notify pre-existing items.');
        } catch (err) {
            showDiagResult(false, 'Error resetting baseline.');
        }
    }

    function showDiagResult(success, msg) {
        const el = document.getElementById('diag-result');
        if (!el) return;
        el.className = `jn-result-msg ${success ? 'success' : 'error'}`;
        el.textContent = msg;
        el.style.display = 'block';
        setTimeout(() => { el.style.display = 'none'; }, 4000);
    }

    function setText(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text;
    }

    function addArrInstance(type, data = null) {
        const container = document.getElementById(`${type}-instances`);
        if (!container) return;

        const template = document.getElementById('arr-instance-template');
        const clone = template.content.cloneNode(true);
        const wrapper = clone.querySelector('.jn-arr-instance');

        const instanceId = data?.id || generateInstanceId();
        wrapper.dataset.instanceId = instanceId;
        wrapper.dataset.instanceType = type;

        const strings = getStrings();
        applyConfigTranslations(wrapper);

        if (data) {
            wrapper.querySelector('.jn-arr-instance-name').textContent = data.name || strings.instanceUnnamed;
            wrapper.querySelector('.arr-enabled').checked = data.enabled !== false;
            wrapper.querySelector('.arr-name').value = data.name || '';
            wrapper.querySelector('.arr-url').value = data.serverUrl || '';
            if (data.hasApiKey) {
                wrapper.querySelector('.arr-apikey').placeholder = (strings.config && strings.config.apiKeyConfiguredPlaceholder) || '(configured — leave blank to keep)';
            }
            setSelectValuePreservingUnknown(wrapper.querySelector('.arr-polling'), data.pollingIntervalSeconds || 300);
            wrapper.querySelector('.arr-ignore-ssl').checked = data.ignoreSslErrors || false;
        }

        // The *arr webhook connection status/URL is a single shared setting (one secret
        // covers every Sonarr/Radarr instance), but shown once per instance card for
        // visibility — every copy on the page reflects the same value, populated from the
        // config already loaded for the page. It's read-only now: whether this shows
        // connected is decided entirely by the backend (auto-configure on save, or a
        // successful capability check), never by the admin clicking anything here.
        const webhookCopyBtn = wrapper.querySelector('.arr-webhook-copy-btn');
        renderWebhookConnectionStatus(wrapper.querySelector('.arr-webhook-connection-status'), _adminConfig && _adminConfig.arrWebhookEnabled);
        webhookCopyBtn.dataset.url = (_adminConfig && _adminConfig.arrWebhookUrl) || '';
        wireCopyButton(webhookCopyBtn, () => webhookCopyBtn.dataset.url || '');
        renderWebhookStatus(wrapper.querySelector('.arr-webhook-status'), _adminConfig && _adminConfig.arrWebhookLastReceivedAt);

        // Update header name on input
        wrapper.querySelector('.arr-name').addEventListener('input', e => {
            wrapper.querySelector('.jn-arr-instance-name').textContent = e.target.value || strings.instanceUnnamed;
        });

        // Delete instance
        wrapper.querySelector('.arr-delete-btn').addEventListener('click', () => {
            wrapper.remove();
        });

        // Test connection
        wrapper.querySelector('.arr-test-btn').addEventListener('click', async () => {
            const resultEl = wrapper.querySelector('.arr-test-result');
            const webhookResultEl = wrapper.querySelector('.arr-webhook-capability-result');
            resultEl.style.display = 'block';
            resultEl.className = 'arr-test-result jn-result-msg';
            resultEl.textContent = strings.testing;
            if (webhookResultEl) webhookResultEl.style.display = 'none';

            try {
                const endpoint = type === 'sonarr'
                    ? `/Admin/test/sonarr/${instanceId}`
                    : `/Admin/test/radarr/${instanceId}`;

                const result = await apiRequest(endpoint, { method: 'POST' });
                resultEl.className = `arr-test-result jn-result-msg ${result.success ? 'success' : 'error'}`;
                resultEl.textContent = result.message;
                renderWebhookCapabilityResult(webhookResultEl, result, 'arr-webhook-capability-result');
            } catch (err) {
                resultEl.className = 'arr-test-result jn-result-msg error';
                resultEl.textContent = strings.saveFirst;
                if (webhookResultEl) webhookResultEl.style.display = 'none';
            }
        });

        // Sample notification — fetches real counts from this instance (series/movies
        // added, anything currently downloading) and shows what a real notification
        // would look like, without waiting for an actual event.
        wrapper.querySelector('.arr-sample-btn').addEventListener('click', async () => {
            const resultEl = wrapper.querySelector('.arr-sample-result');
            resultEl.style.display = 'block';
            resultEl.className = 'arr-sample-result jn-result-msg';
            resultEl.textContent = strings.testing;

            try {
                const endpoint = type === 'sonarr'
                    ? `/Admin/test/sonarr/${instanceId}/sample`
                    : `/Admin/test/radarr/${instanceId}/sample`;

                const result = await apiRequest(endpoint, { method: 'POST' });
                resultEl.className = `arr-sample-result jn-result-msg ${result.success ? 'success' : 'error'}`;
                resultEl.textContent = result.message;
            } catch (err) {
                resultEl.className = 'arr-sample-result jn-result-msg error';
                resultEl.textContent = strings.saveFirst;
            }
        });

        // Create webhook automatically — on success, also reflects the newly-enabled
        // shared toggle across every instance card on the page (same sync the manual
        // checkbox change listener above already performs). Also invoked automatically
        // after every config save (see saveConfig()) — polling is a backstop, not the
        // primary delivery path, so this shouldn't need a separate manual click once
        // the instance is configured.
        wrapper.querySelector('.arr-webhook-autoconfig-btn').addEventListener('click', () => runArrWebhookAutoConfigure(wrapper, type, instanceId));

        container.appendChild(clone);
    }

    /**
     * Renders the webhook-capability half of a connection test result into its own element,
     * kept separate from the connection result itself — a successful connection with a failed
     * webhook check must not read as one all-green message. `webhookCapable` is null/undefined
     * for connection tests that don't check this (Discord/Telegram/WhatsApp), in which case the
     * element is just hidden. `extraClass` (e.g. 'arr-webhook-capability-result') is re-applied
     * on every render since it's overwritten along with the rest of className — needed so a
     * class-based lookup (cloned per-instance elements) still finds the element next time.
     */
    function renderWebhookCapabilityResult(el, result, extraClass) {
        if (!el) return;
        const baseClass = extraClass ? `${extraClass} jn-result-msg` : 'jn-result-msg';
        if (result.webhookCapable === null || result.webhookCapable === undefined) {
            el.style.display = 'none';
            return;
        }

        el.style.display = 'block';
        el.className = `${baseClass} ${result.webhookCapable ? 'success' : 'error'}`;
        el.textContent = result.webhookMessage || '';
    }

    async function testSeerr() {
        const resultEl = document.getElementById('seerr-test-result');
        const webhookResultEl = document.getElementById('seerr-webhook-capability-result');
        const strings = getStrings();
        resultEl.style.display = 'block';
        resultEl.className = 'jn-result-msg';
        resultEl.textContent = strings.testing;
        if (webhookResultEl) webhookResultEl.style.display = 'none';

        try {
            // Save first so backend uses current form data
            await saveConfigSilent();
            const result = await apiRequest('/Admin/test/seerr', { method: 'POST' });
            resultEl.className = `jn-result-msg ${result.success ? 'success' : 'error'}`;
            resultEl.textContent = result.message;
            renderWebhookCapabilityResult(webhookResultEl, result);
        } catch (err) {
            resultEl.className = 'jn-result-msg error';
            resultEl.textContent = strings.connectionFailed;
            if (webhookResultEl) webhookResultEl.style.display = 'none';
        }
    }

    async function autoConfigureSeerrWebhook() {
        try {
            await saveConfigSilent();
        } catch (err) {
            const resultEl = document.getElementById('seerr-webhook-autoconfig-result');
            if (resultEl) {
                resultEl.style.display = 'block';
                resultEl.className = 'jn-result-msg error';
                resultEl.textContent = getStrings().connectionFailed;
            }

            return;
        }

        await runSeerrWebhookAutoConfigure();
    }

    async function testDiscord() {
        const resultEl = document.getElementById('discord-test-result');
        const strings = getStrings();
        resultEl.style.display = 'block';
        resultEl.className = 'jn-result-msg';
        resultEl.textContent = strings.testing;

        try {
            await saveConfigSilent();
            const result = await apiRequest('/Admin/test/discord', { method: 'POST' });
            resultEl.className = `jn-result-msg ${result.success ? 'success' : 'error'}`;
            resultEl.textContent = result.message;
        } catch (err) {
            resultEl.className = 'jn-result-msg error';
            resultEl.textContent = strings.connectionFailed;
        }
    }

    async function testTelegram() {
        const resultEl = document.getElementById('telegram-test-result');
        const strings = getStrings();
        resultEl.style.display = 'block';
        resultEl.className = 'jn-result-msg';
        resultEl.textContent = strings.testing;

        try {
            await saveConfigSilent();
            const result = await apiRequest('/Admin/test/telegram', { method: 'POST' });
            resultEl.className = `jn-result-msg ${result.success ? 'success' : 'error'}`;
            resultEl.textContent = result.message;
        } catch (err) {
            resultEl.className = 'jn-result-msg error';
            resultEl.textContent = strings.connectionFailed;
        }
    }

    async function testWhatsApp() {
        const resultEl = document.getElementById('whatsapp-test-result');
        const strings = getStrings();
        resultEl.style.display = 'block';
        resultEl.className = 'jn-result-msg';
        resultEl.textContent = strings.testing;

        try {
            await saveConfigSilent();
            const result = await apiRequest('/Admin/test/whatsapp', { method: 'POST' });
            resultEl.className = `jn-result-msg ${result.success ? 'success' : 'error'}`;
            resultEl.textContent = result.message;
        } catch (err) {
            resultEl.className = 'jn-result-msg error';
            resultEl.textContent = strings.connectionFailed;
        }
    }

    async function sampleSeerrNotification() {
        const resultEl = document.getElementById('seerr-test-result');
        const strings = getStrings();
        resultEl.style.display = 'block';
        resultEl.className = 'jn-result-msg';
        resultEl.textContent = strings.testing;

        try {
            await saveConfigSilent();
            const result = await apiRequest('/Admin/test/seerr/sample', { method: 'POST' });
            resultEl.className = `jn-result-msg ${result.success ? 'success' : 'error'}`;
            resultEl.textContent = result.message;
        } catch (err) {
            resultEl.className = 'jn-result-msg error';
            resultEl.textContent = strings.connectionFailed;
        }
    }

    async function detectTelegramChatId() {
        const resultEl = document.getElementById('telegram-detect-result');
        const strings = getStrings();
        resultEl.style.display = 'block';
        resultEl.className = 'jn-result-msg';
        resultEl.textContent = strings.testing;

        try {
            await saveConfigSilent();
            const result = await apiRequest('/Admin/telegram/detect-chat-id', { method: 'POST' });
            resultEl.className = `jn-result-msg ${result.success ? 'success' : 'error'}`;
            resultEl.textContent = result.message;
            if (result.success && result.chatId) {
                setValue('telegram-chatid', result.chatId);
            }
        } catch (err) {
            resultEl.className = 'jn-result-msg error';
            resultEl.textContent = strings.connectionFailed;
        }
    }

    async function saveConfig() {
        const strings = getStrings();
        try {
            await saveConfigSilent();
            showSaveResult(true, strings.configSaved);
            autoConfigureAllWebhooksAfterSave();
        } catch (err) {
            showSaveResult(false, fmt(strings.configError, err.message));
        }
    }

    async function saveConfigSilent() {
        const config = buildConfigFromForm();
        await apiRequest('/Admin/config', {
            method: 'PUT',
            body: JSON.stringify(config)
        });
    }

    function buildConfigFromForm() {
        const seerrApiKey = document.getElementById('seerr-apikey')?.value?.trim();
        const discordWebhook = document.getElementById('discord-webhook')?.value?.trim();
        const discordBotToken = document.getElementById('discord-bottoken')?.value?.trim();
        const discordClientSecret = document.getElementById('discord-clientsecret')?.value?.trim();
        const telegramToken = document.getElementById('telegram-token')?.value?.trim();
        const whatsAppAccessToken = document.getElementById('whatsapp-access-token')?.value?.trim();

        const sonarrInstances = collectArrInstances('sonarr');
        const radarrInstances = collectArrInstances('radarr');

        return {
            seerrSettings: {
                enabled: isChecked('seerr-enabled'),
                serverUrl: getValue('seerr-url'),
                apiKey: seerrApiKey || undefined,
                seerrType: parseInt(getValue('seerr-type') || '0'),
                ignoreSslErrors: isChecked('seerr-ignore-ssl'),
                // Read-only now (see renderWebhookConnectionStatus) — the admin never sets
                // this directly, so a save must carry forward whatever the backend already
                // determined rather than reading a control that no longer exists.
                webhookEnabled: (_adminConfig && _adminConfig.seerrWebhookEnabled) || false
            },
            sonarrInstances,
            radarrInstances,
            // Same reasoning as seerrWebhookEnabled above — read-only, carried forward
            // from whatever the backend last determined via auto-configure/capability
            // check, never set directly by the admin here.
            arrWebhookEnabled: (_adminConfig && _adminConfig.arrWebhookEnabled) || false,
            notificationSettings: {
                enabled: isChecked('notif-enabled'),
                deduplicationWindowMinutes: parseInt(getValue('notif-dedup-window') || '10'),
                downloadingNotifyThresholdPercent: parseInt(getValue('notif-downloading-threshold') || '50')
            },
            defaultLanguage: getValue('notif-language') || 'auto',
            notificationRetentionDays: parseInt(getValue('notif-retention-days') || '30'),
            disableScriptInjectionMiddleware: !isChecked('script-injection-enabled'),
            whatsAppSettings: {
                enabled: isChecked('whatsapp-enabled'),
                phoneNumber: getValue('whatsapp-phone') || '',
                accessToken: whatsAppAccessToken || undefined,
                phoneNumberId: getValue('whatsapp-phone-number-id') || '',
                verifyToken: getValue('whatsapp-verify-token') || ''
            },
            externalChannelSettings: {
                discordSettings: {
                    enabled: isChecked('discord-enabled'),
                    webhookUrl: discordWebhook || undefined,
                    botToken: discordBotToken || undefined,
                    botUsername: getValue('discord-botname') || 'JellyNotify',
                    embedColor: getValue('discord-color') || '#AA5CC3',
                    clientId: getValue('discord-clientid') || '',
                    clientSecret: discordClientSecret || undefined
                },
                telegramSettings: {
                    enabled: isChecked('telegram-enabled'),
                    botToken: telegramToken || undefined,
                    botUsername: getValue('telegram-botusername') || '',
                    chatId: getValue('telegram-chatid') || '',
                    silentMessages: isChecked('telegram-silent'),
                    disableLinkPreviews: isChecked('telegram-no-preview')
                }
            }
        };
    }

    function collectArrInstances(type) {
        const container = document.getElementById(`${type}-instances`);
        if (!container) return [];

        return Array.from(container.querySelectorAll('.jn-arr-instance')).map(wrapper => {
            const apiKey = wrapper.querySelector('.arr-apikey')?.value?.trim();
            return {
                id: wrapper.dataset.instanceId,
                name: wrapper.querySelector('.arr-name')?.value || '',
                enabled: wrapper.querySelector('.arr-enabled')?.checked ?? true,
                serverUrl: wrapper.querySelector('.arr-url')?.value || '',
                apiKey: apiKey || undefined,
                ignoreSslErrors: wrapper.querySelector('.arr-ignore-ssl')?.checked ?? false,
                pollingIntervalSeconds: parseInt(wrapper.querySelector('.arr-polling')?.value || '300')
            };
        });
    }

    /**
     * Attempts to create/confirm the shared *arr webhook connection for one instance.
     * Reusable from both the per-instance button and the automatic post-save sweep
     * (see saveConfig()) — the backend endpoint is idempotent (skips if a "JellyNotify"
     * connection already exists), so calling it again on every save is safe and cheap.
     */
    async function runArrWebhookAutoConfigure(wrapper, type, instanceId) {
        const resultEl = wrapper.querySelector('.arr-webhook-autoconfig-result');
        const strings = getStrings();
        if (!resultEl) return;

        resultEl.style.display = 'block';
        resultEl.className = 'jn-result-msg';
        resultEl.textContent = strings.testing;

        try {
            const endpoint = type === 'sonarr'
                ? `/Admin/arr-webhook/sonarr/${instanceId}/auto-configure`
                : `/Admin/arr-webhook/radarr/${instanceId}/auto-configure`;

            const result = await apiRequest(endpoint, { method: 'POST' });
            resultEl.className = `jn-result-msg ${result.success ? (result.alreadyExists ? 'warning' : 'success') : 'error'}`;
            resultEl.textContent = result.message;

            // The backend sets ArrWebhookEnabled = true whether this just created the
            // connection or found it already there (see AutoConfigureArrWebhookAsync) — the
            // read-only status shown on every instance card should reflect that either way.
            if (result.success) {
                if (_adminConfig) {
                    _adminConfig.arrWebhookEnabled = true;
                }

                document.querySelectorAll('.arr-webhook-connection-status').forEach(el => {
                    renderWebhookConnectionStatus(el, true);
                });
            }
        } catch (err) {
            resultEl.className = 'jn-result-msg error';
            resultEl.textContent = strings.saveFirst;
        }
    }

    /**
     * Attempts to create/confirm Seerr's webhook. Reusable from both the manual button
     * and the automatic post-save sweep (see saveConfig()) — the backend endpoint is
     * idempotent (skips if already pointing at us, refuses to clobber a different one),
     * so calling it again on every save is safe and cheap.
     */
    async function runSeerrWebhookAutoConfigure() {
        const resultEl = document.getElementById('seerr-webhook-autoconfig-result');
        const strings = getStrings();
        if (!resultEl) return;

        resultEl.style.display = 'block';
        resultEl.className = 'jn-result-msg';
        resultEl.textContent = strings.testing;

        try {
            const result = await apiRequest('/Admin/seerr-webhook/auto-configure', { method: 'POST' });
            resultEl.className = `jn-result-msg ${result.success ? (result.alreadyExists ? 'warning' : 'success') : 'error'}`;
            resultEl.textContent = result.message;

            // The backend sets WebhookEnabled = true whether this just configured Seerr or
            // found it already pointing at us (see AutoConfigureSeerrWebhook) — the
            // read-only status shown here should reflect that either way.
            if (result.success) {
                if (_adminConfig) {
                    _adminConfig.seerrWebhookEnabled = true;
                }

                renderWebhookConnectionStatus(document.getElementById('seerr-webhook-connection-status'), true);
            }
        } catch (err) {
            resultEl.className = 'jn-result-msg error';
            resultEl.textContent = strings.connectionFailed;
        }
    }

    /**
     * After every config save, automatically attempts to create/confirm the webhook for
     * every enabled service that has a server URL filled in — this is the primary path
     * now that Seerr/Sonarr/Radarr can be told to call JellyNotify directly; polling is
     * only a backstop for whatever this can't reach (older versions, network issues).
     * Runs in the background after the "Configuration saved" confirmation — a slow or
     * unreachable external service shouldn't delay the save itself.
     */
    async function autoConfigureAllWebhooksAfterSave() {
        if (isChecked('seerr-enabled') && getValue('seerr-url').trim() !== '') {
            await runSeerrWebhookAutoConfigure();
        }

        for (const type of ['sonarr', 'radarr']) {
            const container = document.getElementById(`${type}-instances`);
            if (!container) continue;

            for (const wrapper of container.querySelectorAll('.jn-arr-instance')) {
                const enabled = wrapper.querySelector('.arr-enabled')?.checked ?? false;
                const url = wrapper.querySelector('.arr-url')?.value?.trim() ?? '';
                const instanceId = wrapper.dataset.instanceId;
                if (enabled && url !== '' && instanceId) {
                    await runArrWebhookAutoConfigure(wrapper, type, instanceId);
                }
            }
        }
    }

    async function sendTestNotification() {
        const strings = getStrings();
        const el = document.getElementById('test-notification-result');
        try {
            await apiRequest('/notifications/test', { method: 'POST' });
            if (el) {
                el.className = 'jn-result-msg success';
                el.textContent = strings.testSent;
                el.style.display = 'block';
                setTimeout(() => { el.style.display = 'none'; }, 4000);
            }
        } catch (err) {
            if (el) {
                el.className = 'jn-result-msg error';
                el.textContent = strings.testError;
                el.style.display = 'block';
                setTimeout(() => { el.style.display = 'none'; }, 4000);
            }
        }
    }

    function showSaveResult(success, msg) {
        const el = document.getElementById('save-result');
        if (!el) return;
        el.className = `jn-result-msg ${success ? 'success' : 'error'}`;
        el.textContent = msg;
        el.style.display = 'block';
        setTimeout(() => { el.style.display = 'none'; }, 4000);
    }

    // ─── DOM helpers ─────────────────────────────────────────────────
    function isChecked(id) { return document.getElementById(id)?.checked ?? false; }
    function getValue(id) { return document.getElementById(id)?.value ?? ''; }
    function setValue(id, val) { const el = document.getElementById(id); if (el) el.value = val; }
    function setChecked(id, val) { const el = document.getElementById(id); if (el) el.checked = !!val; }

    /**
     * Sets a <select>'s value, adding a one-off extra <option> for it first if the
     * value isn't one of the fixed choices (e.g. a polling interval saved before
     * this field became a dropdown, or set directly through the API) — so an
     * unusual existing value is shown accurately instead of silently snapping to
     * whatever option happens to be first.
     */
    function setSelectValuePreservingUnknown(selectEl, value) {
        if (!selectEl) return;
        const stringValue = String(value);
        if (!selectEl.querySelector(`option[value="${stringValue}"]`)) {
            const option = document.createElement('option');
            option.value = stringValue;
            option.textContent = `${stringValue}s`;
            selectEl.insertBefore(option, selectEl.firstChild);
        }

        selectEl.value = stringValue;
    }
    function escapeHtml(str) {
        const d = document.createElement('div');
        d.appendChild(document.createTextNode(str || ''));
        return d.innerHTML;
    }

    // crypto.randomUUID() only exists in secure contexts (HTTPS or localhost).
    // Most self-hosted Jellyfin instances are reached over plain HTTP on a LAN
    // IP, where it's simply undefined — this is only used as a client-side
    // instance-list key, not for anything security-sensitive.
    function generateInstanceId() {
        if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID();
        }
        return `jn-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
    }

    // ─── Initialization ───────────────────────────────────────────────

    /**
     * Loads the bell/panel stylesheet globally. The config page loads
     * jellynotify.css itself via an inline bootstrap <link> in
     * configPage.html, but nothing else ever links it on regular Jellyfin
     * Web pages — without this, the bell button renders with no styling
     * (a bare, unstyled browser button instead of a circular icon).
     */
    function injectGlobalStylesheet() {
        if (document.getElementById('jn-global-css')) return;
        const link = document.createElement('link');
        link.id = 'jn-global-css';
        link.rel = 'stylesheet';
        link.href = `${API_BASE}/web/jellynotify.css`;
        document.head.appendChild(link);
    }

    function init() {
        if (document.getElementById('jellynotify-config-page')) {
            initConfigPage();
            return;
        }

        injectGlobalStylesheet();

        let attempts = 0;
        const maxAttempts = 20;
        const tryInject = () => {
            attempts++;
            injectBellUI();
            if (!document.getElementById('jn-bell-btn') && attempts < maxAttempts) {
                setTimeout(tryInject, 500);
            }
        };

        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', tryInject);
        } else {
            tryInject();
        }

        // Re-inject when Jellyfin navigates (SPA)
        const observer = new MutationObserver(() => {
            if (!document.getElementById('jn-bell-btn')) {
                injectBellUI();
            }
        });
        observer.observe(document.body, { childList: true, subtree: true });

        // Jellyfin Web is a hash-routed SPA; the header itself is rarely torn down,
        // but some route transitions replace it. Re-check on navigation as a cheap
        // belt-and-braces alongside the MutationObserver above.
        const recheckBell = () => {
            if (!document.getElementById('jn-bell-btn')) {
                injectBellUI();
            }
        };
        window.addEventListener('hashchange', recheckBell);
        window.addEventListener('popstate', recheckBell);
    }

    init();
})();
