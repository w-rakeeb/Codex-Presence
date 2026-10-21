# Desktop release notes

## 3.1.0

- Correct resize corners by retaining an 8px native resize gutter around the browser. Collapse the unused native layout and defer snapshot refresh, motion and geometry writes during a resize drag. Keep maximized content inside the current monitor working area, above the taskbar.
- Let dashboard cards, statistics, settings and preview panels use the available width, including beyond the previous content cap.
- Move the overflow menu to the browser's top layer, keep it inside the viewport and support Escape/outside-click dismissal. Remove sidebar taglines.
- Add a separate Manual presence section with editable activity text and connection, update, session, local clock and custom start/end timing. Save profiles separately; enable manual mode explicitly. Returning to automatic preserves the real-work startup gate. Pause, privacy mode and game priority still apply.
- Include Rust engine 1.12.0. Normal presence composition and existing configuration schemas stay unchanged.

## 3.0.0

- Replace the top-tab interface with a local daisyUI 5 / Tailwind CSS 4 workspace hosted in WebView2. Add a full sidebar that becomes an icon rail on smaller windows.
- Rebuild Overview with paired status/preview cards, current session information, four large statistics, a token breakdown and retained session/quota details.
- Reorganize Privacy and Layout into form panels with a saved-output preview rail. On narrow windows, show the preview before the controls.
- Add section search, clearer toggle rows, visible input feedback and a persistent Save/Reload strip. Keep retained drafts and the existing manual-save contract.
- Keep all five styles, four palettes, System/Dark/Light, tray/background behavior and original engine actions. The native dashboard is the fallback if WebView2 cannot initialize.
- Load no browser during hidden startup. Bundle the complete interface and licenses locally; prevent external navigation, downloads and permission requests from reaching the desktop bridge.
- Preserve the Rust binary, outgoing Discord text, configuration schema, original MIT credit and the independent old-design installation.

## 2.0.2

- Apply the daisyUI skill's card, fieldset, bordered-tab, button, toggle and stats patterns through native WPF components. No web runtime or UI dependency is added.
- Give engine status a clearer headline and a separate action row that fits narrow windows.
- Join usage statistics into one panel with consistent alignment, inset padding and dividers.
- Refine secondary actions, preview badges, card spacing, form labels and the sticky Save/Reload area across all five tabs.
- Preserve every existing setting, manual save behavior, startup gate, original attribution and exact outgoing Discord payload.

## 2.0.1

- Use the Windows highlight text brush for selected tabs and dropdown items in custom high-contrast themes. The 2.0 redesign and engine/settings contracts remain unchanged.

## 2.0.0

- Redesign the WPF shell with graphite layers, indigo actions, segmented navigation, a sticky footer and a pinned Layout preview.
- Replace equal-weight engine buttons with a status card, one primary action and a background/exit overflow menu.
- Add usage tiles, exact-value copy, context/cache meters, a cached-input breakdown and clearer settings/tool rows.
- Consolidate palettes and control templates; add stronger input outlines, System appearance, high-contrast mapping and reduced motion.
- Add app artwork at 16/20/24/32/48/256 sizes and monochrome live/paused/error tray variants for dark/light taskbars.
- Remember geometry and last tab separately. Preserve settings formats, manual Save/Reload, drafts, engine behavior and exact Discord text.

See `desktop-design.md` for component ownership, contrast adjustments, verification scope and preserved decisions.

## 1.9.1

- Keep automatic startup and background monitoring, but hide Discord presence until a new Codex prompt or task starts after the companion opens. Restored chats, metadata updates and token refreshes cannot unlock it.
- Preserve the startup gate across engine restarts, and retain normal idle timeout, manual Pause and game priority after work begins.

## 1.9.0

- Load Settings sections only when opened, retaining their controls, unsaved values and validation when moving between sections or tabs.
- Show recent sessions in batches of twelve. All sessions remain available through Show more; details build on expansion and suspend updates while collapsed. Full-data expansion is tracked independently per session.
- Move configuration validation commands, file hashes, backups, atomic replacement and desktop preference persistence off the UI thread. Serialize configuration reads and writes and capture their target paths.
- Distinguish unknown speed from Standard and Fast. Missing or malformed token, cost and context values remain unavailable.
- Reflect external publication changes in Pause / Resume without confusing the master switch with monitoring-only, idle hiding or game priority.
- Keep recovery guidance in the main interface and technical errors in Tools. Show scanning and automatic reconnect feedback; handle background watcher failures.
- Restore the original field's focus when canceling the exit decision. Improve input boundary contrast, pressed and hover states, accessible names, shared sizing and tabular numeric text.
- Make retained preview icons and quota bars respond to appearance changes. Keep all five styles, four palettes, both color modes, application identities, privacy choices and schema 18.

The audit includes reproducible synthetic performance measurements, automated runtime checks and native window interaction. See [audit coverage](desktop-audit.md) for measured results and platform limits.

## 1.8.0

- Added Settings → Activity → Discord application with Default and Custom sources. Custom accepts a public Application ID from Discord's General Information page; no bot token, OAuth scope, intents or Administrator permission is needed.
- Kept both built-in application IDs available. Returning to Default restores the original connection and artwork while retaining the saved custom ID and image keys.
- Added independent custom large/small image keys and a developer portal shortcut. Empty image keys omit artwork; custom images belong to the selected application's Rich Presence Art Assets.
- Validated IDs in desktop controls and engine configuration. Malformed IDs receive focused inline feedback and cannot replace the configuration. An invalid external edit preserves the running engine's last valid settings.
- Applied custom IDs to CLI, VS Code and desktop activity. ID changes reconnect Discord and clear the previous asset catalog without restarting monitoring or resetting continuous time.
- Renamed Layout's existing Codex/ChatGPT selector to Application heading to distinguish display text from the connection's Application ID. Both text layouts, visibility switches, idle hiding, game priority and background behavior are retained.
- Migrated to schema 18 with Default selected and existing preferences preserved. Tools reads the desktop version from its assembly.

Verified on Windows 11 x64: 297 Rust tests, formatting and strict lint checks; 335 desktop checks with configuration on each drive. The new application card and expanded asset/default-ID panels fit at all nine tested widths from 320 to 1920 logical pixels. Native mouse/keyboard checks cover Custom selection, an empty-ID error, Ctrl+S saving, Default restoration and retained custom values. Custom IDs use monitoring-only synthetic fixtures; a live connection and artwork for a newly created user application require its real ID. Built-in live connection and public package verification are separate release gates. Existing Windows 10 and DPI/accessibility limits remain in [audit coverage](desktop-audit.md).

## 1.7.0

- Kept navigation available during requests while disabling duplicate asynchronous actions. Save, Reload and diagnostics retain the correct originating view when tabs change.
- Fixed Reload retaining unsaved controls when the saved configuration was unchanged. Edits made after a reload starts remain intact.
- Added inline validation for monitoring values, home folders, pricing JSON and complete configuration JSON, with focus and accessible error feedback.
- Added safe recovery from damaged desktop preferences, atomic preference saves and exact backups. Failed preference saves preserve the previous monitoring settings and engine.
- Added a visible Review / Exit without saving / Cancel decision for unsaved drafts, including hidden windows. Fixed the actual X-button close-event timing.
- Adapted navigation, settings sections and field-order rows down to 320 logical pixels. Wide content is capped for readability; short windows keep scrolling and save controls.
- Repaired scrollbar rail commands, added named window controls and maximization, keyboard focus indicators and polite status announcements. Long status messages remain bounded with full tooltips.
- Fixed the documentation button in portable packages, command timeout recovery and malformed quota reset handling.
- Fixed cached session parsing after equal-length and growing rewrites, while preserving append and split-line handling. Bridge control messages now wake a waiting poll directly, reducing timer wakeups.

Verified on Windows 11 x64: 286 Rust tests, formatting and strict lint checks; 308 desktop checks in each drive configuration. All pages and Settings sections pass horizontal bounds checks at 320, 375, 390, 430, 768, 1024, 1280, 1440 and 1920 logical pixels. Forty appearance combinations pass text/button contrast and compact layout checks. See [audit coverage](desktop-audit.md) for native interaction evidence and remaining limits. Schema 17, application identities and existing defaults are preserved.

## 1.6.0

- Changed the Discord context suffix to **Used**.
- Added configurable automatic idle hiding, from 1 to 1440 minutes. It clears the whole presence after inactivity and restores it when work resumes, preserving manual Pause and game priority. New activity resets the timeout; a stale working label can expire, while pending commands remain visible.
- Reorganized Settings into Appearance, Activity, Background and Advanced. Section changes retain edits, and invalid values reveal the relevant section.
- Added Slate, Forest and Dusk color themes with dark/light variants, independent of the five interface styles. Appearance previews immediately; Save keeps it and Reload restores the saved appearance.
- Added consistent navigation icons, moved the text layout controls to the top of Layout, consolidated application/label controls and improved field-order accessibility labels.
- Disabled dependent idle, priority and manual-plan controls when their parent option is inactive. Added an idle countdown to the dashboard.
- Preserved the active tab and newer edits when an asynchronous save completes. Unsaved appearance previews also survive that save.
- Configuration schema 17 defaults idle hiding to off and preserves existing preferences.

Verified on Windows 11 x64: 284 Rust tests and 205 desktop checks in each drive configuration. Forty style/theme/mode combinations pass compact layout and text/button contrast checks. Fixtures exercise parsed idle expiry and restoration without publishing activity. Windows 10 execution and live game priority transitions remain unverified.

## 1.5.0

- Restored activity/project ahead of model text in the grouped layout.
- Added an activity/project heading toggle. Off retains Codex App or ChatGPT App; on assigns activity/project, model/effort and token/context to the heading and two separate text fields.
- Added coverage for both layouts, standard/Fast display, private fields, missing data and long Unicode chat titles. Desktop checks save and restore both heading modes.
- Configuration schema 16 defaults the new toggle to off and retains existing visibility and label settings.
- Added idle/waiting timer pause. The clock is hidden during inactivity; continuous time excludes that interval on resume.
- Added desktop activity priority for known games, running Steam games and selected executable names. Codex publication yields temporarily while local monitoring continues; releasing priority preserves manual pause and the saved configuration.
- Reserved usage text before optional quota fields so context values remain complete in both grouped layouts.

Verified on Windows 11 x64: 280 Rust tests, 90 desktop checks in each drive configuration, and native heading-toggle/save/preview checks. Priority policy tests use synthetic game/process inputs and the real isolated bridge; no games were launched. Windows 10 execution remains unverified.

## 1.4.0

- Corrected the dropdown popup template so mouse and keyboard selections work reliably.
- Removed the unsaved-settings navigation lock. Tabs retain controls, selections and scroll position; saves merge only the edited fields into the latest configuration.
- Added continuous and work/project elapsed-time modes. The continuous anchor survives presence-engine restarts during the same desktop-app run.
- Added a separate token/context line beneath the model information, while retaining custom labels and the optional inline layout.
- Added Paper and Studio interface styles. All five styles support dark and light modes independently.
- Removed navigation fades and repeated meter animations; unchanged snapshots avoid idle rendering work.
- Added Ctrl+S, Ctrl+Tab, Ctrl+Shift+Tab and Ctrl+1 through Ctrl+5.
- Added explicit DPI and Windows compatibility settings and Segoe UI font fallback.
- Added portable ZIP packaging, SHA-256 checksums, attribution and a desktop release checklist. Engine configuration schema is now 15.

Verified on Windows 11 x64: 275 Rust tests, 76 desktop checks in each of two drive configurations, and native dropdown/draft/shortcut interaction checks. Windows 10 execution remains unverified.

## 1.3.0

- Added independent interface styles and dark/light mode, background host startup, desktop shortcuts and a dedicated status panel.
- Repaired terminal launch failure handling and automatic desktop-engine restoration after the terminal exits.

## Earlier desktop versions

Added the native Windows dashboard, tray and background controls, usage preview, custom labels, privacy settings and the terminal bridge. The original Rust engine history is recorded in the root CHANGELOG.md.
