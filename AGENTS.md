# Yacplayer Agent Guide and Living Project State

**Primary language:** English · [Русская версия](AGENTS.ru.md) · [README](README.md) · [Architecture](docs/ARCHITECTURE.md) · [Known issues](docs/KNOWN_ISSUES.md)

This file is the operational entry point for every coding agent that works in this repository. It records the implemented system, the current verified state, the work that remains, and the rules that must remain true while the project evolves.

## Mandatory read-and-update contract

Every agent MUST follow this protocol, including on its first encounter with the repository:

1. Read this entire file before planning, editing, running destructive commands, or making a commit.
2. Inspect the relevant source and tests instead of treating this document as proof that the code still behaves as described.
3. Preserve user changes and unrelated untracked files. Never use broad cleanup, reset, checkout, or staging commands without first inspecting their exact effect.
4. Update this file in the same change whenever work alters behavior, architecture, dependencies, commands, controls, configuration, persistence, supported formats/platforms, tests, packaging, release state, known defects, priorities, or repository policy.
5. Keep [AGENTS.ru.md](AGENTS.ru.md) semantically synchronized with this English source. English is authoritative when wording differs.
6. If an investigation discovers that any statement here is stale, correct it even when the original task was elsewhere. A read-only task only needs an update when it discovers materially new project state.
7. Update the verification record near the end of this file before handing work off. Record commands actually run; never claim unperformed tests or manual checks.
8. Before finishing, review `git diff`, `git status --short`, and this file. A feature is not complete if the living project state is stale.

Do not remove or weaken this contract. Its purpose is to let a new agent understand the project and continue safely without relying on prior conversation history.

## Project snapshot

- Product: Yacplayer 1.26.5.11, a keyboard-operated full-screen terminal music player.
- Current supported runtime: Windows x64 only.
- Framework: .NET 10, `net10.0-windows`, nullable reference types enabled.
- Audio: NAudio 3.3.0, `AudioFileReader`, and WASAPI shared mode with requested 100 ms latency.
- UI: custom ANSI/VT alternate-screen renderer; there is no GUI framework.
- Visualizers: colored FFT spectrum, real-time PCM waveform, and Off; visualization PCM is tapped before output volume/mute.
- Optional real-time analysis: a separately gated pre-volume PCM tap feeds UI-thread RMS/peak/crest, zero-crossing, spectral centroid/dominant-frequency, multi-band spectral-flux onset detection, and multi-hypothesis PLL BPM/phase tracking. `B` disables both the panel and this analysis path.
- Metadata: ATL 7.19.0, loaded lazily for the current track only.
- Persistence: per-user JSON at `%LOCALAPPDATA%\Yacplayer\settings.json`.
- Distribution: framework-dependent win-x64 publish, optional win-x64 Native AOT publish, an Inno Setup 6 installer, and a self-contained x64 MSIX build for Microsoft Store.
- Automated tests: 34 xUnit tests that avoid real audio hardware.
- License: MIT. The repository and user-facing documentation carry an AI-assisted/generated-code notice.
- Public documentation is English-first with Russian counterparts.

## What is implemented

### Startup and CLI

- `Program.cs` configures UTF-8, parses arguments, handles Ctrl+C through cancellation, and maps malformed arguments to exit code 2 and startup failures to exit code 3.
- Syntax: `yacplayer [path] [--recursive] [--fps 30|60] [--no-visualizer]`.
- `--help` and `--version` do not initialize audio or the alternate terminal screen.
- A supplied file starts playback. A supplied directory is scanned into the queue. With no path, the built-in browser opens in the saved last directory or the process working directory.
- Redirected input/output skips the interactive TUI, reports the number of loaded tracks, and exits.

### Playback and queue

- Play, pause/resume, stop/reset, seek ±5 seconds, previous/next, and natural-EOF advancement are implemented.
- Volume is clamped to 0–100%; mute preserves the logical volume.
- Repeat modes are Off, All, and One. Repeat One applies to automatic EOF, while manual Next still advances.
- Shuffle uses a bag to avoid repeats within one cycle and a history stack for Previous.
- With one current track, Shuffle + Repeat All starts the next cycle by replaying that track instead of attempting to pop an empty bag.
- Batch insertion appends only new indices to an active shuffle bag instead of rebuilding the cycle.
- Saved repeat and shuffle values are restored into the playlist during startup.
- Playback completion and errors cross from NAudio into the UI loop through a bounded, generation-tagged event queue; stale events are ignored.
- Removing a queue entry never deletes a filesystem object.
- Queue duplicates are permitted by `Playlist`, but browser add/play operations deduplicate paths case-insensitively.
- Independent `ListViewport` instances keep the selected/current item visible in the main queue, browser file list, and browser-side queue without reordering their collections.

### File browser

- The browser is Midnight Commander-inspired: files on the left and queue on the right when the terminal is wide enough.
- `[..]` and Backspace navigate to the parent. Going above a drive root shows a virtual `This PC` list of ready drives.
- Directories appear before files; names are sorted case-insensitively.
- Visible extensions: `.wav`, `.mp3`, `.m4a`, `.aac`, `.wma`, and `.flac`.
- `Enter` enters a directory or adds/plays a file. `A` appends a selected file or a directory scan without starting playback.
- Directory enumeration is cancellable and completes through a generation-checked UI queue, so opening a large folder does not stop rendering. Browser additions use a persistent case-insensitive path set and one playlist batch insertion.
- Recursive scans skip reparse points and inaccessible branches.

### Terminal UI

- `TerminalSession` enters the alternate screen, hides the cursor, and restores terminal state on normal disposal.
- Every frame is first composed into a styled `ScreenBuffer`; `Diff` emits only changed runs with ANSI cursor/style sequences, except rows with likely variable-width Unicode, which are erased and fully repainted.
- The wide layout contains header, Now Playing, Queue, visualizer, Playback, and Audio Info panels. Compact terminals receive a reduced layout.
- Green panel-title backgrounds use white bold text while their borders remain bright green.
- The top-right header shows the running assembly version as `vA.B.C.D` and hides it only when the terminal is too narrow.
- The progress bar uses solid green `█` cells and a `·` remainder.
- Spectrum columns use `█`, with a smooth ANSI true-color green/yellow/red gradient based on height and a yellow top marker.
- `V` cycles Spectrum → Waveform → Off. Switching mode resets visualization backlog, and FFT is not calculated in Waveform or Off modes.
- The waveform draws a normalized min/max envelope with green `.` characters. The dot glyph is the current intentional design.
- `B` toggles the persisted Realtime Audio panel in sufficiently wide layouts. When off, the secondary tap stops writing and the level/spectral/tempo analyzer is not invoked.

### Metadata and settings

- Queue entries initially use filename title, `Unknown artist`, empty album, and zero duration.
- Only the currently playing entry is loaded through ATL. Cancellation follows track changes; the worker posts a completion to the UI loop rather than mutating the entry directly.
- Settings validation clamps volume, accepts only 30/60 FPS and 2048/4096 FFT sizes, validates enums, and drops a missing last-directory path.
- Malformed JSON is moved to `settings.json.bad` when possible. Saves use `settings.json.tmp` followed by replacement.
- Volume, mute, visualizer mode, real-time analysis, repeat, shuffle, recursive mode, FFT size, FPS, and last directory are represented in settings.

### Packaging and branding

- `assets/Yacplayer.ico` is embedded through `ApplicationIcon`; the PNG source is also retained.
- Both README files embed the silent product demo through a GitHub user-attachment URL; `assets/Yacplayer_demo.mp4` retains the source video and `assets/Yacplayer_screenshot.png` remains available as the static product screenshot.
- `artifacts/publish/` is the tracked framework-dependent distribution consumed by the installer.
- `src/Yacplayer/Properties/PublishProfiles/NativeAotWinX64.pubxml` builds a self-contained, trimmed Native AOT executable into `artifacts/publish-aot/`.
- `installer/Yacplayer.iss` performs a per-user x64 installation into `%LOCALAPPDATA%\Programs\Yacplayer`, provides English/Russian wizard languages, checks for .NET 10, and can create a desktop shortcut.
- `packaging/msix/Build-Msix.ps1` publishes self-contained win-x64 output, generates Store assets and a full-trust manifest, and creates an unsigned MSIX. Partner Center identity overrides and certificate material remain local/ignored; accepted Store packages are signed by Microsoft.

## Architecture and data flow

```text
Program / CLI
    -> App async UI loop (composition, input routing, playlist orchestration)
        -> BrowserController -> FileBrowser worker -> UI-owned browser state
        -> CurrentTrackMetadataLoader -> MetadataService -> UI-owned TrackEntry update
        -> VisualizationPipeline
        -> AudioEngine
            -> AudioFileReader
            -> PcmTapSampleProvider
                -> visualization SampleRingBuffer -> SpectrumAnalyzer or waveform reader
                -> gated analysis SampleRingBuffer -> RealtimeAudioAnalyzer
            -> VolumeSampleProvider -> WasapiPlayer -> Windows default output device
            -> bounded AudioEventQueue -> UI-owned EOF/error handling
        -> AppRenderer -> ScreenBuffer -> ANSI diff -> TerminalSession
        <-> SettingsStore -> settings.json
```

Important execution contexts:

- Main async loop: keyboard input, browser/playlist state, DSP reads, rendering, and settings orchestration.
- NAudio playback thread: decoded sample reads, channel downmix, and ring writes.
- NAudio event context: `PlaybackStopped` publishes a bounded, nonblocking event tagged with the active generation.
- Thread pool: directory work and ATL metadata loading.

Ownership and safety invariants:

- There must be at most one active decoder/output pair.
- Audio callback code must not perform FFT, terminal work, metadata parsing, JSON/file persistence, logging, blocking waits, or unbounded allocation.
- Visualization and analysis transports must remain bounded and must never block playback.
- Disabling real-time analysis must stop its callback-side ring writes and all UI-thread analysis, not merely hide the panel.
- Playlist/browser state is owned by the UI loop; audio callbacks may publish events but must not transition playlist or audio state.
- Queue deletion must never become disk deletion without an explicit separately reviewed feature.
- User-controlled text must pass through `ScreenBuffer.Write`; never inject raw escape sequences into UI content.
- Help/version paths must remain free of audio and alternate-screen initialization.
- Recursive scanning must not follow reparse points by default.
- Runtime behavior must remain local-only unless a network feature is deliberately designed and documented.

## Repository map

| Path | Responsibility |
|---|---|
| `src/Yacplayer/Program.cs` | Process entry, CLI result handling, cancellation, top-level errors |
| `src/Yacplayer/App.cs` | Composition root, main loop, input routing, and shutdown/settings orchestration |
| `src/Yacplayer/Application/` | Browser controller, current-track metadata lifecycle, and visualization pipeline |
| `src/Yacplayer/Presentation/` | Player/browser layouts and terminal rendering |
| `src/Yacplayer/Cli/` | Argument parser and built-in help |
| `src/Yacplayer/Configuration/` | Settings model, source-generated JSON context, atomic-ish storage |
| `src/Yacplayer/Library/` | Playlist, queue model, browser/scan logic, lazy metadata service |
| `src/Yacplayer/Audio/` | NAudio/WASAPI lifecycle, PCM tap, snapshot, bounded sample ring |
| `src/Yacplayer/Dsp/` | Spectrum FFT/bands and real-time level, spectral, beat, and tempo analysis |
| `src/Yacplayer/Terminal/` | Alternate-screen lifetime and styled differential rendering |
| `tests/Yacplayer.Tests/` | Device-independent xUnit tests |
| `.github/workflows/tests.yml` | Windows Release build and test workflow for pushes and pull requests |
| `assets/` | PNG source and multi-resolution Windows ICO |
| `installer/` | Inno Setup source |
| `packaging/msix/` | Microsoft Store MSIX manifest, build script, and non-secret identity example |
| `artifacts/` | Intentionally tracked release/publish binaries; update deliberately |
| `docs/ARCHITECTURE*.md` | Detailed English/Russian architecture documentation |
| `docs/KNOWN_ISSUES.md` | Bilingual defects, performance analysis, and portability roadmap |

## Build, test, and run

Use PowerShell examples from the repository root.

```powershell
dotnet restore Yacplayer.sln
dotnet build Yacplayer.sln -c Release
dotnet test Yacplayer.sln -c Release
dotnet run --project src/Yacplayer --
dotnet run --project src/Yacplayer -- "C:\Music\track.mp3"
```

GitHub Actions mirrors the restore, Release build, and test gate on `windows-latest` for pushes, pull requests, and manual dispatches. The workflow uses read-only repository permissions and does not require secrets.

Framework-dependent publish:

```powershell
dotnet publish src/Yacplayer/Yacplayer.csproj `
  -c Release -r win-x64 --self-contained false `
  -o artifacts/publish
```

Native AOT publish:

```powershell
dotnet publish src/Yacplayer/Yacplayer.csproj `
  -p:PublishProfile=NativeAotWinX64
```

Native AOT requires Visual Studio Build Tools with the Desktop development with C++ workload.

Microsoft Store MSIX (local unsigned identity by default):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File packaging\msix\Build-Msix.ps1
```

Use the ignored `StoreIdentity.local.psd1` populated from Partner Center for a submission build; see `docs/MICROSOFT_STORE.md`. Never add certificates, private keys, passwords, or Partner Center API credentials to the repository.

Build the installer only after refreshing `artifacts/publish/`:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Yacplayer.iss
```

Do not regenerate or commit binary artifacts unless the task explicitly requires release output. Check binary diffs and package inputs separately from source changes.

## Verification expectations

For ordinary source changes, the minimum automated gate is:

```powershell
dotnet build Yacplayer.sln -c Release
dotnet test Yacplayer.sln -c Release
```

Add focused tests for changed device-independent behavior. The current suite covers CLI rejection, playlist repeat/removal/shuffle including the single-track Repeat All boundary, ring wrapping/latest reads, gated pre-volume PCM tapping, ANSI diff/color/batching, settings round trip and malformed JSON, synthetic spectrum/tempo analysis, extracted player/browser layouts, browser-controller deduplication, metadata fallback, and browser sorting/filtering.

Automated tests do not verify real WASAPI behavior. Audio, terminal, or packaging work should also record an appropriate manual smoke check when the environment permits:

- start with no arguments and navigate parent/root drives;
- play a known legal/local WAV or generated test fixture;
- pause, seek, change volume/mute, stop, next/previous, and reach natural EOF;
- cycle all visualizer modes and resize the terminal;
- exit with `Q` and Ctrl+C and confirm terminal restoration;
- for installer work, test install, launch, upgrade/uninstall, shortcuts, and a clean user profile.

Never add copyrighted music as a fixture. Generate a small WAV during the test or keep manually supplied media outside the repository.

## Known defects and next work

The detailed backlog is [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md). Keep this summary synchronized with it.

### P1: correctness and performance

1. Replace the ring protocol with a proven SPSC design. Producer overflow and consumer reads currently both write `_read`.
2. Stop consuming and discarding partial FFT windows; retain the last valid spectrum and use time-based decay.
3. Reuse screen buffers and ANSI output storage instead of allocating a cell array and diff string every frame.
4. Batch multichannel downmix/ring writes; the callback currently invokes `ring.Write` per mono sample.
5. Make recursive scanning incremental, cancellable, and batched; startup and `A` scans still materialize their full result.
6. Establish CPU, allocation, memory-growth, callback-duration, terminal-throughput, and soak baselines.

### P2/P3: behavior and maintainability

- Add a focused waveform rendering test that preserves the intentional `.` glyph and min/max envelope behavior.
- Remove `Tab` focus and `/` search from CLI help until implemented, or implement the missing controls. `App.HandleAsync` currently handles neither.
- Distinguish an inaccessible browser directory from an empty one.
- Handle terminal grapheme/display width instead of assuming one UTF-16 `char` per column.
- Avoid spectrum work while playback is inactive and cache frequency-band/bin mappings.
- Replace delay-after-work scheduling with monotonic frame deadlines and a lower idle rate.
- Add input/layout snapshots, concurrent ring stress tests, generated-WAV integration tests, optional WASAPI smoke tests, installer tests, and long-running soak coverage.
- Calculate/display bitrate if it becomes a supported metadata field; preserve the current lazy-current-track policy unless requirements change.
- Treat realtime BPM as a heuristic estimate: the spectral-flux/PLL tracker needs several seconds and may select a different metrical level; validate it on a representative real-music corpus and measure CPU cost before using it for synchronization.
- Reformat dense one-line code in the remaining domain classes and tests when doing so can remain a reviewable style-only change.

## Platform status and portability

Windows is the only supported OS. Cross-publishing the current project does not produce a functional Linux or macOS player because the target and audio backend are Windows-specific.

Porting direction:

1. Separate portable domain/DSP/terminal code from a `net10.0-windows` host.
2. Introduce `IAudioEngine` and a platform-neutral PCM tap contract.
3. Keep NAudio/WASAPI in a Windows backend; implement and test PipeWire/PulseAudio/ALSA and CoreAudio backends separately.
4. Introduce a platform filesystem-root/config-location abstraction.
5. Validate codecs, input, ANSI behavior, Unicode width, resize, EOF, and device failures on each real OS.
6. Add OS-specific CI/runtime smoke tests and packages only after real runtime support exists.

Do not claim Linux/macOS support based on compilation alone.

## Documentation and language policy

- English is the primary and authoritative documentation language.
- Keep Russian equivalents current: `README.ru.md`, `docs/ARCHITECTURE.ru.md`, the Russian half of `docs/KNOWN_ISSUES.md`, and `AGENTS.ru.md`.
- User-visible behavior changes require README and keymap/config updates in both languages.
- Architecture, data-flow, threading, performance, dependency, or portability changes require architecture/known-issues updates as applicable.
- Preserve the prominent AI-assisted/generated-code notice. Do not imply that AI output has received human review unless it actually has.
- Prefer documentation of observed behavior over aspirational claims. Explicitly label plans and unverified platform/codec behavior.

## Git and repository hygiene

- Release versions use exactly four numeric components in `A.B.C.D` form. Date-derived releases follow `1.YY.DD.MM`; the current version for 2026-11-05 is `1.26.5.11`. Every Store update must compare greater than the previously published MSIX version. Synchronize project/assembly metadata, CLI help, installer, MSIX defaults/template, and EN/RU documentation whenever the version changes.

- Remotes: `github` is `git@github.com:Geckon01/YACPlayer.git`; `origin` is the private Gitea server at `https://git.geckon01.ru/geckon01/YacPlayer.git`.
- On 2026-11-03, history on both remotes was rewritten to remove accidentally committed copyrighted test audio. Do not merge or push an old clone/branch based on the pre-rewrite history; rebase onto the sanitized `main` or reclone.
- Never commit audio test media, even briefly. Use generated fixtures. Inspect staged content and commit messages for media names before every push.
- Avoid `git add -A` in a dirty tree. Stage explicit paths after reviewing `git status --short` and `git diff --cached`.
- `artifacts/` contains intentionally tracked binaries, so a changed binary is not automatically disposable or appropriate to commit.
- Do not force-push, rewrite history, delete releases/tags, or alter remotes without explicit user authorization.
- Keep commits focused and use English commit messages.
- Never commit local history-cleanup bundles, temporary mirrors/tools, IDE profiles, or user-specific publish profiles.

Local-only caution at the time of this update: `yacplayer-history-backup-20261103.bundle` contains the pre-rewrite history and copyrighted blobs. It is a recovery artifact only; never stage, publish, or copy it into a release. The working tree may also contain user-owned untracked publish outputs/profiles. Preserve them unless the user explicitly asks otherwise.

## Current verification record

Update this section after every material change.

- Predictive BPM tracker added on 2026-11-05: replaced broadband RMS-difference tempo detection with a 1024-sample/256-hop multi-band spectral-flux onset envelope, interpolated 55–200 BPM autocorrelation candidates, phase coverage scoring, and a bounded eight-agent PLL bank with confidence decay and switching hysteresis. The Beat indicator now follows predicted phase and survives short missing-beat sections. Added synthetic coverage for 97/127/173 BPM grid refinement, missing beats, and a 120→150 BPM change. Focused whitespace verification succeeded; Release build completed with 0 warnings/0 errors and all 34/34 tests passed. Accuracy, lock behavior, and CPU cost on a representative real-music corpus remain manually unverified; release binaries were not rebuilt.
- Installer release refresh on 2026-11-05: republished the framework-dependent win-x64 application into `artifacts/publish/`, confirmed `Yacplayer.exe --version` reports `1.26.5.11`, and compiled `artifacts/installer/Yacplayer-1.26.5.11-win-x64-setup.exe` successfully with Inno Setup 6.7.1. The installer is 3,242,417 bytes with SHA-256 `6E8E56BD884ABC417EBFE49372582E5802A3A645A206F09763AA316B9DF57902`; `dotnet test Yacplayer.sln -c Release --no-build` passed 29/29 tests. Installation, upgrade, and uninstallation smoke testing remain pending.
- Version update on 2026-11-05: synchronized project/assembly metadata, CLI help, installer, MSIX defaults, regression coverage, and EN/RU documentation to the date-derived version `1.26.5.11`. Focused whitespace verification passed; `dotnet build Yacplayer.sln -c Release --no-restore` succeeded with 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` passed 29/29 tests, and `--version` printed `Yacplayer 1.26.5.11`. Installer, MSIX, and tracked publish binaries were not rebuilt.
- Single-track shuffle crash fixed on 2026-11-05: after refilling an exhausted shuffle bag, `Playlist.Next` now handles the still-empty one-track Repeat All cycle by replaying the current item instead of calling `PopBag` with count zero. Added coverage for manual Next, automatic EOF, and unchanged Repeat Off termination. Focused whitespace verification and the regression passed; `dotnet build Yacplayer.sln -c Release --no-restore` succeeded with 0 warnings/0 errors; `dotnet test Yacplayer.sln -c Release --no-build` passed 29/29 tests. Real playback smoke testing remains pending.
- `App` decomposition on 2026-11-04: reduced `App.cs` from 727 to 418 lines and left it as composition root, event loop, and command router. Browser loading/navigation/deduplication moved to `BrowserController`; current-track metadata cancellation/completions to `CurrentTrackMetadataLoader`; visualizer/realtime DSP coordination to `VisualizationPipeline`; all player/browser layouts and viewports to `AppRenderer`. Added internal-boundary access for tests plus renderer-layout and browser-deduplication regressions. `dotnet format Yacplayer.sln whitespace` was applied to the changed source/test files, `dotnet build Yacplayer.sln -c Release --no-restore` succeeded with 0 warnings/0 errors, and `dotnet test Yacplayer.sln -c Release --no-build` passed 28/28 tests. Manual TUI/WASAPI smoke testing remains pending.
- Realtime audio-analysis panel added on 2026-11-04: `B` toggles a persisted third bottom panel containing RMS/peak/crest, zero-crossing rate, spectral centroid, dominant frequency, beat pulse, and heuristic BPM/confidence. A separate bounded pre-volume ring is written only while enabled, and the UI-thread analyzer is not invoked while disabled. Added regression tests for tap gating, synthetic tone/tempo analysis, and settings persistence. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/App.cs src/Yacplayer/Audio/AudioEngine.cs src/Yacplayer/Audio/PcmTapSampleProvider.cs src/Yacplayer/Cli/Arguments.cs src/Yacplayer/Configuration/PlayerSettings.cs src/Yacplayer/Dsp/RealtimeAudioAnalyzer.cs --no-restore --verify-no-changes` succeeded; `dotnet build Yacplayer.sln -c Release` succeeded with 0 warnings/0 errors; `dotnet test Yacplayer.sln -c Release --no-build` passed 26/26 tests. Real-audio BPM accuracy, WASAPI behavior, and TUI layout remain manually unverified.
- Large-folder responsiveness update on 2026-11-04: browser directory enumeration now runs as a cancellable generation-tagged background request whose completion is applied by the UI loop, avoiding render/input stalls while a folder opens. Browser queue additions use a persistent case-insensitive path set and one `Playlist.AddRange`; active shuffle cycles append only the new indices instead of rebuilding the bag. `FileBrowser` uses enumerated filesystem attributes instead of a separate `Directory.Exists` call per entry. Added shuffle-cycle and cancellation regressions; `dotnet build Yacplayer.sln -c Release` succeeded with 0 warnings/0 errors and `dotnet test Yacplayer.sln -c Release --no-build` passed 23/23 tests. Manual TUI testing with a large/slow directory remains pending.
- Playback ownership/settings cleanup on 2026-11-04: restored saved repeat/shuffle state into `Playlist`, removed the nonfunctional fullscreen-visualizer setting and `H` control, and replaced direct NAudio EOF/error callbacks with a bounded, generation-tagged `AudioEventQueue` drained by the UI loop. Added regression coverage for idempotent shuffle restoration, bounded queue overflow, and removal of the obsolete help entry. `dotnet build Yacplayer.sln -c Release` succeeded with 0 warnings/0 errors and `dotnet test Yacplayer.sln -c Release --no-build` passed 21/21 tests. Real WASAPI natural-EOF/error and TUI smoke testing remains pending.
- README FAQ update on 2026-11-04: renamed the troubleshooting section to FAQ in both language variants, converted its entries to question-and-answer wording, and added an explanation of the Windows-only target and the work required for real Linux/macOS support. Relative documentation links and Markdown formatting were checked without rebuilding code.
- Inline README video correction on 2026-11-04: replaced the repository-relative demo links in both language variants with the standalone GitHub attachment URL `https://github.com/user-attachments/assets/6b0c0f89-5e3f-4fda-b4a3-3f3359404995`, matching the Markdown pattern used by TwitchDownloader so GitHub renders an inline player. The attachment was uploaded through GitHub's README editor, its permanent URL was verified to resolve to `video/mp4`, the remote edit was cancelled without a commit, and the local source MP4 remains tracked for preservation.
- README demo update on 2026-11-04: replaced the top screenshot references in both language variants with links to the user-supplied `assets/Yacplayer_demo.mp4`. The 36.27-second H.264 video is 1912x996, contains no audio stream, is 6,758,466 bytes, and has SHA-256 `A8154462D12A16DFA725BA4612E525EC0E252332FE2B03B4C88D203C18384BBB`; media integrity and both relative links were checked without rebuilding code.
- `Playlist.cs` style cleanup on 2026-11-04: expanded the repeat enum, fields/properties, playback navigation, removal, shuffle history/bag handling, and helper loops into conventional blocks while preserving mutation order and algorithms. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/Library/Playlist.cs --no-restore` completed successfully, `dotnet build Yacplayer.sln -c Release` succeeded with 0 warnings/0 errors, and `dotnet test Yacplayer.sln -c Release --no-build` passed 18/18 tests, including playlist coverage.
- `PlayerSettings.cs` style cleanup on 2026-11-04: expanded the visualizer enum and each validation rule into conventional blocks without changing defaults, validation order, or accepted values. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/Configuration/PlayerSettings.cs --no-restore` completed successfully, `dotnet build Yacplayer.sln -c Release` succeeded with 0 warnings/0 errors, and `dotnet test Yacplayer.sln -c Release --no-build` passed 18/18 tests.
- `SettingsStore.cs` style cleanup on 2026-11-04: expanded loading, malformed-JSON recovery, ignored I/O branches, and temporary-file saving into readable blocks while preserving operation order and exception behavior. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/Configuration/SettingsStore.cs --no-restore` completed successfully, `dotnet build Yacplayer.sln -c Release` succeeded with 0 warnings/0 errors, and `dotnet test Yacplayer.sln -c Release --no-build` passed 18/18 tests.
- `Arguments.cs` style cleanup on 2026-11-04: expanded the record declaration, parser state, switch cases, validation branches, and early exit into conventional blocks without changing CLI behavior. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/Cli/Arguments.cs --no-restore` completed successfully, `dotnet build Yacplayer.sln -c Release` succeeded with 0 warnings/0 errors, and `dotnet test Yacplayer.sln -c Release --no-build` passed 18/18 tests.
- `Program.cs` style cleanup on 2026-11-04: expanded dense one-line argument checks, Ctrl+C handler, and top-level exception handling into conventional blocks without changing control flow or exit codes. `dotnet build Yacplayer.sln -c Release` succeeded with 0 warnings/0 errors and `dotnet test Yacplayer.sln -c Release --no-build` passed 18/18 tests.
- GitHub Actions test CI added on 2026-11-04: `.github/workflows/tests.yml` runs restore, Release build, and all tests on `windows-latest` for pushes, pull requests, and manual dispatches, with read-only repository permissions and no secrets. Local verification succeeded with `dotnet build Yacplayer.sln -c Release` at 0 warnings/0 errors and `dotnet test Yacplayer.sln -c Release --no-build` at 18/18 passing tests; the first hosted Actions run remains pending until the commit is pushed to GitHub.
- Last repository/code review: 2026-11-03.
- Reviewed sanitized `main`: `5b27795` before adding this guide.
- Confirmed project target/dependencies, source flow, 12 test definitions, publish profile, installer, English/Russian docs, and tracked artifact layout by inspection.
- The local branch was moved to the sanitized GitHub history only after confirming the old and new tip trees were identical.
- Build/test status for this documentation change: `dotnet build Yacplayer.sln -c Release` succeeded with 0 warnings/0 errors; `dotnet test Yacplayer.sln -c Release --no-build` passed 12/12 tests on 2026-11-03.
- Output-volume independence fix on 2026-11-03: moved volume/mute gain after `PcmTapSampleProvider`, added a pre-volume PCM regression test, and verified `dotnet build Yacplayer.sln -c Release --no-restore` with 0 warnings/0 errors and `dotnet test Yacplayer.sln -c Release --no-build` with 13/13 passing tests. Real WASAPI/TUI volume and mute smoke testing remains pending.
- The Store-identity MSIX was rebuilt successfully after the visualization-volume fix with Windows SDK `MakeAppx` 10.0.26100.9457; NuGet vulnerability metadata lookup emitted `NU1900`, but package creation succeeded.
- Version update on 2026-11-03: synchronized project/assembly, CLI, installer, MSIX, and documentation to `1.26.3.11`; added the four-component version policy and a regression test. Release build succeeded with 0 warnings/0 errors, 14/14 tests passed, `--version` printed `Yacplayer 1.26.3.11`, and the Store-identity `Yacplayer-1.26.3.11-Store-win-x64.msix` package was created successfully.
- Panel-title color update on 2026-11-03: changed text on bright-green title backgrounds from black to white. `dotnet build Yacplayer.sln -c Release --no-restore` succeeded with 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` passed 14/14 tests, and the Store-identity `Yacplayer-1.26.3.11-Store-win-x64.msix` was rebuilt successfully. Manual TUI inspection remains pending.
- Header-version update on 2026-11-03: the top-right frame now derives and displays `vA.B.C.D` from the running assembly. Release build succeeded with 0 warnings/0 errors, 14/14 tests passed, and the Store-identity `Yacplayer-1.26.3.11-Store-win-x64.msix` was rebuilt successfully. Manual TUI inspection remains pending.
- README artwork update on 2026-11-03: replaced the top icon in both language variants with the user-supplied PNG screenshot; image integrity and links were checked without rebuilding code.
- Installer release refresh on 2026-11-03: republished the framework-dependent win-x64 application into `artifacts/publish/`, confirmed `Yacplayer.exe --version` reports `1.26.3.11`, and compiled `artifacts/installer/Yacplayer-1.26.3.11-win-x64-setup.exe` successfully with Inno Setup 6.7.1. The current installer SHA-256 is `6A9A00B852824252FD90FDDE60A74D7375AB1B388EB272B21CE8E09386EDC436`. Installation/uninstallation smoke testing remains pending.
- Unicode diff mitigation on 2026-11-03: rows containing likely wide/zero-width Unicode in either adjacent frame are erased and fully repainted while ordinary rows retain run-level diffing; terminal autowrap is disabled during the alternate-screen session. Added a focused regression test; `dotnet build Yacplayer.sln -c Release --no-restore` succeeded with 0 warnings/0 errors and `dotnet test Yacplayer.sln -c Release --no-build` passed 15/15 tests. Manual mixed-width TUI smoke testing remains pending.
- Spectrum gradient update on 2026-11-03: replaced the three hard color thresholds with an interpolated ANSI true-color green/yellow/red gradient while retaining the yellow top marker; added true-color escape coverage. Final Release build succeeded with 0 warnings/0 errors and 16/16 tests passed. Manual visual gradient and mixed-width TUI smoke testing remains pending.
- Shared list viewport update on 2026-11-04: the file browser, main queue, and browser-side queue now render independent moving windows that keep their target selection/current item visible with a one-row margin; collection order remains unchanged. Added viewport movement and clamp regression tests; `dotnet build Yacplayer.sln -c Release --no-restore` succeeded with 0 warnings/0 errors and `dotnet test Yacplayer.sln -c Release --no-build` passed 18/18 tests. The installer and Store-identity MSIX were rebuilt successfully; the current MSIX SHA-256 is `8C4B16B234DE4F28702749EAFEBCF245535EB039FE8867CC6E3FF26A89D49DDE`. Manual long-queue navigation and automatic-EOF smoke testing remains pending.
- Microsoft Store preparation on 2026-11-03: the self-contained x64 MSIX was built successfully with Windows SDK `MakeAppx` 10.0.26100.8249; the manifest, generated assets, execution alias, MIT license, third-party notices, and privacy policy were inspected in the package layout.
- Verification after MSIX changes: `dotnet build Yacplayer.sln -c Release --no-restore` succeeded with 0 errors and one `NU1900` warning because NuGet vulnerability metadata was unreachable; `dotnet test Yacplayer.sln -c Release --no-build` passed 12/12 tests.
- Partner Center rejected the first Store build because the supplied `Publisher` value had been truncated, producing the wrong package family name. The full Publisher from the Product identity page, plus `Name` and `PublisherDisplayName`, were applied through the ignored local override and `Yacplayer-1.0.0.0-Store-win-x64.msix` was rebuilt on 2026-11-03. Windows `PackageFamilyNameFromId` confirmed the expected PFN `geckon01.YacPlayer_42xf8azg8zfh0`; no credential or signing material was added to tracked files.
- Partner Center upload/certification, Windows App Certification Kit, installation/launch, and WASAPI/TUI smoke checks remain pending because they require external submission and/or manual system testing.
- Manual WASAPI/TUI/installer smoke status: not performed for this documentation-only change.

## Agent completion checklist

Before handing off any task:

- [ ] Re-read the relevant sections of this file and inspect the implementation.
- [ ] Preserve unrelated tracked and untracked user changes.
- [ ] Add/update focused tests and run proportionate verification.
- [ ] Update English and Russian user documentation when public behavior changed.
- [ ] Update architecture and known issues when design, risks, or priorities changed.
- [ ] Update both `AGENTS.md` and `AGENTS.ru.md`, including the verification record.
- [ ] Review `git diff`, `git diff --cached`, and `git status --short`.
- [ ] Confirm no media fixtures, secrets, local backups, or unintended binaries are staged.
- [ ] Report what changed, what was verified, and what remains unverified.
