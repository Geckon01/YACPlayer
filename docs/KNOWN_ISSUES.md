# Known Issues and Portability Roadmap

**Primary language:** English · [Русская версия](#известные-проблемы-и-план-портирования) · [Architecture](ARCHITECTURE.md) · [README](../README.md)

This document records the currently known defects, technical debt, performance risks, missing behavior, and work required to run Yacplayer on operating systems other than Windows. It describes the present implementation, not confirmed future commitments.

Priority meanings: **P0** blocks a supported release or risks incorrect behavior; **P1** is important reliability or performance work; **P2** is a functional or maintainability gap; **P3** is a lower-impact improvement.

## Summary

| Priority | Area | Current problem | Main consequence |
|---|---|---|---|
| P0 | Portability | The application targets `net10.0-windows` and uses WASAPI | Published Linux/macOS binaries are not usable players |
| P1 | Concurrency | The sample ring has unclear ownership of its read cursor | Lost, duplicated, or inconsistent visualization samples are possible |
| P1 | Playback events | End-of-track handling can mutate playback state outside the UI loop | Races during EOF, track switching, or shutdown |
| P1 | Spectrum | Partial FFT windows are consumed and then discarded | Flicker, intermittent empty frames, and wasted work |
| P1 | Rendering | A new cell array and ANSI output are allocated every frame | Avoidable GC and CPU load, especially at 60 FPS |
| P1 | Library loading | Recursive scans and queue insertion are not incremental | Slow startup/UI stalls and poor scaling for large libraries |
| P2 | Settings | Saved repeat and shuffle state is not restored into the playlist | Persisted settings do not fully affect runtime behavior |
| P2 | UI | The fullscreen visualizer setting is exposed but incomplete | The `H` control promises a layout that is not implemented |
| P3 | Metadata | Metadata is intentionally loaded only for the current track | Unplayed queue entries retain filename/unknown-artist fallbacks |
| P2 | Browser | Empty and inaccessible folders are not distinguished | Misleading error feedback |

## Correctness and concurrency

### P1 — sample ring cursor race

`SampleRingBuffer.Write` may advance `_read` when overwriting old data, while `Read` also advances `_read`. The producer and consumer therefore both write the same cursor. Atomic reads/writes prevent torn values but do not provide a complete single-producer/single-consumer protocol; an interleaving can move the cursor using stale state.

Recommended action: replace it with a proven SPSC design whose producer owns only the write sequence and whose consumer owns only the read sequence. The consumer can clamp its local read position against `write - capacity`. Add sustained concurrent tests with sequence validation, wraparound, overwrite, reset, and shutdown.

### P1 — playback event ownership

NAudio raises `PlaybackStopped` on an audio/event context. The current downstream end-of-track path can change the playlist and replace/dispose audio resources outside the UI loop.

Recommended action: publish bounded `AudioEvent` values to a channel and let the UI loop exclusively perform playlist transitions, track opening, and error presentation. Test natural EOF, manual next/previous near EOF, rapid reopen, device failure, and application shutdown.

### P1 — lifecycle and device integration coverage

Unit tests deliberately avoid a real audio device. WASAPI pause, seek, mute, natural EOF, device removal, default-device change, and decoder failure remain unverified integration paths.

Recommended action: add generated-WAV integration tests, an optional Windows WASAPI smoke suite, and repeatable rapid-switch/soak scenarios. Keep hardware-dependent tests opt-in in CI.

## Performance and resource use

### P1 — partial FFT windows are discarded

`SpectrumAnalyzer.Analyze` calls `Read` for a full FFT buffer. When fewer samples are available, those samples have already been removed before the method clears the bands and returns. At a UI rate close to or above the rate at which full windows arrive, this can repeatedly starve the analyzer and visibly flash the spectrum.

Recommended action: expose an availability check or exact-window read that consumes nothing until a complete window exists. Alternatively maintain a DSP-side accumulation buffer. Preserve the last valid spectrum and apply time-based decay instead of immediately drawing silence.

### P1 — per-frame terminal allocations

Every frame creates a `ScreenBuffer` cell array and a new ANSI diff string. At 30/60 FPS this causes continuous allocation and garbage collection proportional to terminal dimensions, even when very little changes.

Recommended action: reuse two fixed-size screen buffers, resize only when terminal dimensions change, and use a pooled/reusable output builder. Do not flush when the diff is empty. Benchmark allocation rate and CPU at 30 and 60 FPS across typical terminal sizes.

### P1 — audio callback performs tiny ring writes

For multichannel audio, `PcmTapSampleProvider` downmixes one frame at a time and invokes `ring.Write` for every mono sample. This creates excessive method, volatile, and modulo operations on the time-sensitive audio path.

Recommended action: downmix into a reusable block buffer and write once per decoded block. Avoid heap allocation, locks, FFT, metadata, logging, and terminal work in the callback.

### P1 — non-incremental library scanning and insertion

Recursive folder enumeration is asynchronous relative to rendering but produces the full result before applying it. Adding many tracks one at a time can repeatedly update playlist/shuffle structures and approach quadratic work. Startup scanning also completes before the TUI is available.

Recommended action: use cancellable streaming enumeration, bounded batches, bulk playlist insertion, progress reporting, and UI-loop application of results. Define behavior for symlinks/reparse points, permission failures, duplicate paths, and a user navigating away during a scan.

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

- **P2 — settings restore:** `PlayerSettings.RepeatMode` and `Shuffle` are saved but not applied to `Playlist` at startup.
- **P2 — unfinished fullscreen mode:** the fullscreen-visualizer control lacks a dedicated layout. Implement it or remove the control until supported.
- **P3 — metadata scope:** metadata is loaded only for the current track and applied on the UI loop. Unplayed queue entries intentionally retain lightweight fallback values; bitrate is still unavailable.
- **P2 — browser diagnostics:** inaccessible directories and empty directories share the same user-facing result. Preserve and display enumeration errors without crashing.
- **P2 — queue scrolling:** the queue renders from its first item instead of keeping the selected/current item visible in long queues.
- **P2 — Unicode layout:** clipping is based on .NET character positions rather than terminal grapheme/display width. Combining marks, emoji, and East Asian wide characters can still misalign borders and columns. Rows containing likely variable-width Unicode are now erased and fully repainted as a low-cost stale-glyph mitigation, but this is not a complete width-aware layout solution.
- **P2 — controller size:** `App` owns input routing, browser state, playback orchestration, DSP sampling, persistence, and layout. Split it into application state/reducer, input commands, services, and view/layout code before adding substantially more features.
- **P3 — format support contract:** the browser extension allowlist and the actual decoder capabilities can diverge. Each advertised format needs decoder verification and an intelligible open-error path.
- **P3 — terminal compatibility:** ANSI/VT behavior, color depth, resizing, alternate-screen recovery, redirected input/output, and small-window layouts need broader testing outside Windows Terminal.

## Packaging and release gaps

- **P1 — Windows-only installer:** Inno Setup produces only a Windows installer. There are no `.deb`, `.rpm`, AppImage, Homebrew, `.pkg`, or notarized macOS artifacts.
- **P2 — runtime dependency:** the current Windows distribution is framework-dependent and needs the matching .NET 10 runtime. Consider self-contained packages or a deliberate bootstrap/install flow.
- **P2 — CI matrix:** there is no cross-platform build, test, packaging, or terminal smoke-test matrix.
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

1. Fix the ring protocol and route audio events through the UI loop.
2. Correct FFT accumulation/decay and batch callback writes.
3. Add integration, concurrency, allocation, and soak baselines.
4. Reuse rendering buffers and make scanning incremental/cancellable.
5. Finish or hide incomplete settings/UI behavior.
6. Extract `IAudioEngine` and a platform filesystem service.
7. Retarget portable code, isolate WASAPI, and add Linux/macOS backends one platform at a time.
8. Add the OS test matrix and native packaging only after runtime behavior passes on real systems.

---

# Известные проблемы и план портирования

**Язык:** Русский · [English (основной)](#known-issues-and-portability-roadmap) · [Архитектура](ARCHITECTURE.ru.md) · [README](../README.ru.md)

Этот документ фиксирует известные дефекты, технический долг, риски производительности, незавершённые функции и работы, необходимые для запуска Yacplayer не только в Windows. Это описание текущего состояния, а не обещание реализации.

Приоритеты: **P0** блокирует поддерживаемый релиз или несёт риск некорректной работы; **P1** — важная надёжность или производительность; **P2** — функциональный или архитектурный пробел; **P3** — улучшение с меньшим влиянием.

## Краткий список

| Приоритет | Область | Текущая проблема | Последствие |
|---|---|---|---|
| P0 | Портируемость | Цель `net10.0-windows`, вывод через WASAPI | Сборки Linux/macOS не являются рабочими плеерами |
| P1 | Конкурентность | У курсора чтения кольцевого буфера нет одного владельца | Возможна потеря или несогласованность данных визуализации |
| P1 | События аудио | Завершение трека может менять состояние вне UI-цикла | Гонки при EOF, переключении и завершении приложения |
| P1 | Спектр | Неполные FFT-окна считываются и выбрасываются | Мигание, пустые кадры и лишняя работа |
| P1 | Рендеринг | На каждом кадре создаются массив ячеек и ANSI-строка | Лишние GC и CPU, особенно при 60 FPS |
| P1 | Медиатека | Сканирование и добавление очереди не потоковые | Медленный запуск и плохое масштабирование |
| P2 | Настройки | Repeat и shuffle сохраняются, но не восстанавливаются в playlist | Настройки не полностью влияют на запуск |
| P2 | UI | Полноэкранный визуализатор не завершён | Клавиша `H` обещает отсутствующую компоновку |
| P3 | Метаданные | Метаданные намеренно читаются только для текущего трека | Незапускавшиеся элементы сохраняют имя файла/Unknown artist |
| P2 | Браузер | Пустая и недоступная папки не различаются | Ошибочное сообщение пользователю |

## Корректность и конкурентность

### P1 — гонка курсора кольцевого буфера

`SampleRingBuffer.Write` сдвигает `_read` при вытеснении старых данных, а `Read` сдвигает тот же `_read` после чтения. Атомарность не делает такой протокол корректным: запись устаревшего значения при определённом чередовании потоков возможна.

Решение: проверенная SPSC-схема, где producer владеет только номером записи, consumer — номером чтения, а отставший consumer локально ограничивает позицию значением `write - capacity`. Нужны длительные конкурентные тесты последовательности, переполнения, wraparound, reset и shutdown.

### P1 — владелец событий воспроизведения

NAudio вызывает `PlaybackStopped` в аудио/event-контексте. Текущий путь EOF может менять playlist и заменять/освобождать аудиоресурсы вне UI-цикла.

Решение: передавать ограниченные `AudioEvent` через channel, а playlist, открытие трека и отображение ошибок менять только в UI-цикле. Проверить естественный EOF, next/previous около EOF, быстрое переоткрытие, ошибку устройства и shutdown.

### P1 — нет интеграционного покрытия жизненного цикла

Unit-тесты не используют реальное аудиоустройство. Не проверены WASAPI pause/seek/mute, естественный EOF, отключение устройства, смена default device и ошибки декодера.

Решение: интеграционные тесты с генерируемым WAV, опциональный Windows WASAPI smoke-набор и воспроизводимые rapid-switch/soak сценарии. Аппаратные тесты в CI должны оставаться опциональными.

## Производительность и ресурсы

### P1 — выбрасываются неполные FFT-окна

`SpectrumAnalyzer.Analyze` запрашивает полный FFT-буфер. Если данных меньше, они уже удалены из ring, после чего bands очищаются. При неудачном соотношении UI FPS и скорости накопления это постоянно обнуляет спектр и вызывает мигание.

Решение: exact-window read, который ничего не потребляет без полного окна, либо накопительный DSP-буфер. При нехватке данных сохранять последний валидный спектр и применять зависящее от времени затухание.

### P1 — выделения памяти на каждом кадре

Каждый кадр создаёт массив `ScreenBuffer` и строку ANSI diff. При 30/60 FPS это даёт постоянные аллокации и GC пропорционально размеру терминала даже при минимальных изменениях.

Решение: переиспользовать два screen buffer, менять размер только при resize, применять переиспользуемый/pooled output builder и не делать flush при пустом diff. Измерить CPU и allocation rate при 30/60 FPS.

### P1 — мелкие записи в ring из audio callback

Для многоканального аудио `PcmTapSampleProvider` сводит по одному frame и вызывает `ring.Write` для каждого mono sample. Это множит вызовы, volatile-операции и modulo на чувствительном аудиопути.

Решение: сводить в переиспользуемый блочный буфер и записывать один раз на блок. В callback не должно быть heap allocation, locks, FFT, metadata, logging или terminal work.

### P1 — сканирование и добавление медиатеки не инкрементальны

Рекурсивное перечисление выполняется вне рендера, но результат применяется целиком. Поэлементное добавление большой очереди может многократно перестраивать связанные структуры и приближаться к квадратичной сложности. Стартовое сканирование завершается до открытия TUI.

Решение: cancellable streaming enumeration, ограниченные batch, bulk insert, прогресс и применение результатов в UI-цикле. Определить поведение для symlink/reparse point, access denied, дублей и ухода пользователя из папки.

### P2 — лишний DSP и повторный расчёт диапазонов

FFT теперь отключается в режимах Waveform и Off, но всё ещё выполняется при отсутствии воспроизведения. Границы FFT bins можно кешировать: они меняются только с FFT size, sample rate или количеством полос.

### P2 — планирование кадров

Delay-based polling добавляет время работы к задержке, поэтому частота дрейфует; при неизменном экране работа продолжается с выбранным FPS. Нужны monotonic deadline, объединение изменений и пониженная idle-частота без потери отзывчивости.

### P2 — нет бюджета производительности

Не зафиксированы пороги CPU, allocation, роста памяти, terminal bytes/sec, длительности callback и задержки большой медиатеки. Нужны сценарии и baseline через `dotnet-counters`, `dotnet-trace`, Windows Performance Recorder, а также длительный soak test.

## Функциональные и UI-пробелы

- **P2 — восстановление настроек:** `RepeatMode` и `Shuffle` сохраняются, но не применяются к `Playlist` при запуске.
- **P2 — незавершённый fullscreen:** отдельная полноэкранная компоновка визуализатора не реализована; её нужно закончить или временно убрать управление `H`.
- **P3 — область метаданных:** метаданные читаются только для текущего трека и применяются в UI-цикле. Остальные элементы намеренно используют лёгкие fallback-значения; bitrate пока отсутствует.
- **P2 — диагностика браузера:** access denied должен отличаться от пустой папки и не приводить к падению.
- **P2 — прокрутка очереди:** текущий/выбранный трек должен оставаться видимым в длинной очереди.
- **P2 — Unicode:** расчёт по позициям .NET не учитывает grapheme/display width; combining marks, emoji и wide characters всё ещё могут ломать выравнивание. Строки с вероятным Unicode переменной ширины теперь очищаются и полностью перерисовываются как дешёвая защита от оставшихся символов, но это не заменяет полноценный width-aware layout.
- **P2 — перегруженный `App`:** input, browser, playback, DSP, persistence и layout следует разделить до значительного расширения функций.
- **P3 — контракт форматов:** расширения браузера и реальные возможности decoder могут расходиться; каждый заявленный формат требует проверки.
- **P3 — совместимость терминалов:** нужны тесты ANSI, цвета, resize, восстановления alternate screen, redirected I/O и маленьких окон вне Windows Terminal.

## Проблемы упаковки и релиза

- **P1:** Inno Setup предназначен только для Windows; пакетов Linux/macOS нет.
- **P2:** Windows-дистрибутив framework-dependent и требует .NET 10 runtime; нужен осознанный выбор self-contained либо bootstrap.
- **P2:** нет cross-platform CI-матрицы build/test/package/terminal smoke.
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

1. Исправить ring protocol и передавать audio events в UI loop.
2. Исправить FFT accumulation/decay и блочную запись из callback.
3. Добавить integration, concurrency, allocation и soak baseline.
4. Переиспользовать render buffers; сделать scan incremental и cancellable.
5. Закончить или скрыть незавершённые UI/settings.
6. Выделить `IAudioEngine` и platform filesystem service.
7. Перенацелить portable code, изолировать WASAPI и реализовывать Linux/macOS по одной платформе.
8. Добавлять OS test matrix и native packaging после проверки runtime на реальных системах.
