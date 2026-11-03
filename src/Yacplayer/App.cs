using System.Collections.Concurrent;
using Yacplayer.Audio;
using Yacplayer.Cli;
using Yacplayer.Configuration;
using Yacplayer.Dsp;
using Yacplayer.Library;
using Yacplayer.Terminal;

namespace Yacplayer;

public sealed class App(Arguments arguments)
{
    private static readonly string VersionLabel = $" v{typeof(App).Assembly.GetName().Version?.ToString(4) ?? "0.0.0.0"} ";
    private readonly Playlist _playlist = new();
    private readonly SampleRingBuffer _ring = new(32768);
    private readonly SettingsStore _store = new();
    private PlayerSettings _settings = new();
    private string _message = "Ready";
    private bool _help;
    private bool _browserVisible;
    private string _browserPath = Environment.CurrentDirectory;
    private IReadOnlyList<BrowserEntry> _browserEntries = [];
    private int _browserSelected;
    private readonly ListViewport _browserViewport = new();
    private readonly ListViewport _queueViewport = new();
    private readonly ListViewport _browserQueueViewport = new();
    private bool _browserAtDrives;
    private readonly ConcurrentQueue<MetadataCompletion> _metadataCompletions = new();
    private TrackEntry? _metadataTarget;
    private CancellationTokenSource? _metadataCancellation;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        _settings = await _store.LoadAsync(ct);
        if (arguments.Fps is int fps) _settings.Fps = fps;
        if (arguments.NoVisualizer) _settings.VisualizerMode = VisualizerMode.Off;
        if (arguments.Recursive) _settings.Recursive = true;
        var start = arguments.Path ?? (!string.IsNullOrEmpty(_settings.LastDirectory) ? _settings.LastDirectory : Environment.CurrentDirectory);
        if (Directory.Exists(start)) _settings.LastDirectory = start;
        var files = arguments.Path is null ? [] : await Task.Run(() => FileBrowser.Scan(start, _settings.Recursive).ToArray(), ct);
        _playlist.AddRange(files.Select(path => new TrackEntry(path)));
        _browserPath = Directory.Exists(start) ? start : Path.GetDirectoryName(start) ?? Environment.CurrentDirectory;
        _browserVisible = arguments.Path is null;
        if (_browserVisible) await LoadBrowserAsync(_browserPath, ct);

        using var audio = new AudioEngine(_ring);
        var analyzer = new SpectrumAnalyzer(_ring, _settings.FftSize);
        audio.SetVolume(_settings.Volume);
        if (_settings.Muted) audio.ToggleMute();
        audio.Error += value => _message = value;
        audio.Ended += () => { var next = _playlist.Next(true); if (next is not null) audio.Open(next.Path); else _message = "Queue finished"; };
        if (File.Exists(start) && _playlist.Items.Count > 0) { _playlist.Play(0); audio.Open(start); }
        if (Console.IsOutputRedirected || Console.IsInputRedirected) { Console.WriteLine($"Yacplayer: {_playlist.Items.Count} track(s) loaded. Interactive TUI requires a terminal."); return 0; }

        using var terminal = new TerminalSession();
        ScreenBuffer? previous = null;
        var bands = new float[64];
        var rawBands = new float[64];
        var waveform = new float[4096];
        var waveformCount = 0;
        var activeVisualizerMode = _settings.VisualizerMode;
        while (!ct.IsCancellationRequested)
        {
            while (Console.KeyAvailable) if (!await HandleAsync(Console.ReadKey(true), audio, ct)) goto done;
            int width, height;
            try { width = Math.Max(20, Console.WindowWidth); height = Math.Max(8, Console.WindowHeight); } catch { break; }
            EnsureCurrentMetadata(ct);
            ApplyMetadataCompletions();
            var snapshot = audio.Snapshot;
            if (activeVisualizerMode != _settings.VisualizerMode)
            {
                activeVisualizerMode = _settings.VisualizerMode;
                _ring.Reset();
                Array.Clear(rawBands);
                Array.Clear(bands);
                waveformCount = 0;
            }
            if (_settings.VisualizerMode == VisualizerMode.Spectrum)
            {
                analyzer.Analyze(snapshot.SampleRate <= 0 ? 48000 : snapshot.SampleRate, rawBands);
                for (var i = 0; i < bands.Length; i++)
                {
                    var smoothing = rawBands[i] > bands[i] ? 0.55f : 0.16f;
                    bands[i] += (rawBands[i] - bands[i]) * smoothing;
                    if (bands[i] < 0.002f) bands[i] = 0;
                }
            }
            else if (_settings.VisualizerMode == VisualizerMode.Waveform) waveformCount = _ring.ReadLatest(waveform);
            else waveformCount = 0;
            var screen = Draw(width, height, snapshot, bands, waveform, waveformCount);
            terminal.Render(screen.Diff(previous)); previous = screen;
            try { await Task.Delay(1000 / _settings.Fps, ct); } catch (OperationCanceledException) { break; }
        }
    done:
        _metadataCancellation?.Cancel();
        _metadataCancellation?.Dispose();
        audio.Stop();
        _settings.Volume = audio.Snapshot.Volume; _settings.Muted = audio.Snapshot.Muted;
        _settings.RepeatMode = _playlist.Repeat; _settings.Shuffle = _playlist.Shuffle;
        try { await _store.SaveAsync(_settings, CancellationToken.None); } catch (IOException) { }
        return 0;
    }

    private async Task<bool> HandleAsync(ConsoleKeyInfo key, AudioEngine audio, CancellationToken ct)
    {
        if (_help) { if (key.Key is ConsoleKey.Escape or ConsoleKey.Oem2) _help = false; return true; }
        if (_browserVisible)
        {
            switch (key.Key)
            {
                case ConsoleKey.Q: return false;
                case ConsoleKey.F: case ConsoleKey.Escape: _browserVisible = false; return true;
                case ConsoleKey.UpArrow: _browserSelected = Math.Max(0, _browserSelected - 1); return true;
                case ConsoleKey.DownArrow: _browserSelected = Math.Min(_browserEntries.Count, _browserSelected + 1); return true;
                case ConsoleKey.Backspace: await GoToParentAsync(ct); return true;
                case ConsoleKey.Enter: await OpenBrowserSelectionAsync(audio, ct); return true;
                case ConsoleKey.A: await AppendBrowserSelectionAsync(ct); return true;
            }
            return true;
        }
        switch (key.Key)
        {
            case ConsoleKey.Q: return false;
            case ConsoleKey.Spacebar: audio.TogglePause(); break;
            case ConsoleKey.X: audio.Stop(); break;
            case ConsoleKey.N: Play(_playlist.Next(), audio); break;
            case ConsoleKey.P: Play(_playlist.Previous(), audio); break;
            case ConsoleKey.LeftArrow: audio.Seek(-5); break;
            case ConsoleKey.RightArrow: audio.Seek(5); break;
            case ConsoleKey.UpArrow: _playlist.Select(_playlist.SelectedIndex - 1); break;
            case ConsoleKey.DownArrow: _playlist.Select(_playlist.SelectedIndex + 1); break;
            case ConsoleKey.Enter: Play(_playlist.Play(_playlist.SelectedIndex), audio); break;
            case ConsoleKey.Delete:
                var current = _playlist.Remove(_playlist.SelectedIndex);
                if (current is null) audio.Stop();
                else if (audio.Snapshot.Path is not null && !string.Equals(current.Path, audio.Snapshot.Path, StringComparison.OrdinalIgnoreCase)) audio.Open(current.Path);
                break;
            case ConsoleKey.M: audio.ToggleMute(); break;
            case ConsoleKey.Add: case ConsoleKey.OemPlus: audio.SetVolume(audio.Snapshot.Volume + 5); break;
            case ConsoleKey.Subtract: case ConsoleKey.OemMinus: audio.SetVolume(audio.Snapshot.Volume - 5); break;
            case ConsoleKey.S: _playlist.ToggleShuffle(); _message = $"Shuffle {(_playlist.Shuffle ? "on" : "off")}"; break;
            case ConsoleKey.R: _playlist.SetRepeat((RepeatMode)(((int)_playlist.Repeat + 1) % 3)); _message = $"Repeat {_playlist.Repeat}"; break;
            case ConsoleKey.V: _settings.VisualizerMode = (VisualizerMode)(((int)_settings.VisualizerMode + 1) % 3); break;
            case ConsoleKey.H: _settings.FullscreenVisualizer = !_settings.FullscreenVisualizer; break;
            case ConsoleKey.F: _browserVisible = true; await LoadBrowserAsync(_browserPath, ct); break;
            case ConsoleKey.Oem2 when key.KeyChar == '?': _help = true; break;
        }
        return true;
    }

    private static void Play(TrackEntry? track, AudioEngine audio) { if (track is not null) audio.Open(track.Path); }

    private void EnsureCurrentMetadata(CancellationToken appCancellation)
    {
        var current = _playlist.CurrentIndex is int index ? _playlist.Items[index] : null;
        if (ReferenceEquals(current, _metadataTarget)) return;

        _metadataCancellation?.Cancel();
        _metadataCancellation?.Dispose();
        _metadataCancellation = null;
        _metadataTarget = current;

        if (current is null || current.MetadataState != MetadataState.Pending) return;

        _metadataCancellation = CancellationTokenSource.CreateLinkedTokenSource(appCancellation);
        _ = LoadCurrentMetadataAsync(current, _metadataCancellation.Token);
    }

    private async Task LoadCurrentMetadataAsync(TrackEntry entry, CancellationToken ct)
    {
        try
        {
            var metadata = await MetadataService.LoadAsync(entry.Path, ct);
            if (!ct.IsCancellationRequested) _metadataCompletions.Enqueue(new(entry, metadata, null));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) _metadataCompletions.Enqueue(new(entry, null, ex.Message)); }
    }

    private void ApplyMetadataCompletions()
    {
        while (_metadataCompletions.TryDequeue(out var completion))
        {
            if (!ReferenceEquals(completion.Entry, _metadataTarget)) continue;
            if (completion.Metadata is null)
            {
                completion.Entry.MetadataState = MetadataState.Failed;
                _message = $"Cannot read metadata for {Path.GetFileName(completion.Entry.Path)}: {completion.Error}";
                continue;
            }

            completion.Entry.Title = completion.Metadata.Title;
            completion.Entry.Artist = completion.Metadata.Artist;
            completion.Entry.Album = completion.Metadata.Album;
            completion.Entry.Duration = completion.Metadata.Duration;
            completion.Entry.MetadataState = MetadataState.Loaded;
        }
    }

    private async Task LoadBrowserAsync(string path, CancellationToken ct)
    {
        _message = $"Loading {path}";
        var entries = await FileBrowser.EnumerateAsync(path, ct);
        _browserPath = path; _browserEntries = entries; _browserSelected = 0; _browserViewport.Reset(); _browserAtDrives = false;
        _settings.LastDirectory = path;
        _message = entries.Count == 0 ? "No audio files or subdirectories (use [..] to go back)" : $"{entries.Count} item(s)";
    }

    private async Task GoToParentAsync(CancellationToken ct)
    {
        var parent = Directory.GetParent(_browserPath);
        if (_browserAtDrives) return;
        if (parent is not null) await LoadBrowserAsync(parent.FullName, ct);
        else ShowDrives();
    }

    private void ShowDrives()
    {
        _browserAtDrives = true; _browserPath = "This PC"; _browserSelected = 0; _browserViewport.Reset();
        _browserEntries = DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(drive => new BrowserEntry(drive.RootDirectory.FullName, true)).ToArray();
        _message = $"{_browserEntries.Count} drive(s)";
    }

    private async Task OpenBrowserSelectionAsync(AudioEngine audio, CancellationToken ct)
    {
        if (_browserSelected == 0) { await GoToParentAsync(ct); return; }
        var entryIndex = _browserSelected - 1;
        if ((uint)entryIndex >= (uint)_browserEntries.Count) return;
        var entry = _browserEntries[entryIndex];
        if (entry.IsDirectory) { await LoadBrowserAsync(entry.Path, ct); return; }
        var index = FindOrAddTrack(entry.Path);
        Play(_playlist.Play(index), audio);
        _browserVisible = false;
        _message = $"Playing {Path.GetFileName(entry.Path)}";
    }

    private async Task AppendBrowserSelectionAsync(CancellationToken ct)
    {
        if (_browserSelected == 0) { _message = "Parent entry cannot be added"; return; }
        var entryIndex = _browserSelected - 1;
        if ((uint)entryIndex >= (uint)_browserEntries.Count) return;
        var entry = _browserEntries[entryIndex];
        var paths = entry.IsDirectory
            ? await Task.Run(() => FileBrowser.Scan(entry.Path, _settings.Recursive).ToArray(), ct)
            : [entry.Path];
        var added = 0;
        foreach (var path in paths) if (!_playlist.Items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) { _playlist.Add(new TrackEntry(path)); added++; }
        _message = $"Added {added} track(s)";
    }

    private int FindOrAddTrack(string path)
    {
        for (var i = 0; i < _playlist.Items.Count; i++) if (string.Equals(_playlist.Items[i].Path, path, StringComparison.OrdinalIgnoreCase)) return i;
        _playlist.Add(new TrackEntry(path)); return _playlist.Items.Count - 1;
    }

    private ScreenBuffer Draw(int width, int height, PlaybackSnapshot playback, float[] bands, float[] waveform, int waveformCount)
    {
        var screen = new ScreenBuffer(width, height);
        DrawBox(screen, 0, 0, width - 1, 2, " Yacplayer ", CellColor.Green);
        if (width >= VersionLabel.Length + 16)
            screen.Write(width - VersionLabel.Length - 2, 0, VersionLabel, CellColor.White, CellBackground.BrightGreen, bold: true);
        var state = playback.State == PlaybackState.Playing ? "> PLAYING" : playback.State.ToString().ToUpperInvariant();
        var volumeBlocks = Math.Clamp(playback.Volume / 10, 0, 10);
        var header = $"{state}  |  Vol {playback.Volume,3}% {new string('█', volumeBlocks)}{new string('.', 10 - volumeBlocks)}  |  Repeat: {_playlist.Repeat}  |  Shuffle: {(_playlist.Shuffle ? "On" : "Off")}  |  Output: WASAPI";
        screen.Write(14, 1, Fit(header, Math.Max(1, width - 16)), CellColor.White, bold: true);
        if (_browserVisible)
        {
            DrawBrowser(screen, width, height);
            return screen;
        }

        var current = _playlist.CurrentIndex is int index ? _playlist.Items[index] : null;

        if (width < 90 || height < 22)
        {
            DrawBox(screen, 0, 3, width - 1, 7, " Now Playing ", CellColor.Green);
            screen.Write(2, 4, Fit(current is null ? "No track selected" : current.Title, width - 4), CellColor.White, bold: true);
            screen.Write(2, 5, Fit(current?.Artist ?? "Select a track with F", width - 4), CellColor.Green);
            DrawProgress(screen, 2, 6, width - 4, playback);
            DrawBox(screen, 0, 8, width - 1, height - 2, VisualizerTitle(), CellColor.Green);
            DrawVisualizer(screen, 1, 10, width - 2, Math.Max(1, height - 12), bands, waveform, waveformCount);
        }
        else
        {
            var queueLeft = Math.Max(68, width * 72 / 100);
            var nowBottom = 9;
            var infoTop = height - 9;
            DrawBox(screen, 0, 3, queueLeft - 1, nowBottom, " Now Playing ", CellColor.Green);
            screen.Write(3, 5, Fit(current is null ? "No track selected" : current.Title, queueLeft - 6), CellColor.White, bold: true);
            screen.Write(3, 6, Fit(current?.Artist ?? "Open the file browser with F", queueLeft - 6), CellColor.Green);
            screen.Write(3, 7, $"{Path.GetExtension(playback.Path ?? "").TrimStart('.').ToUpperInvariant(),-6} | {playback.SampleRate / 1000d:0.0} kHz | {playback.Channels} ch | {playback.Duration:mm\\:ss}");
            DrawProgress(screen, 3, 8, queueLeft - 6, playback);

            DrawBox(screen, queueLeft, 3, width - 1, height - 2, $" Queue ({_playlist.Items.Count}) ", CellColor.Green);
            var queueRows = Math.Max(1, height - 8);
            _queueViewport.EnsureVisible(_playlist.SelectedIndex, _playlist.Items.Count, queueRows);
            for (var row = 0; row < queueRows; row++)
            {
                var itemIndex = _queueViewport.Offset + row;
                if (itemIndex >= _playlist.Items.Count) break;
                var item = _playlist.Items[itemIndex]; var active = itemIndex == _playlist.CurrentIndex; var selected = itemIndex == _playlist.SelectedIndex;
                var text = $"{(active ? '>' : ' ')} {itemIndex + 1,2}. {item.Title}";
                screen.Write(queueLeft + 2, 5 + row, Fit(text, width - queueLeft - 4), active ? CellColor.Black : selected ? CellColor.Yellow : CellColor.Default, active ? CellBackground.Green : CellBackground.Default, active);
            }

            DrawBox(screen, 0, 10, queueLeft - 1, infoTop - 1, VisualizerTitle(), CellColor.Green);
            if (_settings.VisualizerMode == VisualizerMode.Spectrum) screen.Write(Math.Max(2, queueLeft - 33), 11, $"40 Hz - 18 kHz | {_settings.FftSize} bins", CellColor.Cyan);
            else if (_settings.VisualizerMode == VisualizerMode.Waveform) screen.Write(Math.Max(2, queueLeft - 31), 11, $"Realtime PCM | {playback.SampleRate:N0} Hz", CellColor.Cyan);
            DrawVisualizer(screen, 1, 12, queueLeft - 2, Math.Max(1, infoTop - 12), bands, waveform, waveformCount);

            var middle = queueLeft / 2;
            DrawBox(screen, 0, infoTop, middle - 1, height - 2, " Playback ", CellColor.Green);
            screen.Write(3, infoTop + 2, $"Volume   : {playback.Volume}% {new string('█', Math.Clamp(playback.Volume / 10, 0, 10))}", CellColor.Green);
            screen.Write(3, infoTop + 3, $"Repeat   : {_playlist.Repeat}");
            screen.Write(3, infoTop + 4, $"Shuffle  : {(_playlist.Shuffle ? "On" : "Off")}");
            screen.Write(3, infoTop + 5, $"Output   : WASAPI shared");
            DrawBox(screen, middle, infoTop, queueLeft - 1, height - 2, " Audio Info ", CellColor.Green);
            screen.Write(middle + 3, infoTop + 2, $"Format      : {Path.GetExtension(playback.Path ?? "").TrimStart('.').ToUpperInvariant()}", CellColor.Green);
            screen.Write(middle + 3, infoTop + 3, $"Sample rate : {playback.SampleRate:N0} Hz", CellColor.Green);
            screen.Write(middle + 3, infoTop + 4, $"Channels    : {playback.Channels}", CellColor.Green);
            screen.Write(middle + 3, infoTop + 5, $"Duration    : {playback.Duration:mm\\:ss}", CellColor.Green);
        }

        var footer = _help ? "Esc Close help" : "Hotkeys: [Space] Play/Pause | [Left/Right] Seek | [Up/Down] Select | [F] Files | [S] Shuffle | [R] Repeat | [Q] Quit | [?] Help";
        screen.Write(0, height - 1, Fit(footer, width), CellColor.Green, bold: true);
        if (_help)
        {
            for (var y = 2; y < height - 2; y++) screen.Write(2, y, new string(' ', Math.Max(0, width - 4)));
            screen.Write(4, 3, "KEYS: Space pause  X stop  N/P next/prev  Left/Right seek");
            screen.Write(4, 4, "+/- volume  M mute  Enter play  Del remove  S shuffle");
            screen.Write(4, 5, "R repeat  V visualizer  H fullscreen  Q quit  Esc close");
        }
        return screen;
    }

    private static void DrawProgress(ScreenBuffer screen, int x, int y, int width, PlaybackSnapshot playback)
    {
        var duration = playback.Duration.TotalSeconds;
        var ratio = duration <= 0 ? 0 : Math.Clamp(playback.Position.TotalSeconds / duration, 0, 1);
        var barWidth = Math.Max(1, width - 13); var filled = Math.Clamp((int)(ratio * barWidth), 0, barWidth);
        screen.Write(x, y, $"{playback.Position:mm\\:ss} ");
        screen.Write(x + 6, y, new string('█', filled), CellColor.Green);
        screen.Write(x + 6 + filled, y, new string('·', barWidth - filled));
        screen.Write(x + width - 5, y, $"{playback.Duration:mm\\:ss}");
    }

    private void DrawSpectrum(ScreenBuffer screen, int left, int top, int width, int height, float[] bands)
    {
        if (_settings.VisualizerMode != VisualizerMode.Spectrum || width <= 0 || height <= 0) return;
        var bottom = top + height - 1;
        for (var x = 0; x < width; x++)
        {
            var position = x * (bands.Length - 1d) / Math.Max(1, width - 1); var band = (int)position;
            var next = Math.Min(bands.Length - 1, band + 1); var amplitude = bands[band] * (1 - (position - band)) + bands[next] * (position - band);
            var barHeight = Math.Clamp((int)Math.Round(amplitude * height), 0, height);
            for (var level = 0; level < barHeight; level++)
            {
                var ratio = level / (double)Math.Max(1, height - 1);
                screen.Write(left + x, bottom - level, "█", SpectrumGradient(ratio));
            }
            if (barHeight > 0) screen.Write(left + x, Math.Max(top, bottom - barHeight), "-", CellColor.Yellow, bold: true);
        }
    }

    private static CellColor SpectrumGradient(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        if (ratio <= 0.65)
        {
            var position = ratio / 0.65;
            return CellColor.Rgb((int)Math.Round(230 * position), 190, 0);
        }

        var upperPosition = (ratio - 0.65) / 0.35;
        return CellColor.Rgb(230, (int)Math.Round(190 - 145 * upperPosition), 0);
    }

    private string VisualizerTitle() => _settings.VisualizerMode switch
    {
        VisualizerMode.Spectrum => " Spectrum (FFT) ",
        VisualizerMode.Waveform => " Waveform ",
        _ => " Visualizer: Off "
    };

    private void DrawVisualizer(ScreenBuffer screen, int left, int top, int width, int height, float[] bands, float[] waveform, int waveformCount)
    {
        if (_settings.VisualizerMode == VisualizerMode.Spectrum) DrawSpectrum(screen, left, top, width, height, bands);
        else if (_settings.VisualizerMode == VisualizerMode.Waveform) DrawWaveform(screen, left, top, width, height, waveform, waveformCount);
    }

    private static void DrawWaveform(ScreenBuffer screen, int left, int top, int width, int height, float[] samples, int count)
    {
        if (width <= 0 || height <= 0 || count <= 0) return;
        double peak = 0;
        for (var i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
        if (peak < 0.0001) return;

        var center = top + (height - 1) / 2d;
        var scale = Math.Max(0, height - 1) / 2d;
        var gain = 0.9 / peak;
        for (var x = 0; x < width; x++)
        {
            var from = x * count / width;
            var to = Math.Max(from + 1, (x + 1) * count / width);
            float minimum = samples[from], maximum = samples[from];
            for (var i = from + 1; i < Math.Min(count, to); i++)
            {
                minimum = Math.Min(minimum, samples[i]);
                maximum = Math.Max(maximum, samples[i]);
            }

            var upper = (int)Math.Round(center - Math.Clamp(maximum * gain, -1, 1) * scale);
            var lower = (int)Math.Round(center - Math.Clamp(minimum * gain, -1, 1) * scale);
            upper = Math.Clamp(upper, top, top + height - 1);
            lower = Math.Clamp(lower, top, top + height - 1);
            for (var y = Math.Min(upper, lower); y <= Math.Max(upper, lower); y++) screen.Write(left + x, y, ".", CellColor.Green, bold: true);
        }
    }

    private void DrawBrowser(ScreenBuffer screen, int width, int height)
    {
        var top = 3; var bottom = height - 3;
        var left = 0; var split = width >= 70 ? width * 2 / 3 : width; var right = width - 1;
        DrawBox(screen, left, top, split - 1, bottom, " FILES ");
        if (split < width) DrawBox(screen, split, top, right, bottom, " QUEUE ");
        screen.Write(2, top + 1, Fit(_browserPath, Math.Max(1, split - 5)), CellColor.Yellow);
        screen.Write(2, top + 2, "Name", CellColor.Cyan);
        screen.Write(Math.Max(2, split - 12), top + 2, "Type", CellColor.Cyan);
        var listTop = top + 3; var rows = Math.Max(1, bottom - listTop);
        _browserViewport.EnsureVisible(_browserSelected, _browserEntries.Count + 1, rows);
        for (var row = 0; row < rows; row++)
        {
            var index = row + _browserViewport.Offset;
            if (index > _browserEntries.Count) break;
            var selected = index == _browserSelected;
            var color = selected ? CellColor.Yellow : CellColor.Default;
            if (index == 0)
            {
                var parentLabel = _browserAtDrives ? "[This PC]" : "[..]";
                screen.Write(2, listTop + row, $"{(selected ? '>' : ' ')} {parentLabel}", color);
                screen.Write(Math.Max(2, split - 12), listTop + row, _browserAtDrives ? "ROOT" : "PARENT", color);
                continue;
            }
            var entry = _browserEntries[index - 1];
            var nameWidth = Math.Max(5, split - 16);
            var entryName = _browserAtDrives ? entry.Path : Path.GetFileName(entry.Path);
            screen.Write(2, listTop + row, $"{(selected ? '>' : ' ')} {Fit(entryName, nameWidth)}", color);
            screen.Write(Math.Max(2, split - 12), listTop + row, entry.IsDirectory ? "DIR" : Path.GetExtension(entry.Path).TrimStart('.').ToUpperInvariant(), entry.IsDirectory ? CellColor.Cyan : color);
        }
        if (split < width)
        {
            var queueWidth = Math.Max(1, width - split - 4); var queueRows = Math.Max(1, bottom - top - 2);
            var queueTarget = _playlist.CurrentIndex ?? _playlist.SelectedIndex;
            _browserQueueViewport.EnsureVisible(queueTarget, _playlist.Items.Count, queueRows);
            for (var row = 0; row < queueRows; row++)
            {
                var index = _browserQueueViewport.Offset + row;
                if (index >= _playlist.Items.Count) break;
                var marker = index == _playlist.CurrentIndex ? "*" : " ";
                screen.Write(split + 2, top + 2 + row, Fit($"{marker} {index + 1,3} {_playlist.Items[index].Title}", queueWidth), index == _playlist.CurrentIndex ? CellColor.Green : CellColor.Default);
            }
            if (_playlist.Items.Count == 0) screen.Write(split + 2, top + 2, "Queue is empty");
        }
        screen.Write(1, height - 2, Fit(" Enter Open/Play   Backspace Parent   A Add   F/Esc Close   Q Quit", width - 2), CellColor.Cyan);
        screen.Write(1, height - 1, Fit(_message, width - 2));
    }

    private static void DrawBox(ScreenBuffer screen, int left, int top, int right, int bottom, string title) => DrawBox(screen, left, top, right, bottom, title, CellColor.Cyan);

    private static void DrawBox(ScreenBuffer screen, int left, int top, int right, int bottom, string title, CellColor color)
    {
        if (right - left < 4 || bottom - top < 2) return;
        var borderColor = color == CellColor.Green ? CellColor.BrightGreen : color;
        screen.Write(left, top, "+" + new string('-', right - left - 1) + "+", borderColor);
        if (color == CellColor.Green) screen.Write(left + 2, top, title, CellColor.White, CellBackground.BrightGreen, true);
        else screen.Write(left + 2, top, title, CellColor.Yellow, bold: true);
        for (var y = top + 1; y < bottom; y++) { screen.Write(left, y, "|", borderColor); screen.Write(right, y, "|", borderColor); }
        screen.Write(left, bottom, "+" + new string('-', right - left - 1) + "+", borderColor);
    }

    private static string Fit(string value, int width)
    {
        if (width <= 0) return "";
        return value.Length <= width ? value : width == 1 ? value[..1] : value[..(width - 1)] + "~";
    }

    private sealed record MetadataCompletion(TrackEntry Entry, TrackMetadata? Metadata, string? Error);
}
