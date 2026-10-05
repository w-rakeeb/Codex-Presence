# Codex Presence — A to Z

## Open and start

Extract the release zip, keep its files together and open **Codex Presence.exe**. Keep Discord desktop open, then click **Start presence**. Tools can create a desktop shortcut. Codex activity is detected from local session files. The default home is `%USERPROFILE%\.codex`, or the inherited `CODEX_HOME` environment variable. No Discord token or bot setup is needed.

The first launch stays stopped until you click Start. The app uses the original project's Discord application identities and images. If another presence engine is running, the app reports that instead of stopping it.

The compact Windows x64 build requires Microsoft's [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0). The SDK is unnecessary for running the app. The GUI executable and `Runtime` folder belong together. The source repository is distributed separately through GitHub. Windows 11 is tested locally; the app includes Windows 10-compatible controls, font fallbacks and DPI handling, but a real Windows 10 test remains outstanding.

## Overview

- **Start presence / Stop engine:** start the owned engine, or gracefully stop it and clear its Discord activity.
- **Pause / Resume:** control publication while keeping local monitoring active. The setting persists.
- **Run in background:** start the engine if needed and hide the dashboard. Discord presence and local monitoring continue. Reopen the executable or double-click the tray icon to return to the same running app.
- **Exit:** clear the activity, stop the owned engine and exit the app.
- **Discord preview:** the actual text produced by the original engine's compositor. Discord controls its final profile layout and may take a few seconds to refresh.
- **Session usage:** total tokens, known cost, input/cached/output tokens, cache ratio, plan and speed.
- **Active sessions:** project, model, effort, activity, branch, context and cost. Expand the complete session data for source, timing, policies and provenance.
- **Limits and credits:** observed usage, reset times and credit balance; expand for every reported quota field.
- **Metrics:** full cost breakdown, model totals and pricing coverage.

Version 1.4 keeps tabs usable while settings are unsaved. Each edited tab retains its controls, values and scroll position; a dot marks unsaved changes. Saving a tab merges only its changes, preserving settings already saved elsewhere. Dropdowns use WPF's required popup and keyboard hooks. Page fades and repeatedly allocated progress animations are removed. Snapshot parsing and Discord image lookup run away from the UI thread; a hidden dashboard suspends rendering. Discord controls the font and appearance inside its own activity card.

Costs are estimates from available telemetry and configured rates. Missing costs remain unavailable; partial known amounts stay marked partial. A plan label does not grant or change account access. Statistics are scoped to sessions observed by this engine, not a complete account billing history.

## Privacy

**Publish Discord presence** is the master publication switch. **Privacy mode** hides session details in Discord. Each visibility checkbox controls a field: chat title/project, branch, model, tokens, cost, limits, credits, context, activity, activity target, systems and custom text. **Show subscription** controls the plan name independently of its price; it is also available in Settings. Click **Save changes** to apply.

Enter your **Custom text**, enable its visibility and save. Custom text is initially disabled. It obeys the master privacy switch and can be placed/reordered through Layout.

Local dashboard information remains visible. The engine reads local session/metadata files; it does not upload transcripts. Discord receives only configured presence fields. Project names, branches and activity targets can reveal work, so review these choices before starting publication. Defaults preserve the original project's defaults.

## Layout

Choose **Codex App** or **ChatGPT App** desktop identity. Authoritative session metadata still selects CLI, VS Code or desktop activity. Choose compact or descriptive field labels. **Token label** and **Context label** accept up to 16 characters; their defaults are **TK** and **CTX**. Custom text accepts up to 128 characters.

**Use the chat title for the project field** reads the latest name/title from the local Codex thread database, including renames, with the session index as a fallback. Missing titles fall back to the folder name. Disable this option to display folder names. The database is opened read-only, and title changes are checked with each session poll.

Enable each of the eleven fields and use the arrows to reorder them. When grouped layout is off, choose **Details** (first Discord line) or **State** (second line). Grouped layout assigns rows automatically, so these selectors are disabled. Field visibility stays synchronized with Privacy. The compositor fits Discord's text limits; some fields may be omitted when the lines are full. Save to apply on the next poll.

Keep **Group model, tokens and context** enabled for the structured layouts. **Activity/project heading (three rows)** switches between two arrangements:

- **Off (default):** Codex App or ChatGPT App remains the heading. The first text field shows `Thinking - Chat title`; the second shows model/effort followed by `Token: 29.5M - Context: 85% Used`. Discord wraps that lower field as space permits.
- **On:** `Thinking - Chat title` becomes the heading, model/effort occupies the first text field and token/context occupies the second. The original application ID, artwork and image tooltip stay unchanged.

Both arrangements respect visibility switches and custom labels. Privacy mode restores the normal application heading and hides work details. Long chat titles are fitted separately from model and usage text. Enabled cost, custom text or quota fields may add content to their configured group. Turn grouped layout off for the original fully custom field order; the activity/project heading toggle then has no effect. Discord controls wrapping and typography and cannot provide three custom text fields beneath an additional app heading.

## Settings

Use the **Appearance**, **Activity**, **Background** and **Advanced** section buttons. Switching sections keeps your edits. Save applies the settings across this tab; invalid input opens the relevant section so you can correct it. Appearance previews immediately. **Reload** restores saved appearance and discards this tab's edits.

- **Interface style:** Minimal uses compact outlined cards. Soft uses serif headings and rounder controls. Rounded uses pill controls and borderless cards. Paper uses Cambria headings, ruled sections and a page icon. Studio uses sans-serif headings, outlined rounded cards and a grid icon. All five change typography, spacing, boxes or icons independently of color.
- **Color mode:** Dark or Light, independent of the interface style. Choose both and click **Save changes**. Appearance changes preserve the active engine and Discord connection.
- **Color theme:** Neutral retains black and white. Slate uses cool blue, Forest muted green and Dusk warm purple. Each has dark and light variants. Interface style, color theme and color mode are independent; choosing colors does not change the box or typography style.
- **Hide presence when inactive / Hide after (minutes):** enable automatic hiding in Activity and choose 1–1440 minutes. The entire presence clears after Codex is idle or waiting for that long, and returns when work resumes. A long-running command stays active. Without prior activity, a missing session starts the timeout at the first empty poll; an unknown activity falls back to the session's last update. For a session already idle longer than your duration, hiding can happen immediately after saving. The dashboard shows the remaining idle time. Initially disabled, with 5 minutes offered when you enable it. This option does not quit the app or stop local monitoring; manual Pause and app priority remain respected.
- **Presence timer:** Continuous counts from when this companion app opened and continues through edits, project switches, pauses, reconnects and engine restarts inside the same app session. Closing/reopening the companion begins a new timer. Work / project uses the current session's last activity time and resets with new work. The selection updates without restarting the engine. The terminal launcher receives the same continuous start time when launched from this app.
- **Hide only the clock while idle or waiting:** the earlier clock-only option remains available independently of automatic presence hiding. It removes the clock immediately for Idle or Waiting for input. Continuous excludes that interval on resume; Work / project uses new work. It does not hide the presence itself. Initially off.
- **Give games and selected apps priority:** temporarily clears this desktop engine's Discord activity while a matching application runs. Session monitoring, metrics and the engine continue. It restores publication after the priority app closes, while respecting your existing manual Pause setting. Initially off. Checks run locally in a background task every three seconds; priority transitions wake the engine's poll loop.
- **Detect running Steam games:** uses Steam's local running-app marker when Steam is running; availability depends on the Steam client. Known game executable names are also recognized. Launchers alone do not pause Codex.
- **Other app executables:** add comma-separated executable names, such as `Spotify.exe, MyGame.exe`. Executable paths and case differences are accepted. A selected process receives priority for its entire running lifetime, even if minimized; this does not inspect whether it is actively publishing another Discord presence. For an unrecognized game, add its executable here. Discord chooses which remaining activity to display; this app cannot reorder another application's presence.

- **Plan:** automatic detection or manual selection: Free, Go, Plus, Pro 5x, Pro 20x, Business, Enterprise or Edu. **Show plan price** hides/shows the price; **Show subscription in Discord** hides/shows the entire plan name in Discord. Local monitoring still displays it.
- **Codex home:** absolute folder containing Codex sessions. Changing it saves your current configuration in the old home, switches to the new home and loads that home's settings; it does not move sessions or copy credentials.
- **Poll interval:** 1–60 seconds, default 1 for new preferences. Existing preferences are preserved. A shorter interval detects changes sooner.
- **Stale cutoff:** 1–86400 seconds, default 90.
- **Keep active session:** 60–86400 seconds, default 3600.
- **Keep running when the window closes:** the X hides the dashboard; the engine continues. Reopen the executable or double-click the tray icon to return. Choose Exit to stop everything.
- **Hide the system tray icon:** remove the icon while the app continues running. Combine with background launch or Run in background for a fully hidden dashboard. Reopen `Codex Presence.exe` to restore the existing window, then choose Exit when you want to stop it.
- **Start presence when opening:** optional automatic engine launch on future opens.
- **Open in background and start presence:** future launches start the engine and hide the dashboard automatically. This also starts presence when the separate automatic-start option is off.
- **Start with Windows:** optional current-user Run entry. Starts the engine in the background at sign-in without showing a window. It takes effect when you save; initially off. Disable it here before moving the app.
- **Start with Codex / ChatGPT in background:** watch for the ChatGPT or Codex desktop process. Saving enables the watcher immediately and registers this companion to watch at Windows sign-in. Automatic launches stay hidden from the moment the app starts, regardless of the manual-launch preference. An already-open dashboard stays open. It stops an automatically started engine when both hosts close; manually started engines remain running. Exit closes the watcher until you reopen this companion or sign in again. Activity statistics come from Codex sessions; this option does not parse ordinary ChatGPT conversations or detect browser tabs. Initially off.
- **Monitor locally without publishing:** gives live local data with no Discord activity updates. Restarting the engine applies the choice.
- **Include WSL:** optional extra session discovery, initially off.
- **Efficiency mode:** requests Windows EcoQoS and low process priority for the engine. Windows decides whether its leaf icon appears.
- Efficiency mode uses Below Normal priority so updates remain responsive under load. Git branch queries skip folders outside repositories and time out after 500 ms; a stalled lookup can leave the branch unavailable without blocking presence.
- **Branding:** every original image key and tooltip, per-activity small-image overrides, plus terminal logo mode/path. Asset keys refer to images available on the original Discord applications; they do not upload images. Terminal logo settings apply to the original terminal interface.
- **Pricing:** aliases and model rate overrides in JSON. Rates are USD per million tokens. Example below.
- **Engine identity:** the schema, IDs and public verification key. The original engine enforces its supported identities when validating settings.

```json
{
  "aliases": { "my-model-alias": "gpt-5.4" },
  "overrides": {
    "my-model": {
      "input_per_million": 1.0,
      "cached_input_per_million": 0.1,
      "output_per_million": 4.0
    }
  }
}
```

Click **Save changes** or press **Ctrl+S**. Monitoring changes restart the owned engine if it was running; appearance, timer, tray and startup preferences keep it running. Invalid numbers, paths and JSON are revealed with inline feedback and keyboard focus. Pending requests disable repeat submissions while navigation stays available. Unsaved edits stay on their tabs; **Reload** asks before discarding the current tab's edits and preserves edits made while loading. Exit offers **Review changes**, **Exit without saving**, and **Cancel**. Closing to the background keeps drafts in memory; they are not silently written to disk.

Use **Ctrl+Tab / Ctrl+Shift+Tab** to change tabs, **Ctrl+1–5** to select one directly, and the usual arrow keys, typing and Enter inside dropdowns.

Activity priority applies to the desktop engine. The original terminal interface manages its own publication. Timer and privacy settings remain shared. Process names and the Steam marker stay local and are not sent to Discord.

## Tools

**Doctor** checks setup and Discord connectivity. **Status** reads a one-time session/operational report. Both show actual output, including failures. The output area also holds recent engine errors. **Complete configuration** gives access to every original engine configuration field, including pricing maps and future-compatible JSON fields supported by this engine version. **Save changes** validates through the Rust engine before writing.

**Open original terminal view** opens the upstream GitHub project's terminal interface with the same engine/settings. It pauses the desktop-owned engine first. Press **Q** in the terminal to close it; desktop monitoring resumes if it had been running. Unsaved settings must be saved or reloaded first. It refuses to take over another running engine.

Version 1.3 fixes the launch error caused by output encodings being set on an unredirected terminal. Failed launches now restore desktop monitoring and leave the launcher usable for another attempt. **Create desktop shortcut** adds or updates the shortcut to this app's current folder.

Close the original terminal with Q before using Exit in the desktop app.

Reference buttons open this guide, the original docs, project folder or selected Codex home. The Codex wrapper is also retained:

```powershell
cd '.\Runtime'
.\codex-discord-rich-presence-windows-x64.exe terminal-view
.\codex-discord-rich-presence-windows-x64.exe codex --help
```

Stop the desktop engine before manually starting `terminal-view`; it rejects duplicate instances. Launching the runtime without arguments retains the original upstream takeover behavior. The original `discord-proof` command briefly publishes test activity and clears it; run it only when you want that external test.

## Files, backup and recovery

- `Codex Presence.exe`: native desktop app.
- `Runtime`: modified original Rust engine, logos, icon, checksums and SPDX dependency bill of materials.
- Source repository: desktop source, engine source, tests and original documentation, downloaded separately from GitHub.
- `Data/app-settings.json`: desktop preferences, created when preferences are saved.
- `Data/Backups`: previous presence configuration files, created before replacement.
- `<Codex home>/discord-presence-config.json`: original shared presence configuration.
- `<Codex home>/discord-presence-metrics.json`: latest original metrics snapshot.
- `<Codex home>/discord-presence-history-cache.json`: derived historical usage cache. It avoids scanning unchanged inactive files on every restart, and is invalidated by source length or modification time. It contains derived usage/session metadata, never message transcripts. The engine rebuilds missing, invalid or outdated entries from the source files; active token/cost accounting still parses the complete active session.

If another application edits the shared configuration, saving is rejected to preserve its edits. Click Reload, reapply your choices, and save. To restore a backup, exit the app first, copy the desired backup over `discord-presence-config.json`, and reopen. Do not replace Codex authentication files.

Backups can be on another drive from configuration. The app copies the previous file to its backup folder first, then atomically replaces configuration using a same-directory temporary file. Desktop preferences also use atomic replacement and exact backups under `Data/Backups/Preferences`. Damaged desktop preferences open with safe defaults and a warning, preserving the original file. Unchanged settings do not create extra backups. Version 1.7.0 uses schema 17. Preserve your previous app/runtime and compatible configuration before replacing an installed version; do not overwrite a running executable.

## Troubleshooting

No presence: open Discord desktop, enable its activity display setting, click Start, then run Doctor. A disconnected/retrying status means the IPC connection is not ready. Discord web alone cannot provide local IPC. No sessions: send a message in Codex, confirm the Codex home folder, and inspect Status. The app detects session activity; it does not change the Codex app itself.

Stale dashboard: check whether the engine is stopped. Expired sessions disappear based on cutoff and retention preferences. Missing cost/model/credits: inspect complete session/metrics data; unsupported or missing telemetry cannot be invented.

Can't save: check JSON syntax, numeric ranges and the footer error. Reload if another app changed the file. If the app is hidden, reopen its executable, including when the tray icon is disabled. If .NET is missing on another PC, install Microsoft's .NET 8 **Desktop Runtime x64**.

Discord connection retries back off from 2 seconds to at most 15 seconds, and a 5-second heartbeat checks an idle connection. Changed activity is published on the next configured session poll. Discord may add its own display delay. A first scan or a changed large session takes longer than a cached restart; lowering the poll interval cannot remove Discord's display delay.

## Build and verify

Requires the pinned Rust toolchain, Visual Studio C++ Build Tools with Windows SDK, and .NET 8 SDK.

```powershell
.\scripts\build-desktop.ps1
.\scripts\verify.ps1
.\scripts\verify-desktop.ps1
.\scripts\verify-desktop.ps1 -CrossVolume
```

The default build output is `.build/desktop` inside the repository. Pass an absolute `-OutputDirectory` to stage elsewhere; pass `-AppRoot` to verify that output. Desktop verification uses synthetic sessions with publication disabled. Each run checks 205 behaviors, including both heading toggles and parsed preview text, retained drafts, cross-tab merges, timer and usage settings, idle hiding/restoration, section navigation, edits during asynchronous saves, appearance without engine restart, all 40 style/theme/mode combinations at 460-pixel width, a real terminal launch/recovery, shortcut creation and background operation. Real startup registry entries are not changed. Cross-volume verification uses Windows TEMP on a different drive. Results and previews are under the chosen app root's `Data/Verification`. See `docs/desktop-release.md` for packaging and publication.

## Remove

Disable **Start with Windows** and **Start with Codex / ChatGPT in background**, then save. Choose Exit, then remove the shortcut and extracted app folder if desired. Shared presence configuration remains in your Codex home. Only remove presence-specific files there if you no longer use another presence client; leave sessions and authentication intact.

## Attribution

Based on [xt0n1-t3ch/Codex-Discord-Rich-Presence](https://github.com/xt0n1-t3ch/Codex-Discord-Rich-Presence), version 1.11.2, MIT license. The Windows desktop fork is maintained by w-rakeeb. `LICENSE.txt` and `NOTICE.md` preserve attribution; Discord identities and Codex/OpenAI branding are inherited from the upstream project. Building locally does not publish automatically.
