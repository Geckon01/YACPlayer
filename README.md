# Yacplayer

Yacplayer is a keyboard-first Windows terminal music player. It uses real Windows WASAPI shared-mode playback, local metadata, a bounded PCM tap, Hann-windowed FFT visualization, and a custom ANSI/VT screen-diff renderer. It has no GUI, webview, database, telemetry, or network feature.

```text
 Yacplayer  Playing  Vol 75%  Repeat Off  Shuffle off
 ────────────────────────────────────────────────────
 Unknown artist — track
 01:14 [████████████████░░░░░░░░░░░░░░░] 03:42
       █   █ ███   █
   █   █ █ █ ███ █ █
 >▶    1  track
```

## Requirements and use

Windows 10/11 x64 and the .NET 10 SDK are required to build. From the repository root:

```powershell
dotnet restore Yacplayer.sln
dotnet build Yacplayer.sln -c Release
dotnet test Yacplayer.sln -c Release
dotnet run --project src/Yacplayer -- "C:\Music"
dotnet run --project src/Yacplayer -- "C:\Music\track.mp3" --fps 60
```

Options are `--recursive`, `--fps 30|60`, `--no-visualizer`, `--help`, and `--version`. An unknown/malformed option exits with code 2; an absent startup path exits with code 3. Help and version never initialize audio or enter the alternate screen.

## Keys

| Key | Action |
|---|---|
| Space / X | pause-resume / stop |
| N / P | next / previous |
| Left / Right | seek -5/+5 seconds |
| + / - / M | volume +5/-5 / mute |
| Up / Down / Enter | select / play queue item |
| Delete | remove queue item; never deletes the file |
| S / R | shuffle / repeat Off-All-One |
| V / H | Spectrum-Waveform-Off / fullscreen mode |
| F / Esc | open / close the file browser |
| Enter / Backspace | open file or directory / parent directory |
| A | append the selected file or folder to the queue |
| ? / Esc / Q | help / close overlay / quit |

With no path argument, Yacplayer opens a two-panel, Midnight Commander-style browser in the last valid directory or the current directory. The left panel contains directories and supported audio files; the right panel shows the queue. The always-visible `[..]` entry and Backspace both go to the parent. Going above a drive root opens the `This PC` drive list. Enter opens a directory or adds and plays a file; A appends the selected file or folder without starting playback. Recursive folder addition skips directory reparse points.

## Formats and audio

PCM WAV and standard Windows MP3 are the supported baseline. M4A, AAC, WMA, and FLAC are offered through NAudio's Windows Media Foundation reader and therefore depend on installed OS codecs. OGG and Opus are deliberately not advertised as playable. ATL reads metadata only; it is not a decoder. Decode errors are nonfatal and shown in the status line.

The audio callback reads decoded floating-point PCM, downmixes it to mono, and writes into a bounded overwrite-oldest SPSC-style ring. A UI-side analyzer applies a Hann window, reuses FFT buffers, maps bins into logarithmic bands, and drops incomplete windows. Rendering and metadata never execute in the audio callback. Audio continuity takes priority over visualization freshness.

## Configuration

Settings are valid JSON at `%LOCALAPPDATA%\Yacplayer\settings.json`. Volume, mute, FPS, FFT size, visual mode, repeat/shuffle, recursion and last directory persist. CLI flags override settings for the current invocation. Values are validated; malformed JSON is moved to `settings.json.bad`; writes use a temporary file and atomic replace/move.

## Troubleshooting and limitations

- No audio device: `--help` and `--version` still work; playback reports the WASAPI error and the TUI remains usable.
- Garbled display: use Windows Terminal or enable Windows VT processing and a Unicode-capable font. `Q`/Ctrl+C restores the alternate screen and cursor.
- Codec failure: verify the file in a Media Foundation-capable Windows player. No FFmpeg fallback is used.
- This release is Windows-only, keyboard-only, and does not support OGG/Opus or mouse input. Interactive queue search is not yet implemented.
- Audio-device behavior must be manually checked with a real WAV/MP3: play, pause, seek, volume/mute, rapid next/previous, repeat modes, resize, then Q/Ctrl+C.

For profiling, run `dotnet-counters monitor --process-id <pid>` or capture `dotnet-trace`; neither tool is a runtime dependency.

## Architecture

`Audio` owns decode/output and PCM tapping; `Dsp` owns FFT; `Library` owns scanning, metadata, and deterministic playlist state; `Terminal` owns sanitized buffers/diffs; `Configuration` owns validated atomic JSON; `App` is the single nonblocking UI loop. Tests synthesize inputs and require no sound device.
