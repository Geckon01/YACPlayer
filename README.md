# Yacplayer

**Language:** English · [Русский](README.ru.md)

![Yacplayer icon](assets/Yacplayer_icon.png)

Yacplayer is a local music player for Windows with a full-screen terminal interface. It plays audio through WASAPI shared mode, provides a queue and a two-panel file browser, renders an FFT spectrum from actual decoded PCM, and is operated entirely from the keyboard.

> [!IMPORTANT]
> **AI-generated code notice:** This project contains code generated and modified with artificial-intelligence assistance. AI-produced code may contain defects, vulnerabilities, or incorrect assumptions; qualified human review and testing are required before production use. This notice does not alter the MIT license or its warranty disclaimer.

## Current status

Implemented:

- NAudio 3.3 playback through `WasapiPlayer` in shared mode;
- play, pause/resume, stop, ±5-second seek, previous/next, and automatic next track;
- volume from 0 to 100% and mute;
- queue removal without deleting files, repeat Off/All/One, and shuffle bag navigation;
- startup with either a file or a directory;
- built-in Midnight Commander-style browser with directories, `[..]`, a `This PC` drive list, and file/folder queue insertion;
- adaptive ANSI/VT interface with colored panels and differential rendering;
- Hann-windowed FFT visualizer with logarithmic bands, smoothing, and height-based colors;
- per-user JSON settings;
- CLI, Release publishing, and an Inno Setup installer;
- embedded multi-resolution Windows application icon.

Known limitations:

- a separate `Waveform` renderer is not implemented: `V` changes the enum, but only `Spectrum` is drawn; `Off` disables the spectrum;
- `H` toggles and persists the fullscreen-visualizer flag, but no dedicated fullscreen layout is currently rendered;
- the ATL-backed `MetadataService` exists and is unit-tested but is not wired into `App`; the UI normally uses the filename, `Unknown artist`, and does not calculate bitrate;
- repeat and shuffle are saved to JSON but are not restored into the new `Playlist` instance during startup;
- interactive queue search, mouse input, OGG, and Opus are not implemented;
- actual WASAPI and codec behavior still requires manual verification on a machine with an audio device.

## Requirements

- Windows 10 build 17763 or newer, or Windows 11;
- x64 architecture;
- [.NET 10 Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0/runtime) for the framework-dependent build;
- Windows Terminal is recommended for VT, Unicode, and ANSI color support;
- the .NET 10 SDK is needed only when building from source.

## Quick start

### Installer

Run:

```text
artifacts\installer\Yacplayer-1.0.0-win-x64-setup.exe
```

The installer performs a per-user installation into `%LOCALAPPDATA%\Programs\Yacplayer`, does not require administrator privileges, and can optionally create a desktop shortcut.

### Published application

```powershell
.\artifacts\publish\Yacplayer.exe
.\artifacts\publish\Yacplayer.exe "C:\Music\track.mp3"
.\artifacts\publish\Yacplayer.exe "C:\Music" --recursive
```

With no arguments, the file browser opens immediately in the last available directory or the process working directory.

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
| `V` | Spectrum → Waveform → Off; Waveform is not drawn yet |
| `H` | Toggle the persisted fullscreen flag; no dedicated layout yet |
| `F` | Open the file browser |
| `?` | Open the in-application help |
| `Q` | Shut down cleanly |

## File browser controls

The left panel contains directories and supported audio files; the right panel shows the current queue. Directories are sorted first, then names are sorted case-insensitively.

| Key | Action |
|---|---|
| `Up` / `Down` | Move the selection |
| `Enter` | Enter a directory; for a file, add it if necessary, play it, and close the browser |
| `Backspace` | Open the parent directory |
| `Enter` on `[..]` | Open the parent directory |
| `A` | Append the selected file or directory contents without starting playback |
| `F` / `Esc` | Close the browser |
| `Q` | Exit the application |

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
  "FullscreenVisualizer": false,
  "RepeatMode": "Off",
  "Shuffle": false,
  "Recursive": false,
  "LastDirectory": ""
}
```

Validation covers the volume range, FPS (`30`/`60`), FFT size (`2048`/`4096`), enum values, and existence of the last directory. Malformed JSON is moved to `settings.json.bad`, then defaults are used. Saving writes `settings.json.tmp` before replacing the target. Explicit CLI options override settings for the current run.

## Build and test

```powershell
dotnet restore Yacplayer.sln
dotnet build Yacplayer.sln -c Release
dotnet test Yacplayer.sln -c Release
```

The current suite contains 11 device-independent unit tests covering CLI parsing, playlist behavior, shuffle, ring-buffer wrapping, ANSI diff/styles/batching, settings, FFT, metadata fallback, and browser filtering/sorting.

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

Build the Inno Setup 6 installer:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Yacplayer.iss
```

Output: `artifacts\installer\Yacplayer-1.0.0-win-x64-setup.exe`.

## Repository layout

```text
src/Yacplayer/                  application
  Audio/                       NAudio, WASAPI, PCM tap, ring buffer
  Cli/                         argument parsing and help
  Configuration/               settings model and JSON persistence
  Dsp/                         FFT and logarithmic bands
  Library/                     queue, browser, metadata model/service
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

## Troubleshooting

- **Blank screen or poor colors:** use Windows Terminal, UTF-8, and a font containing `█`.
- **No audio:** verify the default output device; Yacplayer requests WASAPI shared mode with 100 ms latency.
- **A visible file does not play:** appearing in the browser does not guarantee an installed system codec.
- **Explorer shows an old icon:** Windows may cache executable icons; rename the file or restart Explorer.
- **Settings do not load:** inspect `settings.json.bad` next to the configuration file.
- **The terminal remains in alternate screen:** normal `Q` and Ctrl+C paths restore it; after a hard process termination, reset the terminal or open a new tab.

Use `dotnet-counters` and `dotnet-trace` for profiling. Neither is a runtime dependency.

## Licenses

Project-owned code is released under the canonical English [MIT License](LICENSE); an [unofficial Russian translation](LICENSE.ru.md) is also available. Third-party notices: [English](THIRD_PARTY_NOTICES.md) · [Русский](THIRD_PARTY_NOTICES.ru.md).
