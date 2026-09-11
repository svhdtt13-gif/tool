# Input backends — which apps games can be driven, and how to tell

Notepad is the background test. Real acceptance is a **game**. Posted
Win32 messages do not work with every input backend.

## Backend matrix

| Backend | Examples | Win32 `PostMessage` | `SendInput` foreground | Notes |
|---|---|---|---|---|
| Win32 messages (`WM_KEYDOWN`, `WM_CHAR`) | Notepad, classic Win32 UI, many Unity/Unreal menus | Works | Works | MVP path |
| Raw Input (`RegisterRawInputDevices`) | Most modern FPS/action games | Ignored | Works (real input) | Game reads HID stream, not messages |
| DirectInput | Older titles | Ignored | Usually works | Polls device state |
| Anti-cheat protected | Competitive online games | Blocked | Blocked | Out of scope, never bypass |

## Identifying a game's backend

1. Run the game as the only target, START SYNC, type once.
2. If the game reacts: Win32-message compatible, done.
3. If not: check with a spy tool whether the game window proc receives
   `WM_KEYDOWN` (message-compatible but ignoring) vs nothing consumed.
4. Raw Input games need the foreground `SendInput` adapter instead of
   background broadcast — one source at a time, no multi-target.

## Current implementation

- `Win32MessageAdapter`: background broadcast via `PostMessage`
  (`WM_KEYDOWN`/`WM_KEYUP`, `WM_CHAR` for text, mouse messages).
- `ForegroundSendInputAdapter`: foreground injection via `SendInput`.
- `SyncTargetAdapter`: per-target endpoint resolution (edit child for
  text, top-level for mouse), relative/absolute coordinates, latency.
- Text path is observed source text mirrored as `WM_CHAR`; control keys
  forward as `WM_KEYDOWN`/`WM_KEYUP` with per-target pressed-state
  tracking and release-all on Stop/Emergency.

## Reporting a game test

Include: game title + version, windowed/fullscreen, which backend you
suspect, what happened per input (move/click/keys), and the mouse trace
line from the DEBUG panel.
