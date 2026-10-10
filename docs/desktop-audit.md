# Desktop audit

## 1.9.1 silent startup

Verified 2026-10-10 on Windows 11 x64. The desktop bridge gates publication on a fresh user prompt or task-start timestamp after the companion opens. No new dependency or preference migration is required.

- 301 Rust tests pass, with formatting and strict all-feature Clippy checks.
- 379 desktop checks pass on the same drive and across drives. Isolated engine checks cover restored working sessions, empty session folders, background token updates, fresh prompts, priority suppression, engine restart before and after work, and a new companion session.
- Verification uses monitoring-only fixtures and cannot publish Discord activity. Sign-in launch is checked using the existing background arguments; an actual PC reboot was not performed.

The 1.9.0 UI and platform limitations below still apply.

## 1.9.0 responsiveness and product quality

Audited 2026-10-07 on Windows 11 x64. The existing WPF/Rust architecture and all presence features are preserved; no dependency or web rewrite was added.

| Finding | Repair |
|---|---|
| Collapsed sessions constructed and refreshed hidden controls | Twelve initial headers, incremental Show more, expansion-time details and suspended collapsed updates |
| Settings built every section on entry | Each section builds on its first visit and retains its controls and draft |
| File hashes, backups, replacement and preferences blocked the UI thread | Worker-thread transactions, serialized reads/writes and captured destination paths |
| Unknown speed appeared as Standard; malformed numeric values appeared as zero | Explicit unknown/Fast/Standard mapping and unavailable numeric states |
| External publication edits left Pause / Resume stale | Separate bridge publication and monitoring-only fields; temporary suppression remains independent |
| Technical failures appeared in the main interface; host checks could fault unobserved | Actionable recovery text, retained Tools diagnostics and handled watcher failures |
| Cancel moved focus to navigation; input boundaries were faint | Focus restoration, named decision, consistent control sizes/states and 3:1 input-boundary contrast |
| Retained dashboard artwork and quota bars kept old appearance resources | Dynamic icon geometry, stroke, background, radius and meter brushes |
| Changing numeric widths introduced visual movement | Tabular numeric text in live values |

### Measured performance

The opt-in `scripts/verify-desktop.ps1 -Profile` measurement uses a synthetic 100-session snapshot, a 540-pixel WPF window and a warmed operation before each group of repetitions. It measures UI-thread execution including layout and UI-thread allocation, not network latency or browser INP. Dashboard construction is repeated five times, updates thirty times and fresh Settings construction ten times. Settings measures the Advanced section retained from the preceding verification flow. Version 1.9 initially shows twelve sessions; Show more exposes all hundred without replacing existing rows. The runtime still processes the complete snapshot.

| Median operation | 1.8.0 baseline | 1.9.0 |
|---|---:|---:|
| Construct dashboard for 100 sessions | 387.5 ms | 49.9 ms |
| Refresh dashboard values | 6.79 ms | 0.55 ms |
| Construct fresh Settings section | 200.2 ms | 34.0 ms |
| Dashboard construction allocation | 20.27 MB | 3.44 MB |

These are local sample measurements on the development PC, not guaranteed timings on other hardware. The final 1.9 sample had p95 values of 51.0 ms, 1.32 ms and 35.1 ms respectively. Raw baseline and after evidence stays in the private verification folder. The profiler is absent from normal operation and adds no telemetry.

### Verification

- 297 Rust tests, format checks and all-feature Clippy with warnings denied; a warning-free .NET Release build.
- 366 desktop checks with configuration on C: or D: and the application/backup on D:. These include all hundred sessions, retained controls, collapsed update suspension, independent full-data expansion, malformed/unknown telemetry, external publication changes during monitoring-only operation, readable errors, theme resources and focus restoration.
- All five pages and all four Settings sections at nine widths from 320 to 1920 logical pixels, including expanded application panels; scrolling and save/status controls at the minimum 320 × 420 size.
- Forty style/palette/mode combinations with secondary-text and primary-button contrast at least 4.5:1; all eight palette/mode input boundaries at least 3:1 against canvas, control and surface backgrounds.
- Existing atomic-backup, locked-file, damaged-preference, duplicate-action, asynchronous-draft, idle, priority, application-ID and original-terminal checks remain in the suite.
- Native mouse/keyboard checks selected Soft, selected Light with arrow keys and Enter, returned to the same draft after Ctrl+3 / Ctrl+4, and saved with Ctrl+S. A 24-session monitoring-only fixture expanded/collapsed details and loaded the remaining twelve through Show more. Scrollbar rail paging, a 320-pixel window and clean Exit were exercised. No fixture activity was published.

The practical limits below still apply. The interface intentionally avoids page fades, popup animations and repeating meter animations, so motion does not delay interaction or disregard reduced-motion preferences.

## Earlier audit: 1.7.0 and 1.8.0

## 1.8.0 application configuration follow-up

The custom Discord application update passed 297 Rust tests and 335 desktop checks in both same-drive and cross-drive configurations. The new controls select Default or Custom, validate a public ID, retain valid custom values when returning to Default, and apply application-specific asset keys. External malformed configuration keeps the previous valid runtime settings. Synthetic monitoring-only bridge checks verify ID reloads on the same process, both heading layouts and default restoration. New expanded artwork and built-in ID panels pass the nine-width horizontal bounds checks below.

Native mouse/keyboard verification selected Custom, triggered an empty-ID inline error with focus, entered a public-format fixture ID, saved with Ctrl+S, selected Default with the mouse and saved while retaining that ID. The fixture remained unable to publish to Discord and exited through Overview. The actual custom application's live connection and artwork remain dependent on a registered ID supplied by its owner. No bot credentials or permissions are required or stored. The 1.7.0 audit and its platform limits remain applicable.

Audited 2026-10-05 on Windows 11 x64. The product remains a native .NET 8 WPF dashboard with a Rust session engine and Discord IPC. It has five pages, four Settings sections, retained per-page drafts, local JSON configuration, a tray menu, host watcher and terminal handoff. It has no web routes, browser console, server database, account login, uploads, pagination or cloud storage; those parts of a web-app checklist do not apply.

## Findings and repairs

| Area | Finding | Result |
|---|---|---|
| Requests | Repeated actions remained clickable; diagnostics could lose their output when changing tabs | Busy controls disable; navigation remains available; diagnostics retain their own output view |
| Reload | A tab change could discard the wrong draft; unchanged configuration could preserve the old controls | Reload captures its tab and edit version, asks before discarding, and preserves newer edits |
| Validation | Invalid inputs only reached the status panel | Invalid monitoring values, paths and JSON reveal their section, show inline feedback and receive focus |
| Preferences | Corrupt JSON could prevent startup; direct writes risked incomplete files | Visible safe defaults and warnings preserve the original; atomic writes retain exact backups and skip unchanged files |
| Failed saves | Runtime preferences could diverge from a failed save | Previous monitoring preferences are restored; an existing engine survives a blocked write |
| Exit | Hidden drafts blocked exit without a visible path forward; X could show a dialog during the Close event | Review, Exit without saving and Cancel appear visibly; deferred close handling avoids the Windows event restriction |
| Layout | Fixed minimum width and field rows prevented compact use | Adaptive navigation, two-column Settings sections and stacked field controls support 320-pixel width |
| Scrolling | Custom rail lacked page commands | Thumb drag and page-up/down rails work; short windows keep scrolling |
| Accessibility | Window controls lacked useful names; keyboard focus was unclear | Named captions, focus outlines, labeled controls and polite status/error announcements |
| Tools | Portable package lacked the local original documentation path | Original documentation uses the public source page when local source is absent |
| Reliability | Commands could wait indefinitely; extreme quota timestamps could throw | Owned command children time out; invalid reset times show Unavailable |
| Activity | Cache assumed changed files were appended | Length, modification time and bounded prefix/tail checks refresh rewritten sessions without dropping split appends |
| Background load | Bridge sleep loop woke ten times per second between polls | Control messages wake the wait immediately; the stop check needs at most one timed wake per second |

## Verification

- Rust: 232 library, 23 integration and 31 core tests; formatting and Clippy with warnings denied. Pricing catalog and cost ownership are unchanged.
- Desktop: 308 runtime checks on each of two configurations, with application backups on D: and Codex settings on D: or C:. Fixtures cannot publish Discord activity.
- All five pages and all four Settings sections at widths 320, 375, 390, 430, 768, 1024, 1280, 1440 and 1920 logical pixels; minimum-height scrolling at 320 × 420. Checks measure real WPF element bounds, rather than source declarations.
- Five styles × four color themes × two modes: 40 combinations at 460 pixels, checking secondary text and primary-button contrast at 4.5:1 or better.
- Persistence and failure flows: external configuration edits, exact backups, no-op saves, locked preference files, malformed JSON, invalid paths/numbers, asynchronous saves/reloads, newer edits and repeated requests.
- Engine flows: parsed session activity and chat titles, both presence-heading layouts, timed idle expiry/resume, manual pause, synthetic game/custom-app priority, background host transitions, duplicate-engine rejection and terminal failure/exit recovery.
- Native mouse/keyboard: style selection, keyboard theme selection, immediate preview, switching tabs with drafts, Ctrl+S saving, resizing to 320 pixels, scrollbar dragging and field reordering. Native X testing found the Close-event issue; the corrected build successfully shows its decision, Cancel restores the draft, and saving enables a clean close. The actual Close event is also covered by runtime regression checks.

`scripts/verify.ps1` and `scripts/verify-desktop.ps1` reproduce the automated checks. Screenshots and fixture results stay in ignored `Data/Verification` folders; user settings and transcripts are excluded from packages and Git.

## Practical limits

This is tested coverage, not certification that every possible interaction and environment is bug-free. Windows 10 execution, mixed-monitor DPI transitions, enlarged OS text and screen-reader user testing remain outstanding. Small-width checks use WPF logical pixels, not a mobile browser. No real games are launched during verification; Discord decides which concurrent activity it displays and controls its own text wrapping. Public-source and downloaded-package verification are separate release gates. The portable executable is unsigned and requires the .NET 8 Desktop Runtime x64.
