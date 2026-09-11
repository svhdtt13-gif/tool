# PR #7 — Input quality fixes

## Goal

Stabilize the real input pipeline demonstrated by PR #6. Do not replace the real pipeline with sample/mock behavior.

## Bugs observed in Notepad acceptance

1. Printable characters are duplicated (one physical keystroke can produce 2–3 characters).
2. Vietnamese text / IME input does not work correctly.
3. Latency metrics are absurdly large because enqueue timestamps and the latency clock are using incompatible timestamp domains.

## Required fixes

### 1. Eliminate duplicate keyboard text

Review the keyboard injection path, especially `SyncTargetAdapter` / `Win32MessageAdapter`.

Do not generate duplicate text by sending a `WM_CHAR` in addition to a key path that already causes the target to generate character input. Preserve correct key down/up semantics for non-printable and modifier keys.

Acceptance:
- `abcABC123` typed once in Source appears exactly once in every enabled Notepad Target.
- Shift/Ctrl/Alt, Backspace, Enter, Tab, arrows and function keys retain sensible key semantics.

### 2. Vietnamese / Unicode / IME

Make the input path support normal Unicode text entry in Windows Notepad, including Vietnamese with an installed Vietnamese keyboard/IME (Telex/VNI where applicable). Do not fake the result by directly replacing target document text.

Choose an implementation compatible with the existing architecture. If low-level keyboard events alone cannot faithfully reproduce IME composition, add an explicit text/composition path with clear separation from physical key events and prevent double emission.

Acceptance:
- `Tiếng Việt có dấu: ă â ê ô ơ ư đ Đ` can be entered correctly in Source and appears correctly in every enabled Notepad Target.
- No duplicate characters.
- Existing ASCII and modifier behavior remains correct.

### 3. Fix latency metrics

Use one consistent monotonic clock domain (prefer `Stopwatch.GetTimestamp()`) from event enqueue through dispatch/latency recording. Do not feed `DateTime` ticks or another incompatible timestamp into a `Stopwatch`-based tracker.

Acceptance:
- Idle/normal Notepad sync reports realistic latency in milliseconds, not millions of seconds.
- Avg latency <= max latency.
- Metrics remain stable over repeated start/stop cycles.

## Regression / reliability

- Keep `Events received`, `Events dispatched`, `Events dropped` accurate.
- Target close still produces `WINDOW_LOST` without stopping other targets.
- Emergency Stop remains high priority and releases pressed state.
- `dotnet build InputSync.sln` must finish with 0 warnings and 0 errors.
- `dotnet test tests/InputSync.Tests` must pass; add regression tests for the three bugs where practical.
- Update README/acceptance notes if behavior changes.

## Deliverable

Implement the fixes in this branch and open a PR against `main`. Do not merge the PR automatically; it will be reviewed separately.
