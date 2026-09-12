# Input Synchronizer

Windows tool: chọn **1 cửa sổ nguồn (Source)** và **nhiều cửa sổ đích (Targets)**.
Thao tác bàn phím + chuột từ Source được chuẩn hóa rồi phân phối tới các Target.
Triển khai cho [issue #2](https://github.com/svhdtt13-gif/tool/issues/2).

```
Source: Game A
Targets: Game B, Game C, Game D

Game A input
    ↓
Capture → Normalize → Queue → Dispatcher
                         ↓
                 ┌───────┼───────┐
                 ↓       ↓       ↓
               Game B  Game C  Game D
```

> Lưu ý: Win32 message là adapter MVP. App/game dùng Raw Input, DirectInput
> hoặc engine-specific input có thể cần adapter khác. Không né anti-cheat
> hay cơ chế bảo vệ của phần mềm.

## Kiến trúc

```
WindowManager → InputCapture → EventNormalizer → EventQueue
    → EventDispatcher → TargetAdapter → Target HWNDs
```

| Project | Nội dung |
|---|---|
| `src/InputSync.Core` | Models, Capture (hooks), Normalizer, Queue, Dispatcher, Safety, Controller, Persistence |
| `src/InputSync.Win32` | P/Invoke, `WindowManager`, `Win32MessageAdapter` (PostMessage), `ForegroundSendInputAdapter` |
| `src/InputSync.UI` | WPF UI: Source/Targets, Start/Pause/Stop, Emergency Stop, metrics |
| `tests/InputSync.Tests` | xUnit: Normalizer, Queue, StateTracker, Config |

Nguyên tắc bắt buộc (từ issue):

1. HWND là runtime identity; title chỉ để hiển thị.
2. Capture và Dispatch tách thread/queue (Channel bounded).
3. Mouse dùng client-relative/normalized coordinates.
4. State bàn phím/chuột theo dõi riêng từng target.
5. Injection chỉ qua `ITargetAdapter`.
6. Một target lỗi/đóng không crash hệ thống (`IsWindow` trước mỗi send).
7. Emergency Stop thoát nhanh, không qua queue.
8. Không hard-code một phương thức injection duy nhất.
9. Không bypass anti-cheat.
10. Mỗi Slice có test/acceptance.

## Yêu cầu

- Windows 10/11 x64
- .NET SDK 8+ (đã verify với SDK 9.0.318; VS Code 1.137.0)

## Chạy

```powershell
dotnet build InputSync.sln
dotnet run --project src/InputSync.UI
dotnet test tests/InputSync.Tests
```

## Test với Notepad (Slice 6 acceptance)

1. Mở 3 cửa sổ Notepad.
2. Chạy app → Refresh → chọn 1 Source + tick 2 Targets.
3. Coordinate = Relative, Keyboard/Mouse = ON.
4. START SYNC → gõ `ABC123` trên Source.
5. Kỳ vọng: cả 3 cửa sổ đều hiện `ABC123` đúng 1 lần; Events received/dispatched > 0.
6. F8 Start/Pause, F9 Stop, F10 Emergency Stop.
7. Đóng 1 Target → target đó `WINDOW LOST`, các target khác vẫn chạy.
8. STOP bất kỳ lúc nào → không kẹt phím (ReleaseAll).

## Thiết kế đường input (PR #7)

Mỗi phím vật lý chỉ đi đúng 1 đường, không bao giờ phát text 2 lần:

- Phím điều khiển (Shift/Ctrl/Alt, arrows, F-keys, Esc...) → forward `WM_KEYDOWN`/`WM_KEYUP`.
- Mọi phím còn lại → KHÔNG forward key; text được đọc từ nội dung
  Source quan sát được (diff) rồi gửi `WM_CHAR` tới từng target.
  Nhờ đó Unikey/Telex/VNI, IME, paste đều đúng nguyên văn, kể cả tiếng Việt.
- Latency đo bằng một clock duy nhất (`Stopwatch.GetTimestamp()`).
- Đồng bộ text là hậu-xử-lý debounce (~25ms trailing) trên 1 worker
  tuần tự (không đẻ Task theo từng phím): sự kiện phím →
  Windows xử lý xong → đọc text → diff → emit. Gõ nhanh, giữ phím
  repeat, Backspace/Delete giữ, Ctrl+X/Z/Y, Telex, paste đều qua một
  đường duy nhất nên không lệch nhịp, không mất repeat, không đảo delta.
- Chuột đi đường riêng không nghẽn: MouseMove được coalescing (tối đa
  ~8ms/event, luôn lấy vị trí mới nhất), click Down/Up và wheel không
  bao giờ gộp và luôn flush move đang chờ trước để giữ thứ tự.
- Text chỉ emit khi Source đã ổn định (2 lần đọc liên tiếp giống nhau);
  gõ liên tục không dứt thì timeout vẫn emit hiện tại, không mất chữ.
- Chuột scale theo top-level client rect Source → top-level client rect
  Target (tỷ lệ tương đối, miễn nhiễm DPI); không dùng kích thước Edit
  child. Keyboard vẫn post vào Edit child để nhận `WM_CHAR`. Panel DEBUG
  hiện dòng trace live: raw[phys] → logical@dpi → client → normalized →
  target@DPI → translated.
- Game thật là acceptance bắt buộc (Notepad chỉ là nền): xem ma trận
  backend và cách xác định trong [docs/INPUT_BACKENDS.md](docs/INPUT_BACKENDS.md).
- Clipboard: `Ctrl+C/X` không forward (target không được ghi đè clipboard
  chung); nội dung copy được snapshot nội bộ theo clipboard sequence.
  `Ctrl+V` phát đúng snapshot đó tới từng target; không có text thì
  forward phím V như cũ. Phím chữ với Ctrl giữ (trừ V/X/Z/Y/C) và
  Insert/Delete với Ctrl/Shift cũng đi đường text-mirror.

Chi tiết phím bổ trợ (PR #8): modifier (Shift/Ctrl/Alt/Win), arrows,
Home/End, F1–F12, Esc đi đường key với đúng thứ tự
DOWN → key DOWN → key UP → modifier UP, state độc lập từng target.
Khi giữ Ctrl/Alt, mọi phím đi đường key để shortcut (Ctrl+A/C/V/X,
Alt+menu) chạy trên target từ clipboard/state hệ thống chung; text-mirror
chỉ quan sát im lặng lúc đó nên không duplicate. Thả modifier ra, gõ
thường tiếp tục mirror bình thường.

Giới hạn known: app/game không đọc được text (ô chat game...) thì phím chữ
trong đó không mirror được; phím điều khiển vẫn forward bình thường.

## Config (Slice 9)

JSON, HWND chỉ là runtime handle (không persist):

```json
{
  "source": { "process_name": "game.exe", "window_title": "Game A" },
  "targets": [],
  "keyboard": true,
  "mouse": true,
  "coordinate_mode": "relative",
  "hotkeys": { "toggle": "F8", "stop": "F9", "emergency_stop": "F10" }
}
```

## Slices

Slice 1 Window Discovery · 2 Keyboard Capture · 3 Mouse Capture ·
4 Normalization · 5 Dispatcher · 6 Win32 Adapter · 7 State Safety ·
8 UI · 9 Persistence · 10 Reliability (12 tests pass).
