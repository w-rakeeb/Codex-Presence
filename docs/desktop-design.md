# Desktop 2.0 visual redesign

The dashboard remains a .NET 8 WPF application with the existing Rust engine. No UI framework or runtime dependency was added.

## Components and ownership

`MainWindow.xaml` owns the title bar, navigation, pinned preview area, scrolling content, existing save area and sticky footer. `Theme.xaml` owns UI palettes, typography, spacing, radii and shared control templates. `Appearance.cs` selects resources and requests Windows backdrop/corner treatment with an opaque graphite fallback.

`MainWindow.Presentation.cs` supplies the surface, engine status, overflow menu, Discord preview, stat tile, usage bar, setting descriptions, tool rows, toast, tray artwork and transitions. Existing view builders retain their settings bindings and validation. Five interface styles and all four color palettes remain available.

`WindowPresentationState.cs` stores only window geometry, last tab and the System theme override in `Data/window-state.json`. Existing desktop preferences and shared engine configuration keep their formats and save behavior.

## Preserved contracts

- No Rust, Discord formatter, IPC, identity, pricing, session parsing, startup gate, configuration merge or monitoring changes.
- Preview name, details and state render verbatim from the engine snapshot. Discord separators and custom labels remain unchanged.
- Privacy and Layout show saved output, clearly labeled. Changes remain drafts until Save; Reload and cross-tab draft retention work as before. Autosave would change the settings contract.
- New artwork is local to the desktop app and tray. Discord IDs, asset keys and remote artwork are unchanged. The preview uses the local companion mark; arbitrary custom remote artwork is not inferred or downloaded.
- Installation preserves settings and startup registration. Background startup still waits for fresh Codex work.

## Contrast adjustments

The supplied small muted text and primary hover required AA adjustments. Dark muted text uses `#9CA2AC`; light muted text uses `#5B616C`. Dark primary hover uses `#5C68DF` so white button text remains readable. Input outlines use stronger tokens; decorative separators remain subtle.

Executable checks cover text against canvas, surface, raised, hover and Discord-card surfaces in all eight palettes, primary button states at 4.5:1, and input outlines at 3:1. The generated contrast report records every body-text measurement. Disabled controls are exempt.

## Verification and limits

The desktop suite retains configuration, privacy/layout, drafts, startup, terminal, shortcut, background, async, quota, identity and keyboard coverage. Added checks cover state scenes, pinned previews, exact preview text, window-state persistence, System theme isolation, palettes, high-contrast resources, minimum/large windows and WPF rendering at 96/120/144/192 DPI.

Scaled renders are not physical Windows display-scaling tests. OS text-size preferences, mixed-monitor transitions and a full screen-reader session were not certified. Native mouse/keyboard checks cover dropdowns, live appearance preview, draft retention, pinned scrolling, light mode, Ctrl+S feedback and graceful close in a monitoring-only fixture.

The local before/after gallery includes all five tabs and live, paused, stopped, error, Discord-not-running, loading and idle scenes. All scenes use isolated fixtures and never publish Discord activity.

## Decisions

No additional approval is needed for this visual update. Autosave, rewording Discord payloads, uploading Discord assets or replacing the original identity would require a separate functional change and are excluded.
## Final self-review

- Shared 4px spacing scale, consistent card/control radii, layered graphite surfaces and centered 720px content.
- No UI hex colors outside Theme.xaml; input outlines and button states pass the recorded contrast gates.
- All five tabs, seven engine scenes, loading/empty/error feedback and retained settings are covered.
- Keyboard shortcuts, arrow navigation, visible focus and UIA names are verified; native screen-reader certification remains outstanding.
- Save/Reload, privacy/layout drafts, engine binary and outgoing Discord strings are preserved.
- 100/125/150/200% WPF render-scale images and high-contrast resources are checked. Physical display/text scaling and mixed-monitor DPI remain explicitly unverified.

# Desktop component system

The desktop app remains .NET 8 WPF. daisyUI 5 is a CSS library, so its classes cannot style native WPF controls. This redesign adapts the daisyUI skill's component structure and semantic colors to existing WPF templates without adding a browser runtime or changing the presence engine.

## Component selection

| Purpose | daisyUI pattern | Native implementation |
| --- | --- | --- |
| Main navigation | `tabs-border`, `tab-active` | Shared `TabButton`, keyboard navigation, active underline |
| Related settings | `fieldset`, `fieldset-legend`, `label` | Shared settings cards, heading, description, existing controls |
| Engine and tools | `card`, `card-body`, `card-actions` | `Surface`, `BuildEngineControlCard`, `ToolRow` |
| Session numbers | `stats`, `stat-title`, `stat-value` | Joined `StatTile` group with inset padding and hairline separators |
| Main action | `btn-primary` | `PrimaryButton`, one state-dependent engine action |
| Secondary actions | `btn`, `btn-ghost` | Native default, `GhostButton`, `QuietDangerButton` |
| Boolean setting | `toggle`, `toggle-primary` | Existing accessible CheckBox template and retained draft bindings |
| Preview annotation | `badge` | Compact neutral rounded badge, separate from outgoing text |

Cards suit independent engine/preview content; fieldsets suit related settings. Session values use a single joined stats group rather than repeating three cards. Bordered tabs retain the existing five destinations without adding a sidebar to a compact window.

## Semantic theme mapping

All palette values remain in `desktop/Theme.xaml`, including the existing Graphite, Slate, Forest and Dusk variants. `Appearance.cs` selects light/dark/system and Windows high-contrast resources.

| daisyUI role | WPF resource |
| --- | --- |
| base-100 | Surface |
| base-200 | Canvas |
| base-300 | Control |
| base-content | Text, Secondary, Muted |
| primary / primary-content | Accent / OnAccent |
| neutral | Control |
| success / warning / error | Success / Warning / Danger |
| border, field radius, box radius | Line, InputRadius, CardRadius |

Use color for meaning, never to decorate entire settings sections. Numeric values remain tabular. Exact engine preview strings and outgoing Discord fields are unmodified. Existing manual Save/Reload, unsaved draft protection, config schema, background startup gate and tray behavior are retained.

## Component tree

```text
App shell
  Title bar
  Bordered tab navigation
  Pinned layout preview
  Scrollable page
    Overview: engine card, Discord preview, joined stats, usage details
    Privacy: preview, publication fieldset, visible fields
    Layout: text layout, labels, field ordering
    Settings: Appearance / Behavior / Startup / Advanced fieldsets
    Tools: diagnostic action rows, complete configuration, reference
  Save/Reload draft actions
  Status strip
```

The verification suite captures all five tabs, engine states, light/dark palettes, high contrast, small/large windows and 100/125/150/200 percent WPF render scales. Raster scale tests do not certify physical display scaling or screen reader behavior.
