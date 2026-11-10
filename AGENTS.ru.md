# Руководство для агентов и актуальное состояние Yacplayer

**Язык:** [English — основной и авторитетный](AGENTS.md) · Русский · [README](README.ru.md) · [Архитектура](docs/ARCHITECTURE.ru.md) · [Известные проблемы](docs/KNOWN_ISSUES.md#известные-проблемы-и-план-портирования)

Этот файл — оперативная точка входа для каждого агента, работающего с репозиторием. Он описывает реализованную систему, проверенное текущее состояние, оставшуюся работу и инварианты проекта.

## Обязательный протокол чтения и обновления

Каждый агент ОБЯЗАН выполнять эти правила, в том числе при первом знакомстве с проектом:

1. Полностью прочитать `AGENTS.md` до планирования, правок, разрушительных команд или коммита.
2. Проверять относящиеся к задаче исходники и тесты, а не считать этот документ доказательством текущего поведения.
3. Сохранять пользовательские и несвязанные изменения. Не выполнять широкие cleanup/reset/checkout/staging-команды без проверки точного эффекта.
4. Обновлять `AGENTS.md` в той же правке, если меняются поведение, архитектура, зависимости, команды, управление, конфигурация, persistence, форматы/ОС, тесты, упаковка, релизное состояние, известные дефекты, приоритеты или правила репозитория.
5. Синхронно обновлять этот русский файл. При расхождении формулировок авторитетна английская версия.
6. Если исследование обнаружило устаревший факт, исправить его, даже когда основная задача была другой. Для чисто read-only задачи обновление нужно только при обнаружении существенно нового состояния.
7. Перед передачей работы обновить секцию проверки в обоих файлах. Нельзя заявлять невыполненные тесты или ручные проверки.
8. Перед завершением просмотреть `git diff`, `git status --short` и `AGENTS.md`. Работа не завершена, если оперативное описание устарело.

Нельзя удалять или ослаблять этот протокол. Новый агент должен понять проект без истории предыдущего разговора.

## Снимок проекта

- Yacplayer 1.26.5.11 — полноэкранный терминальный музыкальный плеер с клавиатурным управлением.
- Поддерживается только Windows x64.
- .NET 10, `net10.0-windows`, nullable reference types.
- NAudio 3.3.0, `AudioFileReader`, WASAPI shared, запрошенная задержка 100 мс.
- Самописный ANSI/VT-интерфейс без GUI-фреймворка.
- Цветной FFT-спектр, realtime PCM waveform и режим Off; PCM для визуализации снимается до выходной громкости/mute.
- Опциональный realtime-анализ: отдельно отключаемый pre-volume PCM tap питает расчёты RMS/peak/crest, zero-crossing, spectral centroid/dominant frequency, multi-band spectral-flux onset detection и multi-hypothesis PLL BPM/phase tracking в UI-потоке. `B` отключает панель; tap дополнительно остаётся включённым, пока реактивный UI нуждается в его характеристиках.
- ATL 7.19.0 лениво загружает метаданные только текущего трека.
- Настройки: `%LOCALAPPDATA%\Yacplayer\settings.json`.
- Есть framework-dependent win-x64 publish, win-x64 Native AOT, Inno Setup 6 и self-contained x64 MSIX для Microsoft Store.
- 59 xUnit-тестов без реального аудиоустройства.
- MIT; в документации есть уведомление об AI-assisted/generated code.
- Публичные документы ведутся сначала на английском и имеют русские версии.

## Реализованное поведение

### Запуск и CLI

- `Program.cs` настраивает UTF-8, разбирает параметры, преобразует Ctrl+C в cancellation и возвращает код 2 для CLI-ошибок и 3 для ошибок запуска.
- Синтаксис: `yacplayer [path] [--recursive] [--fps 30|60] [--no-visualizer]`.
- `--help` и `--version` не создают аудио и alternate screen.
- Файл сразу воспроизводится, каталог сканируется в очередь, а без пути браузер открывается в заданном стартовом, последнем или текущем рабочем каталоге — в таком порядке.
- При перенаправленном вводе/выводе TUI не запускается.

### Воспроизведение и очередь

- Реализованы play, pause/resume, stop/reset, seek ±5 секунд, previous/next и переход при естественном EOF.
- Громкость 0–100%, mute сохраняет логическое значение громкости.
- Repeat Off/All/One; One повторяет при автоматическом EOF, но ручной Next переходит дальше.
- Shuffle использует bag без повторов внутри цикла и history для Previous.
- При одном текущем треке Shuffle + Repeat All начинает новый цикл повторным воспроизведением этого трека и не пытается извлечь элемент из пустого bag.
- Batch insert добавляет в активный shuffle bag только новые indices, не перестраивая текущий цикл.
- Сохранённые repeat и shuffle применяются к `Playlist` при запуске.
- EOF и ошибки передаются из NAudio в UI-loop через bounded queue с generation tag; устаревшие события игнорируются.
- Удаление из очереди никогда не удаляет файл.
- `Playlist` допускает дубли, браузер устраняет их по полному пути без учёта регистра.
- Независимые `ListViewport` удерживают выбранный/текущий элемент видимым в основной очереди, списке файлов и очереди внутри browser, не переставляя элементы коллекций.

### Файловый браузер

- Интерфейс в стиле Midnight Commander: файлы слева, очередь справа при достаточной ширине.
- `[..]` и Backspace открывают родителя; выше корня диска показывается `This PC` с готовыми дисками.
- Сначала каталоги, затем регистронезависимая сортировка имён.
- Видимые расширения: `.wav`, `.mp3`, `.m4a`, `.aac`, `.wma`, `.flac`.
- `Enter` входит в каталог или добавляет/запускает файл; `A` добавляет файл/каталог без запуска.
- Перечисление каталога отменяемо и возвращает generation-checked результат через UI queue, поэтому открытие большой папки не останавливает rendering. Browser использует постоянный case-insensitive set путей и один batch insert в playlist.
- Рекурсивный scan пропускает reparse points и недоступные ветви.

### Терминальный UI

- `TerminalSession` управляет alternate screen, курсором и восстановлением терминала.
- Кадр строится в `ScreenBuffer`, затем `Diff` выводит только изменённые ANSI-run; строки с вероятным Unicode переменной ширины очищаются и полностью перерисовываются.
- Широкая компоновка содержит header, Now Playing, Queue, visualizer, Playback и Audio Info; есть компактный режим.
- На зелёном фоне заголовков панелей используется белый полужирный текст, а рамки остаются ярко-зелёными.
- Справа в верхней рамке показывается версия запущенной сборки в формате `vA.B.C.D`; она скрывается только при недостаточной ширине терминала.
- Progress bar рисуется зелёными `█`, остаток — `·`.
- Спектр использует `█`, плавный ANSI true-color градиент зелёный/жёлтый/красный по высоте и жёлтый верхний маркер.
- `V`: Spectrum → Waveform → Off. При смене режима backlog очищается; FFT не считается в Waveform/Off. Spectrum analysis потребляет только полные FFT-окна, поэтому высокий UI FPS и режим 4096 bins не выбрасывают частичное накопление.
- Waveform рисуется зелёными `.` по min/max envelope. Точка — актуальное намеренное решение дизайна.
- `B` переключает сохраняемую панель Realtime Audio в достаточно широком layout. В выключенном состоянии вторичный tap не пишет данные (кроме случая, когда они нужны реактивному UI), а анализатор уровней, спектра и темпа не вызывается.
- `T` переключает сохраняемую музыкально-реактивную тему Off → Subtle → Visualizer. `Off` (значение по умолчанию, сохраняющее прежний вид установок) рисует статическую тему без дополнительных вычислений. Subtle/Visualizer плавно смещают рамки панелей, фоны зелёных заголовков панелей, фон активной строки очереди, цвет футера и градиент/маркер столбцов спектра (та же форма зелёно-тёплого перехода, повёрнутая к оттенку темы и осветлённая пульсом баса) по сглаженным bass energy (пульс яркости), spectral centroid (ограниченный зелёно-бирюзовый диапазон оттенка) и RMS (насыщенность) с частотой около 12.5 FPS через `ReactiveThemeController` с RGB true-color фонами. На паузе, остановке и без аудиоданных цвета плавно возвращаются к статической теме.
- `O` открывает полноэкранное меню всех сохраняемых JSON-параметров. Корректные изменения сразу применяются и сохраняются; пути редактируются inline и должны указывать на существующие каталоги.
- `Q` открывает модальный диалог подтверждения выхода `Yep` / `Nope` поверх любого экрана приложения, кроме редактирования пути настройки. По умолчанию выбрано безопасное `Nope`; стрелки/Tab меняют выбор, Enter принимает его, Esc/N отменяет, Y подтверждает выход. Ctrl+C остаётся немедленным cancellation path.
- Строка `Beat` панели Realtime Audio показывает предиктивный импульс ASCII-котиком: `=^o.o^=` на доле и `=^-.-^=` между долями.
- Пасхалка: при активной реактивной теме метка версии в шапке вспыхивает новым цветом (оттенок сдвигается на золотой угол) на каждом предсказанном бите и гаснет к белому через экспоненциальное затухание ~220 мс; фронт бита отслеживается каждый кадр UI, поэтому короткие вспышки не теряются из-за 80 мс гейта цветов. С выключенной темой метка остаётся белой на ярко-зелёном.

### Метаданные и настройки

- До загрузки используются имя файла, `Unknown artist`, пустой album и нулевая duration.
- ATL загружает только текущий элемент. Результат передаётся через concurrent queue и применяется UI-loop.
- Валидация ограничивает volume, FPS 30/60, FFT 2048/4096, enum и существование last/startup directory.
- Повреждённый JSON переносится в `.bad`; сохранение идёт через `.tmp`.
- Модель содержит volume, mute, visualizer, realtime analysis, режим реактивного UI, repeat, shuffle, recursive, FFT, FPS, last directory и опциональный фиксированный startup directory. Версия схемы показывается в меню read-only.

### Упаковка

- `assets/Yacplayer.ico` встроен через `ApplicationIcon`.
- Обе версии README встраивают беззвучную демонстрацию продукта через GitHub user-attachment URL; `assets/Yacplayer_demo.mp4` хранит исходное видео, а `assets/Yacplayer_screenshot.png` сохранён как статический скриншот продукта.
- `artifacts/publish/` — отслеживаемый framework-dependent дистрибутив для installer.
- `NativeAotWinX64.pubxml` создаёт self-contained trimmed AOT в `artifacts/publish-aot/`.
- `Yacplayer.iss` устанавливает приложение для текущего пользователя в `%LOCALAPPDATA%\Programs\Yacplayer`, поддерживает английский/русский мастер и проверяет .NET 10.
- `packaging/msix/Build-Msix.ps1` публикует self-contained win-x64, генерирует Store assets и full-trust manifest и создаёт unsigned MSIX. Identity из Partner Center и сертификаты остаются локальными и игнорируются Git; принятый пакет подписывает Microsoft.

## Архитектура и потоки данных

```text
Program / CLI
    -> App async UI loop (composition, input routing, playlist orchestration)
        -> BrowserController -> FileBrowser worker -> UI-owned browser state
        -> SettingsController -> immediate runtime effects and SettingsStore save
        -> CurrentTrackMetadataLoader -> MetadataService -> UI-owned TrackEntry
        -> VisualizationPipeline
        -> AudioEngine -> AudioFileReader -> PcmTapSampleProvider
            -> visualization SampleRingBuffer -> SpectrumAnalyzer / waveform
            -> gated analysis SampleRingBuffer -> RealtimeAudioAnalyzer
            -> VolumeSampleProvider -> WasapiPlayer -> Windows output
            -> bounded AudioEventQueue -> UI-owned EOF/error handling
        -> AppRenderer -> ScreenBuffer -> ANSI diff -> TerminalSession
        <-> SettingsStore -> settings.json
```

Контексты выполнения:

- main async loop: input, browser/playlist, DSP reads, rendering, settings;
- NAudio playback thread: декодированные samples, downmix и ring writes;
- NAudio event context: `PlaybackStopped` неблокирующе публикует bounded event с generation tag;
- thread pool: каталоги и ATL metadata.

Инварианты:

- Одновременно не более одной пары decoder/output.
- Audio callback не выполняет FFT, terminal, metadata, JSON/file I/O, logging, blocking wait или неограниченные allocation.
- Sample transport для визуализации и анализа ограничен и не блокирует playback.
- Отключение realtime-анализа прекращает callback-side запись в его ring и все вычисления в UI-потоке, а не только скрывает панель.
- Playlist/browser принадлежат UI-loop; audio callbacks могут публиковать события, но не меняют playlist/audio state.
- Queue removal не удаляет файлы.
- Пользовательский текст проходит через `ScreenBuffer.Write`, без raw ANSI.
- Help/version не создают audio/alternate screen.
- Recursive scan не следует по reparse points.
- Runtime остаётся локальным, пока network-функция не спроектирована и не задокументирована явно.

## Карта репозитория

| Путь | Назначение |
|---|---|
| `src/Yacplayer/Program.cs` | Entry point, CLI, cancellation, top-level errors |
| `src/Yacplayer/App.cs` | Composition root, loop, input routing и shutdown/settings orchestration |
| `src/Yacplayer/Application/` | Browser controller, lifecycle metadata текущего трека и visualization pipeline |
| `src/Yacplayer/Application/SettingsController.cs` | Полноэкранная навигация настроек, inline-редактирование путей и эффекты изменений |
| `src/Yacplayer/Presentation/` | Player/browser layouts и terminal rendering |
| `src/Yacplayer/Cli/` | Parser и help |
| `src/Yacplayer/Configuration/` | Settings и JSON storage |
| `src/Yacplayer/Library/` | Playlist, browser, track model, metadata |
| `src/Yacplayer/Audio/` | NAudio/WASAPI, PCM tap, snapshot, ring |
| `src/Yacplayer/Dsp/` | Spectrum FFT/bands и realtime-анализ уровней, спектра, beat и tempo |
| `src/Yacplayer/Terminal/` | Alternate screen и differential renderer |
| `tests/Yacplayer.Tests/` | Device-independent xUnit tests |
| `.github/workflows/tests.yml` | Windows Release build и тесты для push и pull request |
| `assets/` | PNG и Windows ICO |
| `installer/` | Inno Setup source |
| `packaging/msix/` | Manifest, сценарий MSIX-сборки и пример несекретной identity для Microsoft Store |
| `artifacts/` | Намеренно отслеживаемые release binaries |
| `docs/ARCHITECTURE*.md` | Полная архитектура EN/RU |
| `docs/KNOWN_ISSUES.md` | Двуязычный backlog и portability roadmap |

## Сборка и проверка

```powershell
dotnet restore Yacplayer.sln
dotnet build Yacplayer.sln -c Release
dotnet test Yacplayer.sln -c Release
dotnet run --project src/Yacplayer --
```

GitHub Actions повторяет restore, Release build и test gate на `windows-latest` для push, pull request и ручного запуска. Workflow имеет только read-only доступ к репозиторию и не требует секретов.

Framework-dependent publish:

```powershell
dotnet publish src/Yacplayer/Yacplayer.csproj `
  -c Release -r win-x64 --self-contained false `
  -o artifacts/publish
```

Native AOT:

```powershell
dotnet publish src/Yacplayer/Yacplayer.csproj `
  -p:PublishProfile=NativeAotWinX64
```

AOT требует Visual Studio Build Tools с Desktop development with C++.

MSIX для Microsoft Store (по умолчанию локальная unsigned identity):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File packaging\msix\Build-Msix.ps1
```

Для submission используйте игнорируемый `StoreIdentity.local.psd1` со значениями из Partner Center; подробности — в `docs/MICROSOFT_STORE.md`. Сертификаты, private keys, пароли и Partner Center API credentials нельзя добавлять в репозиторий.

Installer собирается после обновления `artifacts/publish/`:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Yacplayer.iss
```

Без явной release-задачи нельзя регенерировать/коммитить бинарные артефакты. Для обычной правки минимум — Release build и tests. Для audio/terminal/installer дополнительно нужен ручной smoke: browser parent/root, legal/generated WAV, pause/seek/volume/mute/EOF, visualizers, resize, Q/Ctrl+C и terminal restore; для installer — install/launch/upgrade/uninstall.

Нельзя добавлять копирайтную музыку как fixture. Генерировать WAV в тесте или держать ручные media вне репозитория.

## Известные проблемы и следующая работа

Полный backlog: [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md).

### P1

1. Переиспользовать screen buffers и ANSI storage вместо allocation на каждом frame.
2. Пакетно downmix/write multichannel samples вместо `ring.Write` на каждый sample.
3. Сделать recursive scan incremental/cancellable/batched; startup и `A` пока материализуют весь результат.
4. Зафиксировать CPU/allocation/memory/callback/terminal/soak baselines.

### P2/P3

- Добавить focused rendering test, фиксирующий намеренный символ `.` и min/max envelope waveform.
- Убрать из help неработающие `Tab` и `/ search` либо реализовать их.
- Отличать inaccessible directory от empty.
- Учитывать grapheme/display width.
- Не считать spectrum при inactive playback и кешировать band/bin mapping.
- Применять elapsed-time decay к последнему корректному spectrum, когда samples перестают поступать.
- Перейти к monotonic frame deadline и снижать idle rate.
- Добавить input/layout snapshots, ring stress, generated-WAV integration, optional WASAPI, installer и soak tests.
- Добавить bitrate только при явной поддержке, сохранив lazy current-track policy.
- Считать realtime BPM эвристической оценкой: spectral-flux/PLL tracker требует нескольких секунд и может выбрать другой метрический уровень; до использования для синхронизации нужны проверка на репрезентативном корпусе реальной музыки и измерение CPU cost.
- Отдельными style-only правками отформатировать плотный однострочный код в оставшихся domain-классах и тестах.

## Платформы

Поддерживается только Windows. Cross-publish текущего проекта не означает поддержку Linux/macOS.

План порта: отделить portable core от Windows host, ввести `IAudioEngine` и neutral PCM contract, изолировать NAudio/WASAPI, реализовать PipeWire/PulseAudio/ALSA и CoreAudio, абстрагировать filesystem/config roots, проверить codec/terminal/device behavior на реальных ОС и только затем добавлять CI/packages.

Нельзя заявлять поддержку ОС только по успешной компиляции.

## Политика документации

- Английский — основной и авторитетный язык.
- Синхронно обновлять `README.ru.md`, `ARCHITECTURE.ru.md`, русскую половину `KNOWN_ISSUES.md` и этот файл.
- Изменение пользовательского поведения требует обновления README/keymap/config в обоих языках.
- Изменение архитектуры, потоков, производительности, зависимостей или portability требует обновления architecture/known issues.
- Сохранять заметное уведомление об AI-assisted/generated code и не утверждать человеческое ревью без факта.
- Описывать наблюдаемое поведение; планы и непроверенные codec/platform claims помечать явно.

## Git и гигиена репозитория

- Release-версии состоят ровно из четырёх числовых компонентов `A.B.C.D`. Для date-derived releases используется `1.YY.DD.MM`; текущая версия для 2026-11-05 — `1.26.5.11`. Каждый Store update должен быть численно больше ранее опубликованной MSIX-версии. При смене версии синхронно обновлять project/assembly metadata, CLI help, installer, MSIX defaults/template и EN/RU документацию.

- `github`: `git@github.com:Geckon01/YACPlayer.git`; `origin`: приватный Gitea `https://git.geckon01.ru/geckon01/YacPlayer.git`.
- 2026-11-03 обе истории были переписаны для удаления случайно добавленного копирайтного тестового аудио. Старые clone/branch нельзя merge/push; их нужно rebase на очищенный `main` или переклонировать.
- Никогда не коммитить аудиотесты, даже временно. Использовать generated fixtures и проверять staged content/commit message.
- Не использовать `git add -A` в грязном дереве; stage только явные пути после `status` и cached diff.
- `artifacts/` содержит намеренно отслеживаемые binaries; их нельзя автоматически удалять или бездумно коммитить.
- Force-push, rewrite, удаление release/tag и изменение remotes требуют явного разрешения пользователя.
- Коммиты должны быть сфокусированы, текст — английский.
- Не коммитить cleanup bundle/mirror/tools, IDE profile или user-specific publish profile.

Локальное предупреждение: `yacplayer-history-backup-20261103.bundle` содержит старую историю и копирайтные blobs. Это только recovery artifact; его нельзя stage/publish/release. В дереве могут быть и другие пользовательские untracked publish outputs/profiles — сохранять без явной команды пользователя.

## Текущая проверка

- Пасхалка со вспышкой версии добавлена 2026-11-11: при активной реактивной теме метка версии в шапке вспыхивает насыщенным цветом на каждом предсказанном бите (оттенок сдвигается на золотой угол, поэтому соседние биты различаются) и гаснет к белому через экспоненциальное затухание 220 мс. Детектор фронта бита и затухание свечения выполняются каждый кадр UI (сравнение bool и одно умножение double), минуя 80 мс гейт обновления цветов, поэтому короткие вспышки не теряются; пик вспышки не приглушается затуханием собственного кадра. В `ReactiveTheme` добавлены `VersionGlow`/`VersionHue`. С выключенной темой метка остаётся статической белой на ярко-зелёном. Добавлены три теста: ротация цвета между битами с полным затуханием до нуля, конечные точки цвета вспышка/белый плюс отсутствие вспышки в Off и эмиссия рендерером производного от темы цвета версии. `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 59/59 тестов. Ощущение синхронизации с битом в реальном терминале вручную не проверено; бинарные артефакты не пересобирались, изменение не закоммичено.

- Музыкально-реактивный UI добавлен 2026-11-11: новая сохраняемая настройка `ReactiveUiMode` (Off/Subtle/Visualizer, по умолчанию Off, поэтому существующие установки сохраняют статический вид) переключается клавишей `T` и отображается в меню настроек как `Reactive UI`. `RealtimeAudioAnalyzer` теперь также сообщает нормализованную характеристику `BassEnergy`, вычисляемую из его существующего 2048-точечного metrics FFT (без нового декодера, ring или потока). `Presentation/ReactiveThemeController.cs` сглаживает bass energy, spectral centroid и RMS однополюсными огибающими attack/release (примерно 70 мс attack, 350–450 мс release, постоянная времени оттенка ~2–3 с, гейт обновления 80 мс ≈ 12.5 FPS), ограничивает их палитрой HSL в зелёно-бирюзовом диапазоне и выдаёт `ReactiveTheme`, который `AppRenderer` потребляет через `AppRenderState`. Рамки панелей, фоны зелёных заголовков, фон активной строки очереди и футер следуют теме; `CellBackground` расширен из enum в RGB-capable record struct (значение по умолчанию остаётся ANSI default). В режиме Off анализ и цветовые вычисления не выполняются; на паузе, остановке и без аудиоданных параметры плавно возвращаются к статической теме. `PcmTapSampleProvider` получил `AnalysisAlways`, а `AudioEngine.SetReactiveAnalysis` держит feature ring наполненным, когда realtime-панель выключена, но тема нуждается в характеристиках. `VisualizationPipeline` гейтит анализатор по потребности панели или темы и вызывает его только во время воспроизведения. Добавлены `ReactiveThemeTests` (rate limiting, attack/release к базе, ограниченный delta яркости, оттенок остаётся в палитре, возврат на паузе, амплитуда Visualizer против Subtle, насыщенность от RMS, эмиссия true-color рендерером, persistence настройки со значением по умолчанию при отсутствующем свойстве, синтетическое разделение bass/treble-характеристик). Более поздний follow-up commit распространил ту же тему на панель спектра: активные клетки столбцов используют реактивный градиент (оттенок темы повёрнут на +38 градуса по высоте столбца, насыщенность ограничена 0.55–0.95, светлотная рампа осветлена пульсом баса), а верхний маркер столбца следует теме вместо статического жёлтого; Off сохраняет классический зелёно/жёлто/красный градиент и жёлтый маркер без изменений. Добавлен renderer-регресс, проверяющий эмиссию статической рампы в Off и производный от темы нижний цвет без жёлтого в активном режиме. `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 56/56 тестов. Визуальная плавность на реальном WASAPI/TUI, качество fallback на терминалах с ограниченной ANSI-палитрой и проверка отсутствия деградации аудиовоспроизведения вручную не проверены; бинарные артефакты не пересобирались, изменение не закоммичено.

- Гонка курсора кольцевого буфера исправлена 2026-11-11: в `SampleRingBuffer` восстановлены одиночные владельцы курсоров — producer пишет только `_write` и никогда не трогает `_read`, а consumer эксклюзивно владеет `_read` и перед каждым копированием сам подтягивает позицию до `write - capacity`, сохраняя семантику drop-oldest. Ёмкость округляется до степени двойки и индексируется маской, поштучный modulo устранён из audio callback. Это закрывает задокументированную P1-гонку, когда overflow producer и чтения consumer оба писали `_read`, а чередование check-then-act могло откатить consumer. Добавлены `RingBufferConcurrencyTests`: регрессия overflow producer/consumer, стресс-чередование `ReadLatest`/`Reset` и тест clamp устаревшей позиции. Renderer-тест подтверждения выхода приведён к английским подписям `Sure exit?` / `Yep` / `Nope`, введённым коммитом `2ce6a6e`; AGENTS/README/ARCHITECTURE (EN/RU) документируют английские подписи. `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 44/44 тестов. Поведение реальных WASAPI/TUI-визуализаций остаётся вручную не проверено; бинарные артефакты не пересобирались, изменение не закоммичено.

Обновление статуса коммитов: поддержка FFT/runtime закоммичена как `0d433ae`, меню настроек и подтверждение выхода — как `31a86f3`, исправление SPSC-протокола ring — как `7533ad2`. Эти коммиты заменяют более ранние формулировки «не закоммичено» в датированных записях ниже.

- Подтверждение выхода добавлено 2026-11-07: `Q` теперь открывает модальный диалог `Да` / `Нет` поверх player, browser, help и settings с безопасным выбором `Нет` по умолчанию. Реализованы навигация стрелками/Tab, принятие через Enter, отмена через Esc/N и подтверждение через Y; редактирование пути и Ctrl+C сохранили прежнюю семантику. Добавлен renderer-тест русского вопроса и обоих вариантов. Focused whitespace verification прошла успешно; `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 41/41 тестов. Ручная проверка интерактивного TUI пока не выполнена; бинарные артефакты не пересобирались, изменение не закоммичено.

- Starvation FFT исправлен 2026-11-07: `SpectrumAnalyzer` теперь использует `SampleRingBuffer.TryReadExact`, оставляя неполные окна в очереди и сохраняя последние bands до накопления полного окна из 2048/4096 samples. Это не позволяет смене FPS и runtime-смене FFT size постоянно съедать короткие блоки и навсегда обнулять spectrum. Добавлены regression-тесты, чередующие порции 48 kHz audio размером для 60 и 30 FPS, для обоих поддерживаемых FFT sizes. Focused spectrum tests прошли 3/3; `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 40/40 тестов. Реальное переключение WASAPI/TUI вручную не проверено; binaries не пересобирались, изменение оставлено без коммита.
- Полноэкранное меню настроек добавлено 2026-11-06: `O` открывает все сохраняемые JSON-параметры. Корректные изменения применяются в UI-loop и сразу сохраняются; пути валидируются, `LastDirectory` переводит browser в выбранную папку, а опциональный `StartupDirectory` имеет приоритет при запуске без пути. Смена FFT size во время работы пересоздаёт spectrum analyzer, для mute добавлен идемпотентный setter. Добавлены regression-тесты SettingsController, полноэкранного rendering, persistence startup directory и перенастройки FFT. `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 38/38 тестов. Ручная проверка TUI-ввода, редактирования путей, WASAPI-изменений и restart behavior не выполнена; binaries не пересобирались, изменение оставлено без коммита.
- Пасхалка с Beat-котиком добавлена 2026-11-05: строка beat в панели Realtime Audio теперь чередует `=^o.o^=` на предсказанной доле и `=^-.-^=` между долями. Renderer coverage проверяет оба состояния. Focused whitespace verification прошла; Release build завершился с 0 warnings/0 errors, прошли 34/34 теста. Timing в реальном терминале вручную не проверен; по просьбе пользователя изменение намеренно оставлено без коммита.
- Предиктивный BPM tracker добавлен 2026-11-05: определение темпа по разнице broadband RMS заменено на multi-band spectral-flux onset envelope с окном 1024 samples/hop 256, interpolated autocorrelation candidates в диапазоне 55–200 BPM, phase coverage scoring и ограниченный банк из восьми PLL agents с confidence decay и switching hysteresis. Индикатор Beat теперь следует предсказанной phase и переживает короткие пропуски ударов. Добавлены synthetic tests для уточнения 97/127/173 BPM вне сетки, пропущенных ударов и перехода 120→150 BPM. Focused whitespace verification прошла; Release build завершился с 0 warnings/0 errors, прошли 34/34 теста. Accuracy, lock behavior и CPU cost на репрезентативном корпусе реальной музыки вручную не проверены; release binaries не пересобирались.
- Обновление installer-релиза 2026-11-05: framework-dependent win-x64 приложение заново опубликовано в `artifacts/publish/`, `Yacplayer.exe --version` подтвердил `1.26.5.11`, а `artifacts/installer/Yacplayer-1.26.5.11-win-x64-setup.exe` успешно собран Inno Setup 6.7.1. Размер установщика — 3 242 417 байт, SHA-256 — `6E8E56BD884ABC417EBFE49372582E5802A3A645A206F09763AA316B9DF57902`; `dotnet test Yacplayer.sln -c Release --no-build` прошёл 29/29 тестов. Smoke-проверки установки, обновления и удаления остаются невыполненными.
- Обновление версии 2026-11-05: project/assembly metadata, CLI help, installer, MSIX defaults, regression-проверка и EN/RU документация синхронизированы на date-derived версию `1.26.5.11`. Focused whitespace verification прошла; `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 29/29 тестов, а `--version` вывел `Yacplayer 1.26.5.11`. Installer, MSIX и tracked publish binaries не пересобирались.
- Падение single-track shuffle исправлено 2026-11-05: после заполнения исчерпанного shuffle bag `Playlist.Next` обрабатывает всё ещё пустой цикл из одного трека в Repeat All повторным воспроизведением current item вместо вызова `PopBag` с count zero. Добавлено покрытие ручного Next, automatic EOF и неизменного завершения Repeat Off. Focused whitespace verification и regression прошли успешно; `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors; `dotnet test Yacplayer.sln -c Release --no-build` прошёл 29/29 тестов. Smoke с реальным воспроизведением остаётся невыполненным.
- Декомпозиция `App` выполнена 2026-11-04: `App.cs` уменьшен с 727 до 418 строк и оставлен composition root, event loop и command router. Browser loading/navigation/deduplication перенесены в `BrowserController`; cancellation/completions metadata текущего трека — в `CurrentTrackMetadataLoader`; координация visualizer/realtime DSP — в `VisualizationPipeline`; все player/browser layouts и viewports — в `AppRenderer`. Для тестов открыт internal boundary и добавлены regression-тесты renderer layouts и browser deduplication. К изменённым source/test files применён `dotnet format Yacplayer.sln whitespace`, `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 28/28 тестов. Ручной TUI/WASAPI smoke остаётся невыполненным.
- Панель realtime-аудиоанализа добавлена 2026-11-04: `B` переключает сохраняемую третью нижнюю панель с RMS/peak/crest, zero-crossing rate, spectral centroid, dominant frequency, beat pulse и эвристическими BPM/confidence. Отдельный bounded pre-volume ring записывается только во включённом состоянии, а UI-thread analyzer не вызывается после отключения. Добавлены regression-тесты gating tap, synthetic tone/tempo analysis и persistence настройки. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/App.cs src/Yacplayer/Audio/AudioEngine.cs src/Yacplayer/Audio/PcmTapSampleProvider.cs src/Yacplayer/Cli/Arguments.cs src/Yacplayer/Configuration/PlayerSettings.cs src/Yacplayer/Dsp/RealtimeAudioAnalyzer.cs --no-restore --verify-no-changes` завершился успешно; `dotnet build Yacplayer.sln -c Release` завершился с 0 warnings/0 errors; `dotnet test Yacplayer.sln -c Release --no-build` прошёл 26/26 тестов. Точность BPM на реальном аудио, WASAPI behavior и TUI layout вручную не проверены.
- Обновление отзывчивости больших папок 2026-11-04: перечисление browser-каталога теперь выполняется как отменяемый background request с generation tag, а результат применяется UI-loop, поэтому открытие папки не блокирует input/rendering. Добавление в очередь использует постоянный case-insensitive set путей и один `Playlist.AddRange`; активный shuffle-cycle получает только новые indices без перестройки bag. `FileBrowser` использует attributes, полученные перечислением, вместо отдельного `Directory.Exists` для каждого элемента. Добавлены regression-тесты shuffle-cycle и cancellation; `dotnet build Yacplayer.sln -c Release` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 23/23 теста. Ручной TUI smoke с большой/медленной папкой остаётся невыполненным.
- Исправление ownership воспроизведения и настроек 2026-11-04: сохранённые repeat/shuffle применяются к `Playlist`, неработающие fullscreen-настройка и клавиша `H` удалены, прямые NAudio callbacks EOF/error заменены bounded `AudioEventQueue` с generation tag, которую читает UI-loop. Добавлены regression-тесты идемпотентного восстановления shuffle, переполнения bounded queue и удаления устаревшей строки help. `dotnet build Yacplayer.sln -c Release` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 21/21 тест. Ручной WASAPI smoke естественного EOF/error и TUI остаётся невыполненным.
- Обновление FAQ в README 2026-11-04: раздел диагностики в обеих языковых версиях переименован в FAQ, существующие пункты преобразованы в формат вопрос–ответ и добавлено объяснение Windows-only target и работ, необходимых для реальной поддержки Linux/macOS. Относительные ссылки документации и Markdown-разметка проверены без пересборки кода.
- Исправление inline-видео README 2026-11-04: repository-relative ссылки в обеих языковых версиях заменены отдельной строкой с GitHub attachment URL `https://github.com/user-attachments/assets/6b0c0f89-5e3f-4fda-b4a3-3f3359404995`, по тому же Markdown-шаблону, который использует TwitchDownloader, чтобы GitHub показывал встроенный player. Attachment загружен через редактор README на GitHub, постоянный URL проверен на перенаправление к `video/mp4`, удалённая правка отменена без коммита, локальный исходный MP4 оставлен tracked для сохранности.
- Обновление демонстрации README 2026-11-04: ссылки на верхний скриншот в обеих языковых версиях заменены ссылками на предоставленный пользователем `assets/Yacplayer_demo.mp4`. H.264-видео длительностью 36,27 секунды имеет разрешение 1912x996, не содержит аудиопотока, занимает 6 758 466 байт и имеет SHA-256 `A8154462D12A16DFA725BA4612E525EC0E252332FE2B03B4C88D203C18384BBB`; целостность файла и обе относительные ссылки проверены без пересборки кода.
- Style cleanup `Playlist.cs` 2026-11-04: repeat enum, поля/свойства, навигация воспроизведения, удаление, shuffle history/bag и helper loops развёрнуты в обычные блоки с сохранением порядка мутаций и алгоритмов. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/Library/Playlist.cs --no-restore` завершился успешно, `dotnet build Yacplayer.sln -c Release` прошёл с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 18/18 тестов, включая playlist coverage.
- Style cleanup `PlayerSettings.cs` 2026-11-04: visualizer enum и каждое validation rule развёрнуты в обычные блоки без изменения defaults, порядка валидации и допустимых значений. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/Configuration/PlayerSettings.cs --no-restore` завершился успешно, `dotnet build Yacplayer.sln -c Release` прошёл с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 18/18 тестов.
- Style cleanup `SettingsStore.cs` 2026-11-04: загрузка, восстановление после malformed JSON, игнорируемые I/O branches и сохранение через temporary file развёрнуты в читаемые блоки с сохранением порядка операций и exception behavior. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/Configuration/SettingsStore.cs --no-restore` завершился успешно, `dotnet build Yacplayer.sln -c Release` прошёл с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 18/18 тестов.
- Style cleanup `Arguments.cs` 2026-11-04: объявление record, состояние parser, ветви switch, validation branches и early exit развёрнуты в обычные блоки без изменения поведения CLI. `dotnet format Yacplayer.sln whitespace --include src/Yacplayer/Cli/Arguments.cs --no-restore` завершился успешно, `dotnet build Yacplayer.sln -c Release` прошёл с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 18/18 тестов.
- Style cleanup `Program.cs` 2026-11-04: плотные однострочные проверки аргументов, обработчик Ctrl+C и top-level exception handling развёрнуты в обычные блоки без изменения control flow и exit codes. `dotnet build Yacplayer.sln -c Release` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 18/18 тестов.
- CI для тестов в GitHub Actions добавлен 2026-11-04: `.github/workflows/tests.yml` выполняет restore, Release build и все тесты на `windows-latest` для push, pull request и ручного запуска с read-only правами на репозиторий и без секретов. Локально `dotnet build Yacplayer.sln -c Release` завершился с 0 warnings/0 errors, а `dotnet test Yacplayer.sln -c Release --no-build` прошёл 18/18 тестов; первый hosted-запуск Actions остаётся непроверенным до отправки коммита на GitHub.
- Последнее ревью репозитория/кода: 2026-11-03.
- Просмотрен очищенный `main` `5b27795` до добавления руководства.
- По коду проверены target/dependencies, data flow, 12 tests, publish profile, installer, двуязычные docs и tracked artifacts.
- Локальная ветка переведена на очищенную историю только после совпадения tree старой и новой вершины.
- Build/tests для этой документальной правки: `dotnet build Yacplayer.sln -c Release` завершился с 0 warnings/0 errors; `dotnet test Yacplayer.sln -c Release --no-build` прошёл 12/12 тестов 2026-11-03.
- Исправление независимости от выходной громкости 2026-11-03: gain громкости/mute перенесён после `PcmTapSampleProvider`, добавлен regression-тест pre-volume PCM; `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 13/13 тестов. Ручной WASAPI/TUI smoke громкости и mute не выполнялся.
- Store-identity MSIX успешно пересобран после исправления визуализации через Windows SDK `MakeAppx` 10.0.26100.9457; lookup NuGet vulnerability metadata выдал `NU1900`, но создание пакета завершилось успешно.
- Обновление версии 2026-11-03: project/assembly, CLI, installer, MSIX и документация синхронизированы на `1.26.3.11`; добавлены правило четырёхкомпонентной версии и regression-тест. Release build завершился с 0 warnings/0 errors, прошли 14/14 тестов, `--version` вывел `Yacplayer 1.26.3.11`, Store-identity пакет `Yacplayer-1.26.3.11-Store-win-x64.msix` создан успешно.
- Обновление цвета заголовков панелей 2026-11-03: текст на ярко-зелёном фоне изменён с чёрного на белый. `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 14/14 тестов, а Store-identity пакет `Yacplayer-1.26.3.11-Store-win-x64.msix` успешно пересобран. Ручная проверка TUI остаётся невыполненной.
- Обновление версии в заголовке 2026-11-03: справа в верхней рамке теперь выводится `vA.B.C.D`, полученная из запущенной assembly. Release build завершился с 0 warnings/0 errors, прошли 14/14 тестов, а Store-identity пакет `Yacplayer-1.26.3.11-Store-win-x64.msix` успешно пересобран. Ручная проверка TUI остаётся невыполненной.
- Обновление изображения README 2026-11-03: верхняя иконка в обеих языковых версиях заменена предоставленным пользователем PNG-скриншотом; целостность изображения и ссылки проверены без пересборки кода.
- Обновление installer-релиза 2026-11-03: framework-dependent win-x64 приложение заново опубликовано в `artifacts/publish/`, `Yacplayer.exe --version` подтвердил `1.26.3.11`, а `artifacts/installer/Yacplayer-1.26.3.11-win-x64-setup.exe` успешно собран Inno Setup 6.7.1. Текущий SHA-256 установщика: `6A9A00B852824252FD90FDDE60A74D7375AB1B388EB272B21CE8E09386EDC436`. Smoke-проверка установки/удаления остаётся невыполненной.
- Защита Unicode diff 2026-11-03: строки с вероятными wide/zero-width Unicode символами в текущем или предыдущем кадре очищаются и полностью перерисовываются, остальные строки сохраняют run-level diff; на время alternate-screen session отключён autowrap. Добавлен целевой regression-тест; `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 15/15 тестов. Ручной TUI smoke с mixed-width Unicode остаётся невыполненным.
- Обновление градиента спектра 2026-11-03: три резких цветовых порога заменены интерполированным ANSI true-color градиентом зелёный/жёлтый/красный, жёлтый верхний маркер сохранён; добавлена проверка true-color escape. Итоговая Release-сборка завершилась с 0 warnings/0 errors, прошли 16/16 тестов. Ручная визуальная проверка градиента и mixed-width TUI остаётся невыполненной.
- Общий list viewport 2026-11-04: file browser, основная queue и queue внутри browser теперь рисуют независимые движущиеся окна, удерживающие target selection/current item видимым с запасом в одну строку; порядок коллекций не меняется. Добавлены regression-тесты движения и clamp viewport; `dotnet build Yacplayer.sln -c Release --no-restore` завершился с 0 warnings/0 errors, `dotnet test Yacplayer.sln -c Release --no-build` прошёл 18/18 тестов. Installer и Store-identity MSIX успешно пересобраны; текущий SHA-256 MSIX: `8C4B16B234DE4F28702749EAFEBCF245535EB039FE8867CC6E3FF26A89D49DDE`. Ручной smoke длинной очереди и automatic EOF остаётся невыполненным.
- Ручной WASAPI/TUI/installer smoke для документальной правки не выполнялся.

- Подготовка Microsoft Store 2026-11-03: self-contained x64 MSIX успешно собран через Windows SDK `MakeAppx` 10.0.26100.8249; в layout проверены manifest, сгенерированные assets, execution alias, MIT license, third-party notices и privacy policy.
- После MSIX-изменений `dotnet build Yacplayer.sln -c Release --no-restore` завершился без ошибок с одним warning `NU1900` из-за недоступных NuGet vulnerability metadata; `dotnet test Yacplayer.sln -c Release --no-build` прошёл 12/12 тестов.
- Partner Center отклонил первую Store-сборку: переданный `Publisher` оказался обрезан, поэтому package family name был неверным. Полный Publisher со страницы Product identity вместе с `Name` и `PublisherDisplayName` применён через игнорируемый локальный override; `Yacplayer-1.0.0.0-Store-win-x64.msix` пересобран 2026-11-03. Windows `PackageFamilyNameFromId` подтвердил ожидаемый PFN `geckon01.YacPlayer_42xf8azg8zfh0`; credentials и signing material в отслеживаемые файлы не добавлялись.
- Загрузка/сертификация в Partner Center, Windows App Certification Kit, установка/запуск и WASAPI/TUI smoke остаются непроверенными: для них нужны внешняя отправка и/или ручная системная проверка.

## Checklist агента перед завершением

- [ ] Снова проверить относящиеся секции и реализацию.
- [ ] Сохранить несвязанные пользовательские tracked/untracked изменения.
- [ ] Добавить/обновить тесты и выполнить пропорциональную проверку.
- [ ] Обновить EN/RU пользовательские документы при изменении поведения.
- [ ] Обновить architecture/known issues при изменении дизайна или рисков.
- [ ] Обновить `AGENTS.md` и `AGENTS.ru.md`, включая verification record.
- [ ] Просмотреть `git diff`, cached diff и `git status --short`.
- [ ] Убедиться, что не staged media, secrets, local backups или случайные binaries.
- [ ] Сообщить изменения, проверки и непроверенные части.
