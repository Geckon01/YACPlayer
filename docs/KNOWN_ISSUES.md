# Known Issues and Portability Roadmap

**Primary language:** English · [Русская версия](#известные-проблемы-и-план-портирования) · [Architecture](ARCHITECTURE.md) · [README](../README.md)

This document records the currently known defects, technical debt, performance risks, missing behavior, and work required to run Yacplayer on operating systems other than Windows. It describes the present implementation, not confirmed future commitments.

Priority meanings: **P0** blocks a supported release or risks incorrect behavior; **P1** is important reliability or performance work; **P2** is a functional or maintainability gap; **P3** is a lower-impact improvement. **Roadmap** describes work outside the currently supported Windows target rather than a Windows-release defect.

## Summary

| Priority | Area | Current problem | Main consequence |
|---|---|---|---|
| Roadmap | Portability | The application targets `net10.0-windows` and uses WASAPI | Published Linux/macOS binaries are not usable players |
| P2 | Spectrum | The last valid spectrum has no elapsed-time decay | Bars can remain stale while playback is paused or samples stop arriving |
| P1 | Rendering | A new cell array and ANSI output are allocated every frame | Avoidable GC and CPU load, especially at 60 FPS |
| P1 | Library loading | Startup and `A` recursive scans still materialize the full result | Slow startup or a temporarily unresponsive UI on very large trees |
| P2 | Realtime analysis | The causal multi-hypothesis tracker is not validated on a representative music corpus | Several-second lock time and possible metrical-level errors remain |
| P3 | Metadata | Metadata is intentionally loaded only for the current track | Unplayed queue entries retain filename/unknown-artist fallbacks |
| P2 | Browser | Empty and inaccessible folders are not distinguished | Misleading error feedback |

## Correctness and concurrency

### P1 — sample ring cursor race (fixed 2026-11-11)

`SampleRingBuffer.Write` previously advanced `_read` when overwriting old data, while `Read` also advanced `_read`. The producer and consumer both wrote the same cursor; an interleaving could move it using stale state and rewind the consumer, causing torn or duplicated reads.

Resolution: the ring now follows a strict SPSC protocol. The producer owns only `_write` and never touches the read cursor; the consumer owns `_read` and catches itself up to `write - capacity` before each copy (drop-oldest semantics preserved). Capacity is rounded up to a power of two and indexed with a bitmask, removing per-sample modulo. Concurrent producer/consumer regression tests cover overflow rewind, `ReadLatest`/`Reset` interleavings, and the stale-cursor clamp. Residual theoretical torn-read window requires the producer to lap the consumer by a full ring between position reads; with 32768/65536-sample rings and 30–60 FPS consumption this is not reachable in practice.

### P1 — lifecycle and device integration coverage

Unit tests deliberately avoid a real audio device. WASAPI pause, seek, mute, natural EOF, device removal, default-device change, and decoder failure remain unverified integration paths.

Recommended action: add generated-WAV integration tests, an optional Windows WASAPI smoke suite, and repeatable rapid-switch/soak scenarios. Keep hardware-dependent tests opt-in in CI.

## Performance and resource use

### P2 — spectrum decay is frame-driven

`SpectrumAnalyzer` now uses an exact-window read: partial FFT windows remain queued and the last valid bands remain visible until a complete replacement window exists. This prevents 60 FPS and 4096-bin modes from starving the analyzer. The remaining gap is that stale bands do not decay according to elapsed time when playback is paused or samples stop arriving.

Recommended action: track monotonic time since the last complete FFT window and apply a time-based decay independently of UI FPS.

### P1 — per-frame terminal allocations

Every frame creates a `ScreenBuffer` cell array and a new ANSI diff string. At 30/60 FPS this causes continuous allocation and garbage collection proportional to terminal dimensions, even when very little changes.

Recommended action: reuse two fixed-size screen buffers, resize only when terminal dimensions change, and use a pooled/reusable output builder. Do not flush when the diff is empty. Benchmark allocation rate and CPU at 30 and 60 FPS across typical terminal sizes.

### P1 — audio callback performs tiny ring writes

For multichannel audio, `PcmTapSampleProvider` downmixes one frame at a time and invokes `ring.Write` for every mono sample. This creates excessive method, volatile, and modulo operations on the time-sensitive audio path.

Recommended action: downmix into a reusable block buffer and write once per decoded block. Avoid heap allocation, locks, FFT, metadata, logging, and terminal work in the callback.

### P1 — non-incremental recursive scanning

Opening a browser directory is now cancellable and does not block rendering. Browser additions use a persistent path `HashSet`, one `AddRange`, and incremental shuffle-bag extension, removing the previous quadratic insertion path. However, startup scanning and directory scans triggered by `A` still materialize the complete result; the latter is awaited by the input handler before the batch is applied.

Recommended action: make recursive scans cancellable streaming operations with bounded batches, progress reporting, and UI-loop application of results. Define behavior for symlinks/reparse points, permission failures, and a user navigating away during a scan.

### P2 — unnecessary DSP work and repeated band mapping

Spectrum analysis is now disabled in Waveform and Off modes, but still runs when no audio is playing. Logarithmic FFT-bin boundaries are recalculated for every band and every spectrum frame although they change only with FFT size, sample rate, or band count.

Recommended action: gate analysis by visibility/playback state and cache band-to-bin ranges. Measure before and after changes rather than asserting unverified CPU savings.

### P2 — frame scheduling

The UI uses periodic delay-based polling. Work time is added to the delay, so the effective rate drifts, and input/render work may continue at the configured rate when the screen is unchanged.

Recommended action: use a monotonic deadline, coalesce state changes, and lower the idle refresh rate while retaining responsive input and progress updates.

### P2 — no performance budget or soak baseline

There are no checked-in thresholds for CPU, allocations, memory growth, terminal bytes per second, callback duration, or large-library latency.

Recommended action: define representative scenarios and capture baselines with `dotnet-counters`, `dotnet-trace`, and Windows Performance Recorder. Add a long-running playback/track-switch soak test before making performance claims.

## Functional and UI gaps

- **P3 — metadata scope:** metadata is loaded only for the current track and applied on the UI loop. Unplayed queue entries intentionally retain lightweight fallback values; bitrate is still unavailable.
- **P2 — tempo estimate:** the Realtime Audio panel now uses multi-band spectral flux, interpolated autocorrelation, phase scoring, and a bounded bank of PLL tempo/phase agents. Synthetic constant-tempo, missing-beat, and tempo-change coverage exists, but no representative real-music corpus or synchronization-grade accuracy baseline does. It still needs several seconds of active playback and may choose a different metrical level for syncopated, changing, or weak rhythms. Keep the UI labelled as an estimate; validate accuracy and CPU cost before using it for synchronization.
- **P2 — browser diagnostics:** inaccessible directories and empty directories share the same user-facing result. Preserve and display enumeration errors without crashing.
- **P2 — Unicode layout:** clipping is based on .NET character positions rather than terminal grapheme/display width. Combining marks, emoji, and East Asian wide characters can still misalign borders and columns. Rows containing likely variable-width Unicode are now erased and fully repainted as a low-cost stale-glyph mitigation, but this is not a complete width-aware layout solution.
- **P3 — format support contract:** the browser extension allowlist and the actual decoder capabilities can diverge. Each advertised format needs decoder verification and an intelligible open-error path.
- **P3 — terminal compatibility:** ANSI/VT behavior, color depth, resizing, alternate-screen recovery, redirected input/output, and small-window layouts need broader testing outside Windows Terminal.

## Packaging and release gaps

- **Roadmap — non-Windows packages:** Inno Setup produces only a Windows installer. There are no `.deb`, `.rpm`, AppImage, Homebrew, `.pkg`, or notarized macOS artifacts because Linux and macOS are not supported runtimes yet.
- **P2 — runtime dependency:** the tracked framework-dependent publish and Inno Setup installer need the matching .NET 10 runtime. The Store MSIX and Native AOT publish are self-contained; a self-contained conventional installer or deliberate bootstrap flow remains optional release work.
- **P2 — CI matrix:** GitHub Actions now runs restore, Release build, and tests on Windows. Cross-platform builds, packaging checks, terminal smoke tests, and real-audio tests are not present.
- **P2 — release validation:** installer install/uninstall, upgrade, file associations, shortcuts, and clean-machine behavior are not automated.
- **P3 — repository hygiene:** generated `bin`, `obj`, publish output, IDE profiles, and packaged artifacts must remain clearly separated from source and intentionally ignored or versioned.

## Linux and macOS portability

### Current status

Yacplayer is **Windows-only**. Producing an executable for `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64` does not make the application functional on that platform. The project target is `net10.0-windows`; playback constructs NAudio's `WasapiPlayer`; the dependency graph includes Windows audio components; installer and icon configuration are Windows-oriented. Cross-published files seen in build directories are not evidence of runtime support.

The playlist, settings, CLI parsing, FFT math, most browser enumeration, and ANSI screen model are broadly portable. Audio output/decoding, platform paths, terminal behavior, packaging, and integration tests require explicit platform work.

### Required architecture changes

1. Change the portable application/core target to `net10.0`. Keep Windows-specific code in a Windows backend or separate `net10.0-windows` host.
2. Introduce an `IAudioEngine` contract covering open, play/pause, seek, volume/mute, snapshots, completion/error events, and deterministic disposal.
3. Move current NAudio/WASAPI code into a Windows implementation selected by platform.
4. Choose and implement Linux output through PipeWire/PulseAudio or ALSA, and macOS output through CoreAudio. Decoder choice must also be validated per platform; WASAPI replacement alone is insufficient if `AudioFileReader` uses Windows-only decoding paths for some formats.
5. Keep FFT tapping behind a platform-neutral PCM contract so every backend supplies normalized interleaved or mono samples consistently.
6. Replace Windows-oriented drive navigation with a platform filesystem-root service. Linux should start from `/` and expose mounted volumes sensibly; macOS should account for `/Volumes`, home directories, permissions, and sandbox rules if applicable.
7. Audit settings/configuration locations and use platform conventions rather than assuming Windows paths.
8. Verify terminal input, ANSI color, Unicode width, resize signals, Ctrl+C, and alternate-screen cleanup in common terminals on each OS.
9. Use conditional package references or separate host/backend projects so Windows-native dependencies are not shipped in Linux/macOS artifacts.
10. Add build and runtime tests for `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`; test on the actual OS, not only by cross-compiling.
11. Add platform-native distribution: at minimum archives with launch instructions, then signed/notarized packages as release requirements demand.
12. Document supported codecs per OS because availability may differ between Windows Media Foundation, Linux libraries, and macOS frameworks.

### Suggested project boundary

```text
Yacplayer.Core          domain, playlist, settings models, DSP, commands
Yacplayer.Terminal      portable ANSI view and input abstractions
Yacplayer.Audio.Abstractions
Yacplayer.Audio.Windows NAudio/WASAPI backend
Yacplayer.Audio.Linux   PipeWire/PulseAudio/ALSA backend
Yacplayer.Audio.Mac     CoreAudio backend
Yacplayer               platform-neutral composition/CLI where possible
```

This split is a direction, not a requirement to create six assemblies immediately. The essential rule is that portable code must not reference a Windows audio implementation.

### Port completion criteria

A platform should not be marked supported until it can open all documented codecs, play/pause/seek, change volume, advance at natural EOF, render without terminal corruption, browse parent/root folders, load/save settings in an appropriate location, survive device errors, and pass automated plus real-device smoke tests. Packaging must start on a clean supported machine without Windows assemblies or undocumented native dependencies.

## Recommended order of work

1. ~~Fix the ring protocol.~~ Completed 2026-11-11.
2. Correct FFT accumulation/decay and batch callback writes.
3. Add integration, concurrency, allocation, and soak baselines.
4. Reuse rendering buffers and make scanning incremental/cancellable.
5. Extract `IAudioEngine` and a platform filesystem service.
6. Retarget portable code, isolate WASAPI, and add Linux/macOS backends one platform at a time.
7. Add the OS test matrix and native packaging only after runtime behavior passes on real systems.

---

# Известные проблемы и план портирования

**Язык:** Русский · [English (основной)](#known-issues-and-portability-roadmap) · [Архитектура](ARCHITECTURE.ru.md) · [README](../README.ru.md)

Этот документ фиксирует известные дефекты, технический долг, риски производительности, незавершённые функции и работы, необходимые для запуска Yacplayer не только в Windows. Это описание текущего состояния, а не обещание реализации.

Приоритеты: **P0** блокирует поддерживаемый релиз или несёт риск некорректной работы; **P1** — важная надёжность или производительность; **P2** — функциональный или архитектурный пробел; **P3** — улучшение с меньшим влиянием. **Roadmap** обозначает работу вне текущей поддерживаемой Windows-платформы, а не дефект Windows-релиза.

## Краткий список

| Приоритет | Область | Текущая проблема | Последствие |
|---|---|---|---|
| Roadmap | Портируемость | Цель `net10.0-windows`, вывод через WASAPI | Сборки Linux/macOS не являются рабочими плеерами |
| P1 | Конкурентность | У курсора чтения кольцевого буфера нет одного владельца | Возможна потеря или несогласованность данных визуализации |
| P2 | Спектр | Последний корректный спектр не имеет затухания по времени | При паузе или прекращении samples столбцы могут оставаться устаревшими |
| P1 | Рендеринг | На каждом кадре создаются массив ячеек и ANSI-строка | Лишние GC и CPU, особенно при 60 FPS |
| P1 | Медиатека | Startup и recursive scan по `A` всё ещё материализуют полный результат | Медленный запуск или временно неотзывчивый UI на очень больших деревьях |
| P2 | Realtime-анализ | Causal multi-hypothesis tracker не проверен на репрезентативном музыкальном корпусе | Сохраняются несколько секунд захвата и возможные ошибки метрического уровня |
| P3 | Метаданные | Метаданные намеренно читаются только для текущего трека | Незапускавшиеся элементы сохраняют имя файла/Unknown artist |
| P2 | Браузер | Пустая и недоступная папки не различаются | Ошибочное сообщение пользователю |

## Корректность и конкурентность

### P1 — гонка курсора кольцевого буфера (исправлено 2026-11-11)

Ранее `SampleRingBuffer.Write` сдвигал `_read` при вытеснении старых данных, а `Read` сдвигал тот же `_read` после чтения. Атомарность не делала такой протокол корректным: при определённом чередовании потоков запись устаревшего значения откатывала consumer назад, вызывая рваные или дублированные чтения.

Решение: кольцо переведено на строгий SPSC-протокол. Producer владеет только `_write` и не трогает курсор чтения; consumer владеет `_read` и перед каждым копированием сам подтягивает позицию до `write - capacity` (семантика drop-oldest сохранена). Ёмкость округляется до степени двойки и индексируется маской, поштучный modulo устранён. Добавлены конкурентные регрессионные тесты producer/consumer: откат при переполнении, чередования `ReadLatest`/`Reset` и кламп устаревшей позиции. Теоретическое окно рваного чтения требует, чтобы producer обогнал consumer на полное кольцо между чтениями позиции; при кольцах 32768/65536 сэмплов и потреблении 30–60 кадров/с это недостижимо на практике.

### P1 — нет интеграционного покрытия жизненного цикла

Unit-тесты не используют реальное аудиоустройство. Не проверены WASAPI pause/seek/mute, естественный EOF, отключение устройства, смена default device и ошибки декодера.

Решение: интеграционные тесты с генерируемым WAV, опциональный Windows WASAPI smoke-набор и воспроизводимые rapid-switch/soak сценарии. Аппаратные тесты в CI должны оставаться опциональными.

## Производительность и ресурсы

### P2 — затухание спектра зависит от кадров

`SpectrumAnalyzer` теперь использует exact-window read: неполные FFT-окна остаются в очереди, а последние корректные bands сохраняются до появления полного следующего окна. Это устраняет starvation анализатора при 60 FPS и 4096 bins. Оставшийся пробел: при паузе или прекращении samples устаревшие bands не затухают по прошедшему времени.

Решение: отслеживать monotonic time с последнего полного FFT-окна и применять time-based decay независимо от UI FPS.

### P1 — выделения памяти на каждом кадре

Каждый кадр создаёт массив `ScreenBuffer` и строку ANSI diff. При 30/60 FPS это даёт постоянные аллокации и GC пропорционально размеру терминала даже при минимальных изменениях.

Решение: переиспользовать два screen buffer, менять размер только при resize, применять переиспользуемый/pooled output builder и не делать flush при пустом diff. Измерить CPU и allocation rate при 30/60 FPS.

### P1 — мелкие записи в ring из audio callback

Для многоканального аудио `PcmTapSampleProvider` сводит по одному frame и вызывает `ring.Write` для каждого mono sample. Это множит вызовы, volatile-операции и modulo на чувствительном аудиопути.

Решение: сводить в переиспользуемый блочный буфер и записывать один раз на блок. В callback не должно быть heap allocation, locks, FFT, metadata, logging или terminal work.

### P1 — recursive scan не инкрементален

Открытие browser-каталога теперь отменяемо и не блокирует rendering. Browser-add использует постоянный `HashSet` путей, один `AddRange` и инкрементальное расширение shuffle bag, поэтому прежний квадратичный путь вставки устранён. Однако startup scan и scan каталога по `A` всё ещё полностью материализуют результат; последний ожидается input handler до применения batch.

Решение: cancellable streaming scan, ограниченные batch, прогресс и применение результатов в UI-цикле. Определить поведение для symlink/reparse point, access denied и ухода пользователя из папки.

### P2 — лишний DSP и повторный расчёт диапазонов

FFT теперь отключается в режимах Waveform и Off, но всё ещё выполняется при отсутствии воспроизведения. Границы FFT bins можно кешировать: они меняются только с FFT size, sample rate или количеством полос.

### P2 — планирование кадров

Delay-based polling добавляет время работы к задержке, поэтому частота дрейфует; при неизменном экране работа продолжается с выбранным FPS. Нужны monotonic deadline, объединение изменений и пониженная idle-частота без потери отзывчивости.

### P2 — нет бюджета производительности

Не зафиксированы пороги CPU, allocation, роста памяти, terminal bytes/sec, длительности callback и задержки большой медиатеки. Нужны сценарии и baseline через `dotnet-counters`, `dotnet-trace`, Windows Performance Recorder, а также длительный soak test.

## Функциональные и UI-пробелы

- **P3 — область метаданных:** метаданные читаются только для текущего трека и применяются в UI-цикле. Остальные элементы намеренно используют лёгкие fallback-значения; bitrate пока отсутствует.
- **P2 — оценка темпа:** панель Realtime Audio теперь использует multi-band spectral flux, interpolated autocorrelation, phase scoring и ограниченный банк PLL tempo/phase agents. Есть synthetic coverage для постоянного темпа, пропущенных ударов и смены темпа, но нет репрезентативного корпуса реальной музыки и baseline точности уровня синхронизации. По-прежнему нужно несколько секунд активного воспроизведения, а синкопированный, меняющийся или слабый ритм может привести к выбору другого метрического уровня. UI должен обозначать значение как оценку; перед использованием для синхронизации нужно проверить точность и CPU cost.
- **P2 — диагностика браузера:** access denied должен отличаться от пустой папки и не приводить к падению.
- **P2 — Unicode:** расчёт по позициям .NET не учитывает grapheme/display width; combining marks, emoji и wide characters всё ещё могут ломать выравнивание. Строки с вероятным Unicode переменной ширины теперь очищаются и полностью перерисовываются как дешёвая защита от оставшихся символов, но это не заменяет полноценный width-aware layout.
- **P3 — контракт форматов:** расширения браузера и реальные возможности decoder могут расходиться; каждый заявленный формат требует проверки.
- **P3 — совместимость терминалов:** нужны тесты ANSI, цвета, resize, восстановления alternate screen, redirected I/O и маленьких окон вне Windows Terminal.

## Проблемы упаковки и релиза

- **Roadmap:** Inno Setup предназначен только для Windows; пакетов Linux/macOS нет, поскольку эти платформы пока не являются поддерживаемыми runtime.
- **P2:** tracked framework-dependent publish и Inno Setup installer требуют .NET 10 runtime. Store MSIX и Native AOT self-contained; self-contained обычный installer или bootstrap остаётся необязательной release-задачей.
- **P2:** GitHub Actions выполняет restore, Release build и tests на Windows. Cross-platform builds, packaging checks, terminal smoke и real-audio tests отсутствуют.
- **P2:** install/uninstall/upgrade, shortcuts и clean-machine сценарии не автоматизированы.
- **P3:** `bin`, `obj`, publish output, IDE profiles и релизные артефакты должны быть явно отделены от исходников и осознанно ignored/versioned.

## Портирование на Linux и macOS

### Текущее состояние

Yacplayer сейчас **работает только в Windows**. Публикация для `linux-x64`, `linux-arm64`, `osx-x64` или `osx-arm64` не делает плеер рабочим. Проект имеет target `net10.0-windows`, создаёт NAudio `WasapiPlayer`, включает Windows audio dependencies, а installer/icon ориентированы на Windows. Наличие cross-publish файлов в build-папках не доказывает runtime-поддержку.

Playlist, settings, CLI parser, FFT, большая часть browser enumeration и ANSI screen model в основном переносимы. Audio output/decoding, platform paths, terminal behavior, packaging и интеграционные тесты требуют отдельной реализации.

### Необходимые изменения

1. Перевести portable core на `net10.0`; Windows-код оставить в отдельном backend или `net10.0-windows` host.
2. Ввести `IAudioEngine`: open, play/pause, seek, volume/mute, snapshots, completion/error events и deterministic disposal.
3. Перенести NAudio/WASAPI в Windows implementation.
4. Реализовать Linux output через PipeWire/PulseAudio или ALSA, macOS — через CoreAudio. Отдельно проверить decoder: одной замены WASAPI недостаточно, если форматы используют Windows-only decoding.
5. Сделать platform-neutral PCM tap для FFT с одинаковым форматом samples у всех backend.
6. Ввести filesystem-root service: `/` и mounts в Linux, `/Volumes`, home и permissions в macOS вместо Windows-ориентированных drives.
7. Использовать системные conventions для settings/config paths.
8. Проверить input, ANSI color, Unicode width, resize signals, Ctrl+C и alternate-screen cleanup в распространённых терминалах каждой ОС.
9. Использовать conditional package references или отдельные host/backend projects, не поставляя Windows native dependencies в Linux/macOS.
10. Добавить build и реальные runtime tests для `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`; cross-compilation недостаточно.
11. Добавить platform-native distribution: сначала архивы и инструкции, затем signing/notarization и native packages по требованиям релиза.
12. Документировать поддерживаемые codecs отдельно для каждой ОС.

### Предлагаемые границы проектов

```text
Yacplayer.Core          domain, playlist, settings models, DSP, commands
Yacplayer.Terminal      portable ANSI view и input abstractions
Yacplayer.Audio.Abstractions
Yacplayer.Audio.Windows NAudio/WASAPI backend
Yacplayer.Audio.Linux   PipeWire/PulseAudio/ALSA backend
Yacplayer.Audio.Mac     CoreAudio backend
Yacplayer               composition/CLI
```

Необязательно сразу создавать шесть assemblies. Критически важно, чтобы portable code не ссылался на Windows audio implementation.

### Критерии готовности порта

ОС нельзя объявлять поддерживаемой, пока плеер не открывает все заявленные codecs, не выполняет play/pause/seek/volume, не переходит дальше при EOF, не портит terminal, позволяет ходить к parent/root, хранит settings в корректном месте, переживает device errors и проходит automated плюс real-device smoke tests. Пакет должен запускаться на чистой машине без Windows assemblies и недокументированных native dependencies.

## Рекомендуемый порядок работ

1. ~~Исправить ring protocol.~~ Выполнено 2026-11-11.
2. Исправить FFT accumulation/decay и блочную запись из callback.
3. Добавить integration, concurrency, allocation и soak baseline.
4. Переиспользовать render buffers; сделать scan incremental и cancellable.
5. Выделить `IAudioEngine` и platform filesystem service.
6. Перенацелить portable code, изолировать WASAPI и реализовывать Linux/macOS по одной платформе.
7. Добавлять OS test matrix и native packaging после проверки runtime на реальных системах.
