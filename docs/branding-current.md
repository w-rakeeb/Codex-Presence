# Current product artwork

The product owner supplied `codex-black-rounded-1024.png` from `D:\Codex\Codex Presence Logo\03-Final` as the approved logo on 2026-10-27. `assets/branding/codex-presence.png` is a byte-identical copy. The 640px source remains preserved in the original folder.

`scripts/export-branding.py` exports the existing artwork without redrawing the mark. It creates the app ICO, six native UI PNG sizes, status-badged tray variants and repository/social images. The optional export tool uses Pillow already present in the development environment; no runtime or package dependency is added. Run it with `--source` pointing at the approved 1024px image and `--delivery` pointing at a folder under the product's Branding directory.

The current app, executable resources, window/taskbar icon, desktop-shortcut icon, preview and release artwork use the same source. `desktop/Web/build.mjs` copies the native 256px mark into the bundled web view. Existing screenshots and artwork in explicitly older documentation are historical records. The independent Old Design installation remains preserved.

New Rich Presence defaults use the versioned public PNG URL in `PRODUCT_LOGO_URL`; application IDs are unchanged, and explicit custom image choices remain supported. Existing users can paste that HTTPS URL into their large-image field or upload the approved PNG as a Discord art asset. Activity images and the application/account icon in Discord's Developer Portal are separate settings; this release cannot edit someone else's developer application.

The current banner and social image are `assets/branding/codex-presence-banner.png` and `assets/branding/social-card.png`. Original MIT attribution is preserved in `LICENSE` and `NOTICE.md`.
