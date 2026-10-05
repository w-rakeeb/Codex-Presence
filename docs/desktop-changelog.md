# Desktop release notes

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
