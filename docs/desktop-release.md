# Windows desktop release

Release the desktop application separately from the upstream terminal package. Desktop tags use `desktop-vX.Y.Z`; the Rust package version describes the engine base.

1. Update `desktop/CodexPresence.csproj`, the guide and release notes.
2. Run `scripts/verify.ps1` for Rust changes and build with `scripts/build-desktop.ps1 -OutputDirectory <absolute staging path>`.
3. Run `scripts/verify-desktop.ps1 -AppRoot <staging path>` and repeat with `-CrossVolume`. These checks use synthetic sessions and cannot publish Discord activity.
4. Exercise dropdown selection with mouse and keyboard, change tabs with unsaved values, return and save, and reopen a hidden app. Check 460-pixel width, enlarged Windows text, Dark/Light modes and all five styles.
5. Check the actual engine with `doctor` and `status`. Verify only the intended instance is running. Never replace a running executable.
6. Inspect the staged Git diff and included files. Exclude session data, authentication, personal settings, backups, logs and verification fixtures. Keep `LICENSE` and `NOTICE.md`.
7. Commit the checked source. Package with `scripts/package-desktop.ps1 -AppRoot <staging path>`. The package includes only the app, runtime, guide, attribution and checksums; it excludes local Data and Source directories.
8. Create an annotated desktop tag on the checked commit, push the fork and publish the zip plus SHA256SUMS.txt through a GitHub release. Do not replace published tag contents or assets.
9. Fetch the published release metadata and check the public source commit, asset size and SHA-256. Verify the extracted release package before closing the task.

The executable is unsigned. Do not describe it as signed or independently certified. Windows 11 is tested on the development machine; Windows 10 compatibility must be checked on a real Windows 10 machine before claiming that platform was tested. .NET support and Windows servicing lifetimes are separate from this application's compatibility.
