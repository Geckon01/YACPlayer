# Yacplayer

**Язык:** Русский · [English](README.md)

<https://github.com/user-attachments/assets/6b0c0f89-5e3f-4fda-b4a3-3f3359404995>

Yacplayer — локальный музыкальный плеер для Windows с полноэкранным терминальным интерфейсом. Он воспроизводит аудио через WASAPI shared mode, показывает очередь и двухпанельный файловый браузер, строит FFT-спектр или waveform из реально декодированного PCM и управляется с клавиатуры.

> [!IMPORTANT]
> **Уведомление об AI-generated code:** проект содержит код, созданный и изменённый при помощи искусственного интеллекта. Такой код может содержать дефекты, уязвимости и неверные предположения; перед production-использованием необходимы человеческое ревью и тестирование. Уведомление не изменяет лицензию MIT и отказ от гарантий.

## Текущее состояние

Работает:

- воспроизведение через NAudio 3.3 и `WasapiPlayer` в shared mode;
- play, pause/resume, stop, seek ±5 секунд, next/previous, auto-next;
- громкость 0–100% и mute;
- очередь, удаление без удаления файла, repeat Off/All/One, shuffle bag и автоматическая прокрутка viewport, удерживающая выбранный/текущий трек на экране;
- запуск с файлом или каталогом;
- встроенный Midnight Commander-подобный браузер: каталоги, `[..]`, список дисков `This PC`, добавление файла/каталога;
- отменяемая фоновая загрузка каталогов без остановки TUI, hash-дедупликация и пакетное добавление больших папок в очередь;
- адаптивный ANSI/VT-интерфейс с цветными панелями, белыми надписями на зелёном фоне заголовков, текущей версией справа в верхней рамке и diff-rendering с полной перерисовкой строк для Unicode переменной ширины;
- переключаемые FFT-спектр с Hann window, накоплением полного окна и плавным true-color градиентом зелёный/жёлтый/красный, waveform из `.` и отключённый режим визуализатора; амплитуда спектра и waveform не зависит от выходной громкости и mute;
- опциональная панель realtime audio analysis: многополосное обнаружение атак, отслеживаемые через PLL BPM/beat confidence и мигающий в ритм индикатор, а также RMS/peak/crest, zero-crossing rate, spectral centroid и dominant frequency; при отключении останавливаются PCM tap и вычисления;
- полноэкранное меню настроек: изменения сразу применяются и сохраняются в JSON профиля пользователя, включая настраиваемую стартовую папку;
- CLI, Release publish и установщик Inno Setup;
- воспроизводимая self-contained MSIX-сборка для Microsoft Store;
- встроенная многоразмерная Windows-иконка.

Текущие ограничения:

- ATL-метаданные лениво загружаются только для текущего трека; ещё не воспроизводившиеся элементы используют имя файла/`Unknown artist`, а bitrate не вычисляется;
- интерактивный поиск очереди, mouse input, OGG и Opus отсутствуют;
- WASAPI и реальные кодеки требуют ручной проверки на машине со звуковым устройством.
- BPM оценивается по realtime-сигналу, а не читается из metadata. Multi-hypothesis tracker требует нескольких секунд для захвата и всё ещё может выбрать другой метрический уровень для синкопированного, меняющегося или неоднозначного ритма.

## Системные требования

- Windows 10 build 17763 или новее, Windows 11;
- архитектура x64;
- [.NET 10 Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0/runtime) для framework-dependent сборки;
- Windows Terminal рекомендуется из-за поддержки VT, Unicode и ANSI-цветов;
- .NET 10 SDK требуется только для сборки из исходников.

## Быстрый старт

### Установщик

Скачайте последнуюю версию установщика из релизов. Запустите и установите как обычное приложение.
Установка выполняется для текущего пользователя в `%LOCALAPPDATA%\Programs\Yacplayer`, не требует прав администратора и может создать ярлык на рабочем столе.

### Запуск опубликованной версии

```powershell
.\artifacts\publish\Yacplayer.exe
.\artifacts\publish\Yacplayer.exe "C:\Music\track.mp3"
.\artifacts\publish\Yacplayer.exe "C:\Music" --recursive
```

Без аргументов файловый браузер открывается в заданной стартовой папке, последнем доступном каталоге браузера либо текущей рабочей папке — в таком порядке.

### Запуск из исходников

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

| Параметр | Назначение |
|---|---|
| `path` | Один аудиофайл или каталог |
| `-r`, `--recursive` | Рекурсивно сканировать каталог; reparse points пропускаются |
| `--fps 30\|60` | Частота UI; другие значения отклоняются |
| `--no-visualizer` | Отключить спектр на текущий запуск |
| `-h`, `--help` | Показать справку без инициализации аудио/TUI |
| `--version` | Показать версию без инициализации аудио/TUI |

Коды выхода:

- `0` — нормальный выход;
- `2` — неизвестный или некорректный аргумент;
- `3` — отсутствующий стартовый путь или критическая ошибка запуска.

Если stdin/stdout перенаправлены, интерактивный TUI не запускается: программа сообщает число загруженных треков и завершает работу.

## Управление плеером

| Клавиша | Действие |
|---|---|
| `Space` | Pause/resume |
| `X` | Stop и сброс позиции |
| `N` / `P` | Следующий / предыдущий трек |
| `Left` / `Right` | Seek −5 / +5 секунд |
| `+` / `-` | Громкость +5 / −5 процентных пунктов |
| `M` | Mute/unmute |
| `Up` / `Down` | Выбор элемента очереди |
| `Enter` | Воспроизвести выбранный элемент очереди |
| `Delete` | Удалить элемент только из очереди |
| `S` | Shuffle on/off |
| `R` | Repeat Off → All → One |
| `V` | Spectrum → Waveform → Off; заголовок панели отражает активный режим |
| `B` | Включить/выключить панель realtime audio analysis вместе с вычислениями |
| `F` | Открыть файловый браузер |
| `O` | Открыть полноэкранное меню настроек |
| `?` | Открыть встроенную справку |
| `Q` | Открыть диалог подтверждения выхода `Да` / `Нет` |

## Управление файловым браузером

Левая панель содержит каталоги и поддерживаемые аудиофайлы, правая — текущую очередь. Каталоги сортируются первыми, затем имена сортируются без учёта регистра.

| Клавиша | Действие |
|---|---|
| `Up` / `Down` | Переместить выбор |
| `Enter` | Войти в каталог; для файла — добавить его при необходимости, запустить и закрыть браузер |
| `Backspace` | Перейти в родительский каталог |
| `Enter` на `[..]` | Перейти в родительский каталог |
| `A` | Добавить выбранный файл или содержимое каталога без запуска |
| `O` | Открыть полноэкранное меню настроек |
| `F` / `Esc` | Закрыть браузер |
| `Q` | Открыть диалог подтверждения выхода `Да` / `Нет` |

При выходе выше корня диска показывается виртуальный список `This PC` со всеми готовыми дисками. Недоступные каталоги не завершают приложение и отображаются как пустые.

## Форматы

| Формат | Текущий статус |
|---|---|
| WAV PCM | Поддерживаемый базовый формат |
| MP3 | Поддерживается на стандартной Windows через NAudio/Media Foundation |
| M4A, AAC | Зависит от доступного Media Foundation codec |
| WMA | Зависит от системного декодера |
| FLAC | Best effort через системные возможности Windows |
| OGG, Opus | Не поддерживаются текущей версией |

Расширение файла используется только для показа в браузере. Реальная возможность воспроизведения определяется декодером; ошибка кодека обрабатывается без падения приложения. FFmpeg и внешние процессы не используются.

## Конфигурация

Файл: `%LOCALAPPDATA%\Yacplayer\settings.json`.

```json
{
  "Version": 1,
  "Volume": 75,
  "Muted": false,
  "Fps": 30,
  "FftSize": 2048,
  "VisualizerMode": "Spectrum",
  "RealtimeAnalysisEnabled": true,
  "RepeatMode": "Off",
  "Shuffle": false,
  "Recursive": false,
  "LastDirectory": "",
  "StartupDirectory": ""
}
```

Нажмите `O`, чтобы редактировать все сохраняемые параметры в полноэкранном меню. Up/Down выбирают строку, Left/Right (или `+`/`-`) меняют значение, Enter редактирует пути. Корректные изменения сразу влияют на работающий плеер и записываются на диск. `StartupDirectory` — фиксированная папка для запуска без пути; пустое значение включает fallback на `LastDirectory`. Изменение `LastDirectory` также сразу открывает эту папку в активном браузере.

Проверяются диапазон громкости, FPS (`30`/`60`), FFT size (`2048`/`4096`), enum-значения и существование обоих каталогов. Повреждённый JSON перемещается в `settings.json.bad`, затем используются значения по умолчанию. Сохранение выполняется через `settings.json.tmp` и замену целевого файла. CLI-флаги имеют приоритет в текущем запуске.

## Сборка и тесты

```powershell
dotnet restore Yacplayer.sln
dotnet build Yacplayer.sln -c Release
dotnet test Yacplayer.sln -c Release
```

GitHub Actions выполняет ту же последовательность restore, Release-сборки и тестов на Windows для каждого push и pull request.

Текущий набор содержит 40 unit-тестов без требования аудиоустройства: CLI, четырёхкомпонентная assembly version, playlist и восстановление repeat/shuffle, включая single-track Repeat All, batch insert внутри активного shuffle-cycle, bounded audio-event delivery, прокрутка/clamp list viewport, ring buffer wrapping/latest/exact reads, независимо отключаемый pre-volume PCM tap, realtime level/spectrum/tempo analysis с накоплением неполных FFT-окон, рендеринг player/browser/settings, взаимодействие с настройками и их сохранение, дедупликация в browser controller, ANSI/true-color diff styles, batching и обновление Unicode-строк, перенастройка FFT, metadata fallback и отменяемая browser filtering/sorting.

Публикация framework-dependent win-x64:

```powershell
dotnet publish src/Yacplayer/Yacplayer.csproj `
  -c Release -r win-x64 --self-contained false `
  -o artifacts/publish
```

Native AOT publish для win-x64 (self-contained, установка .NET runtime не требуется):

```powershell
dotnet publish src/Yacplayer/Yacplayer.csproj `
  -p:PublishProfile=NativeAotWinX64
```

Результат: `artifacts/publish-aot/Yacplayer.exe`. Для Native AOT необходим нативный Windows toolchain из Visual Studio Build Tools (workload Desktop development with C++). AOT-профиль остаётся Windows-only, потому что аудиовывод использует WASAPI.

Сборка MSIX для Microsoft Store:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File packaging\msix\Build-Msix.ps1
```

Настройка identity из Partner Center, проверка и правила хранения секретов описаны в [инструкции Microsoft Store](docs/MICROSOFT_STORE.md#русская-памятка).

Конфиденциальность: [политика Yacplayer](PRIVACY.md#русская-версия). Приложение работает локально и не собирает и не передаёт персональные данные.

Сборка установщика Inno Setup 6:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\Yacplayer.iss
```

Результат: `artifacts\installer\Yacplayer-1.26.5.11-win-x64-setup.exe`.

## Структура репозитория

```text
src/Yacplayer/                  приложение
  Audio/                       NAudio, WASAPI, PCM tap, ring buffer
  Application/                 browser, metadata и visualization coordinators
  Cli/                         разбор аргументов и help
  Configuration/               модель и JSON-хранилище настроек
  Dsp/                         FFT и логарифмические полосы
  Library/                     очередь, browser, metadata model/service
  Presentation/                player/browser terminal layouts
  Terminal/                    alternate screen, cell buffer, ANSI diff
tests/Yacplayer.Tests/          xUnit-тесты чистой логики
assets/                         PNG и многоразмерная ICO-иконка
installer/Yacplayer.iss         Inno Setup 6
docs/ARCHITECTURE.md            архитектура на английском
docs/ARCHITECTURE.ru.md         архитектура на русском
artifacts/publish/              framework-dependent publish
artifacts/installer/            собранный setup
```

Подробная инженерная документация: [Русский](docs/ARCHITECTURE.ru.md) · [English](docs/ARCHITECTURE.md).

## Частые вопросы (FAQ)

- **Почему Yacplayer доступен только для Windows?** Сейчас приложение использует target `net10.0-windows`, а звук выводится через NAudio и WASAPI. Для Linux и macOS нужны отдельные протестированные аудиобэкенды, интеграция с файловой системой и конфигурацией, проверка кодеков и поведения терминала. Простая cross-компиляция текущего Windows host не даст рабочего и поддерживаемого порта.
- **Почему экран пустой или цвета выглядят неправильно?** Используйте Windows Terminal, UTF-8 и шрифт с символом `█`.
- **Почему нет звука?** Проверьте default output device; Yacplayer использует WASAPI shared mode и latency 100 ms.
- **Почему видимый файл не воспроизводится?** Наличие расширения в browser allowlist не гарантирует наличие системного codec.
- **Почему Explorer показывает старую иконку EXE?** Windows может кэшировать значки; переименуйте файл или перезапустите Explorer.
- **Почему не загружаются настройки?** Проверьте `settings.json.bad` рядом с конфигурацией.
- **Почему терминал остался в alternate screen?** Подтверждение `Да` после `Q` и Ctrl+C вызывают cleanup; после аварийного завершения выполните сброс терминала или откройте новую вкладку.

Профилирование возможно через `dotnet-counters` и `dotnet-trace`; runtime-зависимостями проекта они не являются.

## Лицензии

Собственный код распространяется по MIT: юридически канонический [английский текст](LICENSE) и [неофициальный русский перевод](LICENSE.ru.md). Уведомления о сторонних компонентах: [Русский](THIRD_PARTY_NOTICES.ru.md) · [English](THIRD_PARTY_NOTICES.md).
