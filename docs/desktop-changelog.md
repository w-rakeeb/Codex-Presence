# Desktop release notes

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
