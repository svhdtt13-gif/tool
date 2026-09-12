# Game evidence — qnyh.exe (Thien Nu client, Engine 3.739133)

Date: 2026-09-12. Harness: throwaway console app OUTSIDE this repo
driving `Win32MessageAdapter` and `ForegroundSendInputAdapter` directly.
Screenshots cropped per target window. No game processes touched
(no kill, no memory access, no driver).

10 clients were running simultaneously (multi-box setup).

## Method problem found first

Full-screen pixel diff is polluted (browser, animations). All numbers
below are cropped to the target window. A no-input control run still
showed up to ~55% diff on some clients: **the bots auto-walk and fight
on their own**, so diffs alone prove nothing — screenshots were read
by a human for every verdict.

## Results

| # | Backend | Target | Input | API result | Observed | Verdict |
|---|---|---|---|---|---|---|
| 1 | Win32 PostMessage | client_15 | W hold 800ms | True,True | No change (ambient only) | NOT consumed |
| 2 | Win32 PostMessage | client_15 | Enter, `abc123`, Enter | all True | No chat text | NOT consumed |
| 3 | Win32 PostMessage | client_1 | Esc | True,True | No menu | NOT consumed |
| 4 | SendInput foreground | Notepad (control) | `qkp123` | all True | Exact readback | DELIVERY works |
| 5 | SendInput foreground | client_1 | Enter, `abc123`, Enter | all True | No chat text | Unproven (hotkey uncertain) |
| 6 | SendInput foreground | client_46 | W hold 2000ms | down True, up False (focus lost) | No movement | Unproven (focus lost) |
| 7 | SendInput foreground | client_46 | Left-arrow hold | True,True | No movement (identical frames) | No WASD/arrows movement observed |
| 8 | SendInput foreground | client_46 | M / B | True,True | No map/bag | Bindings uncertain |
| 9 | SendInput foreground | client_46 | click ground + 4s | True | Scene changed to new area | LIKELY consumed (confounded by auto-walk, see control) |
| 10 | SendInput foreground | client_46 | Space | True,True | Teleport loading screen appeared | Confounded (bot may teleport itself) |

## Conclusions (conservative)

- **Win32 `PostMessage` keyboard: NOT consumed by qnyh.** Four runs,
  OS-accepted, zero game effect. Do not claim Win32 support for this game.
- **SendInput delivery: proven** on Notepad (exact text), API-accepted
  on the game with focus held.
- **SendInput game effect: mouse click LIKELY, keyboard UNPROVEN.**
  WASD/arrows produced no movement; chat/map/bag hotkeys uncertain;
  click-to-move coincided with a scene change but bots auto-walk.
- **Focus is yanked mid-hold** by an unidentified window (twice).
  Any foreground test must re-assert focus and retry key-up, or keys
  can stick down in the game.
- **UniKeyNT interferes**: synthetic `test123` came back `t�t123`
  (Telex composition raced the stream). Test strings must avoid
  Telex triggers, or accept IME transformation.

## What is needed for a decisive verdict

- Key bindings for this game (move keys? chat hotkey? bag/map keys?).
- One idle client in a safe zone with botting paused during the test.
- Identity of the focus thief (observe popups during a hold).
- Then: re-run movement + chat + click battery on the quiet client.

## Scope consequences

- SendInput is **single-foreground-target only**. It cannot satisfy
  1-source → N-game-clients simultaneously; do not present it as the
  multi-target solution.
- A real broadcast/multi-target game backend is still an open design
  item, pending the verdict above (Raw Input vs DirectInput vs
  async-state polling determines what is even possible).
- No anti-cheat evidence either way; no bypass attempted or planned.
