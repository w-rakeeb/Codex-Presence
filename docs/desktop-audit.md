# Desktop audit — 1.7.0

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
