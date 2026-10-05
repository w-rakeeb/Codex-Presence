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
- Activity/project first, followed by model/effort and grouped `Token: 29.5M - Context: 85% used` text, with custom labels and visibility controls.
- An optional activity/project heading for three independent text groups; the normal Codex App or ChatGPT App heading remains the default.
- Continuous elapsed time since the companion opens, or elapsed time based on work and project activity.
- Optional idle/waiting timer pause, with idle intervals excluded from continuous elapsed time.
- Optional desktop priority for known games, running Steam games and selected applications; Codex yields without stopping local monitoring.
- Settings that retain unsaved changes when you switch tabs. Saving one tab preserves changes already saved on another.
- Background startup with Codex, optional tray visibility, and a hidden window that can always be reopened.
- Five interface styles: Minimal, Soft, Rounded, Paper and Studio. Every style supports independent dark and light modes.
- The original terminal dashboard, connection diagnostics, desktop shortcuts and local configuration tools.
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

A dot beside a tab marks unsaved changes. **Reload** discards changes on that tab. Running in the background retains drafts in memory; save them before exiting. Exit asks you to resolve pending changes instead of silently losing them.

## Privacy and local data

Only enabled presence fields are sent to Discord. Session files, settings and the local desktop data folder stay on your computer. The engine may query the configured asset catalog for Discord image metadata.

Desktop preferences live in `Data/app-settings.json` beside the executable. Engine settings use `discord-presence-config.json` in your Codex home directory. Validated saves retain backups. Monetary amounts are estimates based on observed usage; missing pricing or incomplete coverage is reported rather than invented.

No Discord bot token is needed for local Rich Presence. The upstream application identity and assets are retained. See [NOTICE.md](NOTICE.md) for attribution and third-party notices.

## Build and verify

Use PowerShell 7, the .NET 8 SDK and the stable Rust toolchain with the Windows MSVC build tools.

```powershell
./scripts/verify.ps1
./scripts/build-desktop.ps1
./scripts/verify-desktop.ps1
./scripts/verify-desktop.ps1 -CrossVolume
./scripts/package-desktop.ps1 -AppRoot "$PWD/.build/desktop"
```

Build outputs stay in `.build/desktop`; portable packages go into `releases/desktop-v1.5.0`. The ZIP excludes local settings, session data, backups and verification fixtures. It includes the app, runtime, guide, licenses, attribution and SHA-256 checksums. The .NET Desktop Runtime is installed separately to keep the download small.

Version 1.5.0 passed 280 Rust tests and 90 desktop checks on both same-drive and cross-drive configurations. Checks cover both heading modes, idle timer pause/resume, game/custom process matching and priority transitions through a running engine. Native Windows checks exercised both heading toggles and previews with Ctrl+S; earlier 1.4.0 checks covered dropdown selection, tab changes and retained drafts. Priority policy tests use synthetic process names and do not launch games. These results describe the tested Windows 11 environment; see the [release checklist](docs/desktop-release.md) for coverage and limitations.

The Rust engine and original terminal documentation remain available in [docs/index.md](docs/index.md), [CHANGELOG.md](CHANGELOG.md) and the source tree.

## Credits

Maintained by [w-rakeeb](https://github.com/w-rakeeb) as a fork of [xt0n1-t3ch/Codex-Discord-Rich-Presence](https://github.com/xt0n1-t3ch/Codex-Discord-Rich-Presence). Original MIT notices are preserved in [LICENSE](LICENSE). Desktop additions and release attribution are documented in [NOTICE.md](NOTICE.md).

This is an independent project. It is not affiliated with OpenAI, Discord or Microsoft.
