# CavaWin 🎵

A sleek **real-time audio spectrum visualizer for Windows 10/11** — a windowed
take on the classic [cava](https://github.com/karlstav/cava). It dances to whatever
sound **your system is playing** (Spotify, browser, games, players) via WASAPI
loopback capture. No microphone, no "Stereo Mix", no fuss.

![styles](https://img.shields.io/badge/styles-bars%20%C2%B7%20monstercat%20%C2%B7%20mirror%20%C2%B7%20oscilloscope%20%C2%B7%20LED-blueviolet)
![dotnet](https://img.shields.io/badge/.NET-8%20WPF-5C2D91)
![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)

## Highlights

- 🎨 **Three color themes** — Cappuccino, Dracula and Neon; themes color the frame,
  background and the bars themselves
- ✨ **Five visualization styles**, switchable from the right-click menu:
  - **Bars** — classic cava rounded bars with falling peak caps
  - **Monstercat waves** — a solid smoothed spectrum with a glowing crest line
  - **Mirror** — bars radiating symmetrically from the center line
  - **Oscilloscope** — the actual waveform of the sound in real time
  - **LED segments** — retro equalizer with lit cells and hold-then-fall peak markers
- 🔊 **Zero-config audio capture** — taps the default output device via WASAPI
  loopback (raw COM interop, zero NuGet dependencies); auto-restarts when the
  audio device changes
- 📊 **Real DSP** — 4096-point FFT, logarithmic bands 50 Hz – 10 kHz, automatic
  gain so quiet and loud sources both look great
- 🧲 **Playful physics** — gravity-based bar falloff, peak caps, neighbor-band smoothing
- 🪟 **Borderless draggable window** — move by dragging anywhere, resize from any
  edge, rounded corners, adjustable opacity
- 🖱️ **Click-through mode** — let mouse events pass through the window entirely
- 🖥️ **Fullscreen mode** — stretch across the whole monitor, restore with one
  click (F11 / Esc)
- 🔔 **Tray icon** — left-click to show the window, right-click to exit
- 💾 **Everything persists** — position, size, theme, style and every setting are
  saved automatically to `config.json`
- ⚡ **Lightweight rendering** — pure WPF `DrawingContext` with frozen brushes,
  cached LED grid and tiles; smooth even at 90 FPS

## Requirements

- Windows 10/11 x64
- [.NET SDK 8+](https://dotnet.microsoft.com/download/dotnet/8.0) to build;
  .NET Desktop Runtime 8 to run the non-self-contained build

## Building

Easiest way — double-click `build.bat` (it will offer to run the app right away).

Or manually:

```
dotnet build cava.csproj -c Release
```

Result: `bin\Release\net8.0-windows10.0.19041.0\cava.exe`

### Single-file exe on your desktop

```powershell
powershell -ExecutionPolicy Bypass -File deploy-desktop.ps1
```

Builds a self-contained single-file exe (`dotnet publish -r win-x64
--self-contained` — runs on machines without the runtime), embeds the `cava.ico`
icon and drops it onto your desktop as `cava.exe`.


## Controls

| Action | How |
|---|---|
| Move the window | hold left mouse button and drag anywhere on the background |
| Resize | drag any window edge |
| Settings menu | right-click anywhere on the window |
| Fullscreen | right-click → "Fullscreen" (or F11) |
| Restore size | right-click → "Restore size" (or Esc) |
| Visualization style | right-click → Style → Bars / Monstercat / Mirror / Oscilloscope / LED |
| Color theme | right-click → Theme → Cappuccino / Neon / Dracula |
| Always on top | right-click → check "Topmost" |
| Click-through | right-click → check "Click-through" |
| Exit | right-click → "Exit", or right-click the tray icon |

## Configuration

File: `%APPDATA%\CavaWin\config.json` (created automatically). Available keys:

| Key | Default | Description |
|---|---|---|
| `Bars` | 48 | number of bands (16–96 in the menu; any value in the file) |
| `Framerate` | 60 | FPS cap |
| `Gravity` | 1.0 | bar falloff speed (higher = faster) |
| `Sensitivity` | 1.0 | input gain multiplier |
| `Smoothing` | 1.4 | "bleed" between neighboring bands |
| `Peaks` | true | show peak caps (rendered per-style in monstercat / LED) |
| `Mode` | bars | visualization style: `bars`, `monstercat`, `mirror`, `wave`, `led` |
| `Opacity` | 0.9 | window opacity 0.2–1.0 |
| `Topmost` | true | keep the window above others |
| `ClickThrough` | false | pass mouse clicks through the window |
| `Width`, `Height` | 560×240 | window size |
| `Margin` | 6 | inner margin from window edges |
| `MinFreq` | 50 | lower spectrum bound, Hz |
| `MaxFreq` | 10000 | upper spectrum bound, Hz |
| `ColorFrom`, `ColorTo` | — | custom bar gradient (`#RRGGBB`); applied only when different from the defaults, otherwise the theme decides |
| `Theme` | cappuccino | `cappuccino` (creamy coffee), `dracula` (dark, green-violet bars), `neon` (transparent neon) |
| `WindowLeft`, `WindowTop` | -1 | saved window position (managed automatically) |

## How it works

1. **WASAPI loopback** — attaches to the default output device and reads the very
   stream Windows sends to the speakers. Switching audio devices restarts the
   capture automatically.
2. **DSP** (`src/Dsp.cs`) — ring buffer → Hann window → FFT → logarithmic band
   averaging → auto-gain → gravity and smoothing. Also exposes the raw waveform
   for the oscilloscope style.
3. **Render** (`src/Bars.cs` + `src/Styles.cs`) — WPF `DrawingContext`, frozen
   brushes, cached LED grid; five rendering styles.

## Known limitations

- Only the **default** output device is captured.
- Apps using exclusive-mode output (ASIO) may bypass the capture.
- Won't show over fullscreen exclusive games with composition disabled.

## Project layout

```
audio-visualizer\
├── deploy-desktop.ps1   — publish single-file exe to the desktop (with icon)
├── cava.csproj          — project file (WPF, net8.0-windows)
├── app.manifest         — manifest (DPI PerMonitorV2, Windows 10/11)
├── build.bat            — build + run
├── config.json          — default config (copied to bin)
└── src\
    ├── Program.cs       — entry point, test mode (--test)
    ├── Config.cs        — config load/save
    ├── Dsp.cs           — FFT, bands, gravity, auto-gain, waveform
    ├── AudioCapture.cs  — WASAPI loopback (COM interop, no NuGet)
    ├── Bars.cs          — visual element, bars style + mode dispatch
    ├── Styles.cs        — monstercat / mirror / oscilloscope / LED styles
    ├── MainWindow.cs    — window, context menu, timer
    ├── TrayIcon.cs      — tray icon
    ├── TraySink.cs      — tray message window
    └── Win32.cs         — Set/GetWindowLong, Alt+Tab hiding
```