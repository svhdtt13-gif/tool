# Input Synchronizer Design System

## 0. Research Log

- Embedded refs: shortlisted IBM, Sentry, and Linear for a data-dense operator console; picked `taste-skill` + IBM because Carbon's structured light surfaces and semantic status colors fit a Windows utility.
- Lazyweb: skipped because this is a native WPF utility with an explicit control inventory, not a web product or visual clone.
- Imagen drafts: skipped because no image-generation tool is available and the operational UI requires no imagery.

## 1. Atmosphere & Identity

A calm, exact Windows command center: dense enough to monitor synchronization at a glance, but never crowded. The signature is a strong black status rail above pale, square-edged work surfaces, with color used only for commands and runtime health.

## 2. Color

| Role | WPF resource | Value | Usage |
|---|---|---|---|
| Canvas | `CanvasBrush` | `#FFFFFFFF` | Window background |
| Surface | `SurfaceBrush` | `#FFF4F4F4` | Panels and fields |
| Surface hover | `SurfaceHoverBrush` | `#FFE8E8E8` | Hover and selected rows |
| Ink | `InkBrush` | `#FF161616` | Primary text and status rail |
| Muted ink | `MutedInkBrush` | `#FF525252` | Captions and helper text |
| Border | `BorderBrush` | `#FFC6C6C6` | Dividers and field edges |
| Accent | `AccentBrush` | `#FF0F62FE` | Primary action and focus |
| Accent pressed | `AccentPressedBrush` | `#FF002D9C` | Pressed primary action |
| Success | `SuccessBrush` | `#FF198038` | Active and running |
| Warning | `WarningBrush` | `#FFF1C21B` | Paused and disabled |
| Danger | `DangerBrush` | `#FFDA1E28` | Lost states and emergency stop |
| White text | `OnDarkBrush` | `#FFFFFFFF` | Text on dark or chromatic fills |

Color never acts as the only state indicator; every badge and toggle also has a text label.

## 3. Typography

The primary stack is `Segoe UI, Arial`; metrics and event logs use `Cascadia Mono, Consolas`. The scale is 28px/Light for the product title, 20px/Semibold for major status, 16px/Semibold for panel headings, 14px/Regular for controls and body, and 12px/Semibold for labels and badges. Body text is never smaller than 14px; 12px is limited to short metadata.

## 4. Spacing & Layout

All spacing follows an 8px base with 4px micro spacing. The shell has a fixed header and footer command strip; the center is a two-column grid (selection workspace and metrics/debug rail) whose content owns scrolling. Standard panel padding is 16px, section gap 16px, control gap 8px, and window margin 24px. The minimum supported window size is 960x680; long window titles trim with an ellipsis.

## 5. Components

### Command button
- **Structure**: rectangular WPF `Button` with single-line verb label.
- **Variants**: primary blue, secondary charcoal, quiet gray, danger red.
- **States**: default, hover tonal shift, pressed darkening, visible blue keyboard focus, disabled gray.
- **Accessibility**: 40px minimum height, access keys, descriptive tooltips where needed.

### Field
- **Structure**: label above a square `ComboBox` or list.
- **States**: default pale surface, hover, selected tint, focus outline, disabled muted.
- **Accessibility**: associated label via `Target`, keyboard operable, no placeholder-only labeling.

### Status badge
- **Structure**: pill-shaped `Border` containing explicit uppercase state text.
- **Variants**: ACTIVE/RUNNING success, DISABLED/PAUSED warning, WINDOW LOST/SOURCE LOST danger, READY neutral.
- **Accessibility**: state is communicated by text as well as color.

### Metric tile
- **Structure**: label over a mono numeric value on the shared surface.
- **States**: read-only; no hover treatment.
- **Accessibility**: logical reading order and clear unit labels for latency.

### Toggle button
- **Structure**: feature name plus explicit ON/OFF state.
- **States**: blue ON, gray OFF, keyboard focus, disabled.
- **Accessibility**: `IsChecked` exposes state and the text updates with it.

## 6. Motion & Interaction

Motion intensity is 2/10. There are no automatic animations; hover, pressed, selection, and focus feedback are immediate. Emergency Stop remains enabled whenever the controller can receive commands and has the `Alt+F10` access path in addition to the global hotkey contract.

## 7. Depth & Surface

Use tonal shift only: white canvas, gray-10 panels, gray-20 nested areas, and the dark status rail. No card shadows. Borders are limited to dividers and control outlines; controls and panels use 0px corner radius, while status badges are the only pill exception.

## 8. Accessibility Constraints & Accepted Debt

Target WCAG 2.2 AA contrast, complete keyboard reachability, visible focus, explicit state labels, and no color-only meaning. Tab order follows source, targets, feature controls, lifecycle commands, metrics, then debug log.

| Item | Location | Why accepted | Owner / Exit |
|---|---|---|---|
| Runtime window discovery is represented by sample data | `InputSync.UI` composition root | Win32 discovery/controller implementation belongs to sibling slices | Replace the sample controller when the concrete `ISyncController` composition is available |
