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

## Backend selection (UI)

The app offers `Broadcast` (default) and `Foreground` in the backend
dropdown. Broadcast posts to every enabled target in the background.
Foreground swaps the engine adapter to `SendInput`: only the focused
window receives anything, extra targets report `WINDOW LOST`, and the
text mirror turns off (real keystrokes produce text natively, including
IME). Capture-all is used in this mode because the source cannot hold
focus while the target receives. This mode is single-target by nature
and does not satisfy 1-source → N-clients.

## Runtime evidence (this machine, 2026-09-11)

- `ForegroundSendInputAdapter` verified live: fresh Notepad focused via
  `AttachThreadInput` + `SetForegroundWindow`, typed `qkp123` through
  `SendInput` down/up pairs, read back exact via `WM_GETTEXT`. PASS.
- Typing `test123` instead produced `t�t123`: the global Vietnamese IME
  (`UniKeyNT`) intercepted the synthetic `es` as a Telex sắc-tone
  composition and raced the stream. Lesson: synthetic keystrokes go
  through any active IME/hook exactly like physical ones — Telex-trigger
  sequences must be expected in test strings, and Unikey-style composition
  is why the broadcast path mirrors observed text instead of raw keys.

## Current routing (no BackendMode switch yet — deliberate)

`RealSyncController` always drives the Win32 broadcast adapter today.
A `BackendMode` (broadcast vs foreground SendInput) switch is intentionally
**not built yet**: game evidence shows Win32 messages are ignored by qnyh
while SendInput is single-foreground-target by nature, so a switcher would
suggest a multi-target capability that does not exist. It will be designed
only after the game verdict (Raw Input vs DirectInput vs async-state)
determines what is actually possible — per reviewer direction, no blind
patches. The per-input trace (`kbd … -> 0xHWND`, `mouse … -> (x,y)`)
already shows capture → router → backend → API result for diagnosis.

## Reporting a game test

Include: game title + version, windowed/fullscreen, which backend you
suspect, what happened per input (move/click/keys), and the mouse trace
line from the DEBUG panel.
