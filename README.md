# Yacplayer

**Language:** English · [Русский](README.ru.md)

<https://github.com/user-attachments/assets/6b0c0f89-5e3f-4fda-b4a3-3f3359404995>

Yacplayer is a local music player for Windows with a full-screen terminal interface. It plays audio through WASAPI shared mode, provides a queue and a two-panel file browser, renders FFT spectrum or waveform data from actual decoded PCM, and is operated entirely from the keyboard.

> [!IMPORTANT]
> **AI-generated code notice:** This project contains code generated and modified with artificial-intelligence assistance. AI-produced code may contain defects, vulnerabilities, or incorrect assumptions; qualified human review and testing are required before production use. This notice does not alter the MIT license or its warranty disclaimer.

## Current status

Implemented:

- NAudio 3.3 playback through `WasapiPlayer` in shared mode;
- play, pause/resume, stop, ±5-second seek, previous/next, and automatic next track;
- volume from 0 to 100% and mute;
- queue removal without deleting files, repeat Off/All/One, shuffle bag navigation, and automatic viewport scrolling that keeps the selected/current track visible;
- startup with either a file or a directory;
- built-in Midnight Commander-style browser with directories, `[..]`, a `This PC` drive list, and file/folder queue insertion;
- cancellable background directory loading that keeps the TUI responsive, plus hash-based deduplication and batch queue insertion for large folders;
- adaptive ANSI/VT interface with colored panels, white captions on green title backgrounds, the current version in the top-right header, and differential rendering with full-row refresh for variable-width Unicode;
- switchable Hann-windowed FFT spectrum with exact-window accumulation, a smooth true-color green/yellow/red gradient, `.` waveform, and disabled visualizer modes; spectrum and waveform amplitude are independent of output volume and mute;
- optional realtime audio-analysis panel with multi-band onset detection, PLL-tracked BPM/beat confidence and an indicator that flashes in rhythm, plus RMS/peak/crest levels, zero-crossing rate, spectral centroid, and dominant frequency; its PCM tap and calculations stop when disabled;
- a full-screen settings menu whose changes apply and save immediately to per-user JSON, including a configurable startup folder;
- CLI, Release publishing, and an Inno Setup installer;
- reproducible self-contained MSIX packaging for Microsoft Store submission;
- embedded multi-resolution Windows application icon.

Known limitations:

- ATL metadata is loaded lazily only for the current track; entries that have never played retain filename/`Unknown artist` fallbacks, and bitrate is not calculated;
- interactive queue search, mouse input, OGG, and Opus are not implemented;
- actual WASAPI and codec behavior still requires manual verification on a machine with an audio device.
- BPM is a realtime signal estimate rather than embedded metadata. The multi-hypothesis tracker needs several seconds to lock and can still choose a different metrical level for syncopated, changing, or rhythmically ambiguous material.

## Requirements

- Windows 10 build 17763 or newer, or Windows 11;
- x64 architecture;
- [.NET 10 Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0/runtime) for the framework-dependent build;
- Windows Terminal is recommended for VT, Unicode, and ANSI color support;
- the .NET 10 SDK is needed only when building from source.

## Quick start

### Installer

Download latest installer from releases page and install as usual app.
The installer performs a per-user installation into `%LOCALAPPDATA%\Programs\Yacplayer`, does not require administrator privileges, and can optionally create a desktop shortcut.

### Published application

```powershell
.\artifacts\publish\Yacplayer.exe
.\artifacts\publish\Yacplayer.exe "C:\Music\track.mp3"
.\artifacts\publish\Yacplayer.exe "C:\Music" --recursive
```

With no arguments, the file browser opens immediately in the configured startup folder, the last available browser directory, or the process working directory, in that order.

### Run from source

```powershell
dotnet restore Yacplayer.sln
dotnet run --project src/Yacplayer --
dotnet run --project src/Yacplayer -- "C:\Music"
dotnet run --project src/Yacplayer -- "C:\Music\track.mp3" --fps 60
```

## CLI

```text
yacplayer [path] [--recursive] [--fps 30|60] [--no-visualizer]
```

| Argument | Purpose |
|---|---|
| `path` | One audio file or directory |
| `-r`, `--recursive` | Scan directories recursively; directory reparse points are skipped |
| `--fps 30\|60` | UI frame rate; other values are rejected |
| `--no-visualizer` | Disable the spectrum for the current invocation |
| `-h`, `--help` | Print help without initializing audio or the TUI |
| `--version` | Print the version without initializing audio or the TUI |

Exit codes:

- `0` — normal exit;
- `2` — unknown or malformed CLI argument;
- `3` — missing startup path or critical startup error.

When stdin or stdout is redirected, the interactive TUI is not started. The process reports the number of loaded tracks and exits.

## Player controls

| Key | Action |
|---|---|
| `Space` | Pause/resume |
| `X` | Stop and reset position |
| `N` / `P` | Next / previous track |
| `Left` / `Right` | Seek −5 / +5 seconds |
| `+` / `-` | Increase/decrease volume by five percentage points |
| `M` | Mute/unmute |
| `Up` / `Down` | Select a queue entry |
| `Enter` | Play the selected queue entry |
| `Delete` | Remove from the queue only; the file is never deleted |
| `S` | Toggle shuffle |
| `R` | Repeat Off → All → One |
| `V` | Spectrum → Waveform → Off; the panel title follows the active mode |
| `B` | Toggle the realtime audio-analysis panel and its calculations |
| `T` | Cycle the music-reactive UI theme Off → Subtle → Visualizer |
| `F` | Open the file browser |
| `O` | Open the full-screen settings menu |
| `?` | Open the in-application help |
| `Q` | Open the `Yep` / `Nope` exit-confirmation dialog |

## File browser controls

The left panel contains directories and supported audio files; the right panel shows the current queue. Directories are sorted first, then names are sorted case-insensitively.

| Key | Action |
|---|---|
| `Up` / `Down` | Move the selection |
| `Enter` | Enter a directory; for a file, add it if necessary, play it, and close the browser |
| `Backspace` | Open the parent directory |
| `Enter` on `[..]` | Open the parent directory |
| `A` | Append the selected file or directory contents without starting playback |
| `O` | Open the full-screen settings menu |
| `F` / `Esc` | Close the browser |
| `Q` | Open the `Yep` / `Nope` exit-confirmation dialog |

Moving above a drive root opens the virtual `This PC` list containing all ready drives. Inaccessible directories do not terminate the application and currently appear empty.

## Formats

| Format | Current status |
|---|---|
| WAV PCM | Supported baseline format |
| MP3 | Supported on standard Windows through NAudio/Media Foundation |
| M4A, AAC | Depends on an available Media Foundation codec |
| WMA | Depends on the system decoder |
| FLAC | Best effort through Windows system capabilities |
| OGG, Opus | Not supported by the current version |

The extension allowlist controls visibility in the browser, not guaranteed decoding. Actual playback capability is determined by the decoder. Codec errors are nonfatal. Yacplayer does not invoke FFmpeg or other external executables.

## Configuration

Settings file: `%LOCALAPPDATA%\Yacplayer\settings.json`.

```json
{
  "Version": 1,
  "Volume": 75,
  "Muted": false,
  "Fps": 30,
  "FftSize": 2048,
  "VisualizerMode": "Spectrum",
  "RealtimeAnalysisEnabled": true,
  "ReactiveUiMode": "Off",
  "RepeatMode": "Off",
  "Shuffle": false,
  "Recursive": false,
  "LastDirectory": "",
  "StartupDirectory": ""
}
```

Press `O` to edit every persisted option in a full-screen menu. Use Up/Down to select, Left/Right (or `+`/`-`) to change a value, and Enter to edit folder paths. Valid changes affect the running player and are written immediately. `StartupDirectory` is the fixed folder used when launching without a path; leave it empty to fall back to `LastDirectory`. Changing `LastDirectory` also opens that folder in the active browser.

The optional music-reactive UI (`T` or the `Reactive UI` settings entry) slowly shifts panel border/title/accent colors, the spectrum bar gradient, the progress bar, and the waveform dots from live audio features: bass energy drives brightness pulses, spectral centroid steers a bounded yellow-green-to-cyan hue range, and overall RMS modulates saturation. When the theme is on, the current track is also scanned offline into a one-second loudness profile, and a smoothed drama factor — how much louder the present is than the preceding ten seconds and how soon a louder ten-second section approaches — scales the overall reaction amplitude, so quiet intros and bridges stay calm while choruses and drops push the interface to full intensity well before they hit. `Off` (the default) keeps the static theme and performs no extra work; `Subtle` is the gentle default mode and `Visualizer` reacts more strongly. Colors glide back to the static theme while paused or stopped.

Validation covers the volume range, FPS (`30`/`60`), FFT size (`2048`/`4096`), enum values, and existence of both directory paths. Malformed JSON is moved to `settings.json.bad`, then defaults are used. Saving writes `settings.json.tmp` before replacing the target. Explicit CLI options override settings for the current run.

## Build and test

```powershell
dotnet restore Yacplayer.sln
dotnet build Yacplayer.sln -c Release
dotnet test Yacplayer.sln -c Release
```

GitHub Actions runs the same restore, Release build, and test sequence on Windows for every push and pull request.

The current suite contains 40 device-independent unit tests covering CLI parsing, four-part assembly versioning, playlist behavior and restored repeat/shuffle state including single-track Repeat All, batch insertion during an active shuffle cycle, bounded audio-event delivery, list viewport scrolling/clamping, ring-buffer wrapping/latest/exact reads, independently gated pre-volume PCM tapping, realtime level/spectrum/tempo analysis including partial FFT accumulation, player/browser/settings layout rendering, settings interaction and persistence, browser-controller deduplication, ANSI/true-color diff styles, batching and Unicode-row refresh, FFT reconfiguration, metadata fallback, and cancellable browser filtering/sorting.

Framework-dependent win-x64 publish:

```powershell
dotnet publish src/Yacplayer/Yacplayer.csproj `
  -c Release -r win-x64 --self-contained false `
  -o artifacts/publish
```

Native AOT win-x64 publish (self-contained, no .NET runtime installation required):

```powershell
dotnet publish src/Yacplayer/Yacplayer.csproj `
  -p:PublishProfile=NativeAotWinX64
```

Output: `artifacts/publish-aot/Yacplayer.exe`. Native AOT requires the .NET native toolchain for Windows (Visual Studio Build Tools with the Desktop development with C++ workload). The AOT profile is Windows-only because audio output still uses WASAPI.

Microsoft Store MSIX packaging:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File packaging\msix\Build-Msix.ps1
```

See [Microsoft Store release](docs/MICROSOFT_STORE.md) for Partner Center identity setup, certification, and secret handling.

Privacy: [Yacplayer Privacy Policy](PRIVACY.md). The application is local-only and does not collect or transmit personal data.

Build the Inno Setup 6 installer:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Yacplayer.iss
```

Output: `artifacts\installer\Yacplayer-1.26.5.11-win-x64-setup.exe`.

## Repository layout

```text
src/Yacplayer/                  application
  Audio/                       NAudio, WASAPI, PCM tap, ring buffer
  Application/                 browser, metadata, and visualization coordinators
  Cli/                         argument parsing and help
  Configuration/               settings model and JSON persistence
  Dsp/                         FFT and logarithmic bands
  Library/                     queue, browser, metadata model/service
  Presentation/                player/browser terminal layouts
  Terminal/                    alternate screen, cell buffer, ANSI diff
tests/Yacplayer.Tests/          xUnit tests for device-independent logic
assets/                         PNG and multi-resolution ICO icon
installer/Yacplayer.iss         Inno Setup 6 definition
docs/ARCHITECTURE.md            English architecture guide
docs/ARCHITECTURE.ru.md         Russian architecture guide
artifacts/publish/              framework-dependent publish
artifacts/installer/            compiled installer
```

Complete engineering documentation: [English](docs/ARCHITECTURE.md) · [Русский](docs/ARCHITECTURE.ru.md).

## FAQ

- **Why is Yacplayer available only for Windows?** The application currently targets `net10.0-windows` and sends audio through NAudio and WASAPI. Linux and macOS require separate, properly tested audio backends, filesystem/configuration integration, codec validation, and terminal behavior checks. Cross-compiling the current Windows host would not produce a working or supported port.
- **Why is the screen blank or poorly colored?** Use Windows Terminal, UTF-8, and a font containing `█`.
- **Why is there no audio?** Verify the default output device; Yacplayer requests WASAPI shared mode with 100 ms latency.
- **Why does a visible file not play?** Appearing in the browser does not guarantee an installed system codec.
- **Why does Explorer show an old icon?** Windows may cache executable icons; rename the file or restart Explorer.
- **Why do settings not load?** Inspect `settings.json.bad` next to the configuration file.
- **Why does the terminal remain in the alternate screen?** Confirming `Yep` after `Q`, or using Ctrl+C, restores it; after a hard process termination, reset the terminal or open a new tab.

Use `dotnet-counters` and `dotnet-trace` for profiling. Neither is a runtime dependency.

## Licenses

Project-owned code is released under the canonical English [MIT License](LICENSE); an [unofficial Russian translation](LICENSE.ru.md) is also available. Third-party notices: [English](THIRD_PARTY_NOTICES.md) · [Русский](THIRD_PARTY_NOTICES.ru.md).
