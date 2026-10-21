# Desktop 3.0: actual daisyUI workspace

[Overview](screenshots/desktop-3/Overview.png) · [Privacy](screenshots/desktop-3/Privacy.png) · [Layout](screenshots/desktop-3/Layout.png) · [Settings](screenshots/desktop-3/Settings.png) · [Tools](screenshots/desktop-3/Tools.png) · [Soft / Light](screenshots/desktop-3/Settings-Soft-Light.png)

## What changed

The 2.0.2 update adapted daisyUI patterns to WPF and retained most of the existing layout. Version 3 uses the actual daisyUI 5 library, compiled with Tailwind CSS 4, in a local WebView2 view. The WPF host continues to own the Windows frame, tray, startup, engine lifecycle, settings/drafts and validation. The Rust engine and Discord formatting are unchanged.

The new layout replaces five top tabs with sidebar navigation; small windows use an icon rail. Overview now has paired engine/Discord cards, current session identity, four large metrics and a token breakdown. Privacy and Layout have a preview beside their controls, or above them when narrow. Settings has four sections and local search. Save/Reload and status have persistent areas.

## Components and resources

The implementation uses daisyUI `menu`, `card`, `badge`, `status`, `stats`, `fieldset`, `input`, `textarea`, `select`, `toggle`, `collapse`, `progress`, `dropdown`, `modal`, `toast` and `btn`. Menu was selected over a drawer because navigation must always be available; the compact rail does not require an overlay. Fieldsets group settings; cards hold separate dashboard areas; stats provide numeric hierarchy. The native control schema supplies every existing form and action rather than duplicating validation rules in JavaScript.

`desktop/Web/styles.css` imports Tailwind and daisyUI and defines layout and component refinements. `Theme.xaml` remains the source of application palette values; the host translates them into daisyUI semantic variables. All eight palettes, five styles and System following remain available. Typography is Segoe UI Variable/Segoe UI, numeric figures are tabular, and code/JSON uses Cascadia Mono. Soft style changes headings to a serif face; Rounded, Paper and Studio retain distinct shape/typographic treatments.

Build with `npm ci` and `npm run build` in `desktop/Web`, or use `scripts/build-desktop.ps1`. Node.js is a build dependency only. The release contains compiled CSS, JavaScript, HTML, the local logo, licenses and the WebView2 x64 loader. It does not load a CDN or run a local HTTP server.

## Preserved behavior

- Every input delegates to its existing WPF control and handlers. Save uses the existing validation, conflict detection, merge, backups and atomic writer. Drafts remain drafts until saved.
- Discord preview name/details/state are rendered verbatim with escaped text. The local companion icon is used for the preview; arbitrary Discord artwork is not downloaded. Discord asset keys and identities remain untouched.
- Existing monitoring, timers, priority rules, idle hiding, custom IDs, subscription visibility, pricing, terminal actions, shortcut creation and background startup remain available.
- The browser is initialized only when the dashboard is shown, and hidden dashboards receive no UI polling updates. Input nodes and caret positions survive live updates; dashboard areas update only when their data changes.
- Window geometry and the one-time new-layout width migration live in the existing presentation-only sidecar. Existing preferences and engine storage formats are unchanged.
- The independently named Old Design app is preserved outside the publishing checkout.

## Bridge boundary and recovery

Only bundled `https://presence.local/index.html` can send desktop messages. The host accepts known navigation/command names and IDs belonging to currently rendered controls; it exposes no arbitrary shell, filesystem or evaluation API. External navigation, new windows, downloads and permissions are blocked. CSP permits only local scripts/styles/images and disables network connections. Text is HTML escaped. Native fallback controls regain mouse and keyboard access if the browser fails.

## Verification scope

The existing 580 native checks cover settings, lifecycle, identity, payload, atomic saves, background gate and compatibility behavior. Browser integration checks exercise every form control on all pages and lazy sections, editing/saving, retained drafts, exact preview strings, caret preservation, theme/style preview, engine states, responsive widths, section search, browser zoom, high contrast/reduced motion media, and blocked external navigation. `scripts/verify-web.ps1` runs in an isolated monitoring-only Codex home and cannot publish Discord activity.

Browser zoom and emulated high contrast are useful UI tests, not certification of physical Windows DPI changes, mixed-monitor transitions, Windows 10 or full screen-reader use. Screenshots use synthetic local sessions. The executable remains unsigned and requires .NET 8 Desktop Runtime x64; the new interface additionally requires the WebView2 Runtime. The native fallback remains available.
