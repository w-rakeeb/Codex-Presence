# Codex Presence

A small Windows desktop companion that publishes your Codex activity to Discord. Choose what people can see, preview your presence, and keep the app running quietly in the background.

[Download for Windows](https://github.com/w-rakeeb/Codex-Presence/releases/latest) · [Complete guide](desktop/Guide.md) · [Release notes](docs/desktop-changelog.md) · [Original engine](https://github.com/xt0n1-t3ch/Codex-Discord-Rich-Presence)

## Get started

1. Download the Windows x64 ZIP from Releases and extract the entire folder.
2. Install the [Microsoft .NET 8 Desktop Runtime for x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) if Windows asks for it. The SDK is only needed to build from source.
3. Open **Codex Presence.exe**, start Discord, and select **Start presence**.
4. Choose your visible fields in **Privacy** and **Layout**. Use **Settings** for appearance, timers and background startup.

You can create a desktop shortcut from **Tools**. Reopen the executable to show a hidden window. Extract updates into a new folder after exiting the previous version; keep your `Data` folder if you want to retain desktop preferences.

The release is tested on Windows 11 x64. It uses Windows 10 compatible controls, font fallbacks and DPI settings, but this version has not been run on a Windows 10 machine. The executable is unsigned.

## Features

- Chat titles, activity, model, effort, tokens, context and available usage information from local Codex sessions.
- Activity/project first, followed by model/effort and grouped `Token: 29.5M - Context: 85% Used` text, with custom labels and visibility controls.
- An optional activity/project heading for three independent text groups; the normal Codex App or ChatGPT App heading remains the default.
- Continuous elapsed time since the companion opens, or elapsed time based on work and project activity.
- Optional idle/waiting timer pause, with idle intervals excluded from continuous elapsed time.
- Configurable idle hiding: choose 1–1440 minutes before the entire Discord presence disappears, with automatic restoration when work resumes.
- Optional desktop priority for known games, running Steam games and selected applications; Codex yields without stopping local monitoring.
- Settings that retain unsaved changes when you switch tabs. Saving one tab preserves changes already saved on another.
- Background startup with Codex, optional tray visibility, and a hidden window that can always be reopened.
- Five interface styles: Minimal, Soft, Rounded, Paper and Studio. Every style supports independent dark and light modes.
- Three additional color themes: Slate, Forest and Dusk, each with dark and light variants and immediate previews.
- Settings grouped into Appearance, Activity, Background and Advanced, with consistent navigation icons and dependent controls.
- Settings and session details load on demand; recent sessions appear in batches of twelve with Show more for the full list.
- The original terminal dashboard, connection diagnostics, desktop shortcuts and local configuration tools.
- A Default / Custom Discord application selector, with a saved public Application ID and application-specific artwork keys. No bot token is required.
- The upstream engine's privacy controls, desktop identity handling and cost coverage reporting.

Discord provides a heading and two text fields. With the normal app heading, activity/project occupies the first field and model followed by usage occupies the second. Turn on **Activity/project heading (three rows)** in Layout to use the heading for activity/project, the first field for model/effort and the second for usage. Keep **Group model, tokens and context** enabled for either layout. Discord controls wrapping; three custom rows underneath a retained app heading cannot be forced. This app reads Codex session files. It does not inspect browser chats or parse ordinary ChatGPT conversations.

## Controls

| Action | Control |
| --- | --- |
| Save the current settings tab | Ctrl+S |
| Next or previous tab | Ctrl+Tab / Ctrl+Shift+Tab |
| Open a tab directly | Ctrl+1 through Ctrl+5 |
| Choose a dropdown option | Mouse, or arrow keys followed by Enter |
| Show the hidden dashboard | Open the executable again |
| Stop the app and its owned engine | Exit in the dashboard or tray menu |

A dot beside a tab marks unsaved changes. **Reload** asks before discarding changes on that tab. Running in the background retains drafts in memory. Exit offers Review changes, Exit without saving, or Cancel, including when the dashboard was hidden.

## Privacy and local data

Only enabled presence fields are sent to Discord. Session files, settings and the local desktop data folder stay on your computer. The engine may query the configured asset catalog for Discord image metadata.

Desktop preferences live in `Data/app-settings.json` beside the executable. Engine settings use `discord-presence-config.json` in your Codex home directory. Validated saves retain backups. Monetary amounts are estimates based on observed usage; missing pricing or incomplete coverage is reported rather than invented.

No Discord bot token is needed for local Rich Presence. Default mode retains the upstream application IDs and assets. To use your own, create a Discord application, copy its Application ID from General Information, and choose **Settings → Activity → Discord application → Custom**. Upload artwork to that application's Rich Presence Art Assets and enter the matching keys. No Administrator permission, OAuth scopes, server invite or privileged intents are required. [Setup guide](desktop/Guide.md#your-own-discord-application). See [NOTICE.md](NOTICE.md) for attribution and third-party notices.

## Build and verify

Use PowerShell 7, the .NET 8 SDK and the stable Rust toolchain with the Windows MSVC build tools.

```powershell
./scripts/verify.ps1
./scripts/build-desktop.ps1
./scripts/verify-desktop.ps1
./scripts/verify-desktop.ps1 -CrossVolume
./scripts/package-desktop.ps1 -AppRoot "$PWD/.build/desktop"
```

Build outputs stay in `.build/desktop`; portable packages go into `releases/desktop-v1.9.0`. The ZIP excludes local settings, session data, backups and verification fixtures. It includes the app, runtime, guide, licenses, attribution and SHA-256 checksums. The .NET Desktop Runtime is installed separately to keep the download small.

Version 1.9.0 passed 297 Rust tests and 366 desktop checks with configuration on each drive. Checks cover lazy Settings, batched sessions, retained drafts, independent session expansion, unavailable telemetry, external publication changes, recovery messages, focus restoration and input contrast, plus custom Application IDs, idle hiding, priority, terminal recovery and atomic backups. Every page and Settings section is checked at nine widths from 320 to 1920 logical pixels. All 40 style/theme/mode combinations are checked for compact layout and text/button contrast. Run desktop verification with `-Profile` for synthetic construction/update measurements. Native mouse and keyboard checks cover navigation, appearance, drafts, validation and saving. Tests use synthetic sessions, IDs and process names; no games are launched. A newly registered application's live connection requires its real ID. See the [audit coverage](docs/desktop-audit.md) and [release checklist](docs/desktop-release.md) for measured results and practical limits.

The Rust engine and original terminal documentation remain available in [docs/index.md](docs/index.md), [CHANGELOG.md](CHANGELOG.md) and the source tree.

## Credits

Maintained by [w-rakeeb](https://github.com/w-rakeeb) as a fork of [xt0n1-t3ch/Codex-Discord-Rich-Presence](https://github.com/xt0n1-t3ch/Codex-Discord-Rich-Presence). Original MIT notices are preserved in [LICENSE](LICENSE). Desktop additions and release attribution are documented in [NOTICE.md](NOTICE.md).

This is an independent project. It is not affiliated with OpenAI, Discord or Microsoft.
