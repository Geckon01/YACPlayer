using Yacplayer.Audio;
using Yacplayer.Cli;
using Yacplayer.Configuration;
using Yacplayer.Dsp;
using Yacplayer.Library;
using Yacplayer.Terminal;

namespace Yacplayer;

public sealed class App(Arguments arguments)
{
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
    private int _browserScroll;
    private bool _browserAtDrives;

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
        while (!ct.IsCancellationRequested)
        {
            while (Console.KeyAvailable) if (!await HandleAsync(Console.ReadKey(true), audio, ct)) goto done;
            int width, height;
            try { width = Math.Max(20, Console.WindowWidth); height = Math.Max(8, Console.WindowHeight); } catch { break; }
            var snapshot = audio.Snapshot;
            analyzer.Analyze(snapshot.SampleRate <= 0 ? 48000 : snapshot.SampleRate, rawBands);
            for (var i = 0; i < bands.Length; i++)
            {
                var smoothing = rawBands[i] > bands[i] ? 0.55f : 0.16f;
                bands[i] += (rawBands[i] - bands[i]) * smoothing;
                if (bands[i] < 0.002f) bands[i] = 0;
            }
            var screen = Draw(width, height, snapshot, bands);
            terminal.Render(screen.Diff(previous)); previous = screen;
            try { await Task.Delay(1000 / _settings.Fps, ct); } catch (OperationCanceledException) { break; }
        }
    done:
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

    private async Task LoadBrowserAsync(string path, CancellationToken ct)
    {
        _message = $"Loading {path}";
        var entries = await FileBrowser.EnumerateAsync(path, ct);
        _browserPath = path; _browserEntries = entries; _browserSelected = 0; _browserScroll = 0; _browserAtDrives = false;
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
        _browserAtDrives = true; _browserPath = "This PC"; _browserSelected = 0; _browserScroll = 0;
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

    private ScreenBuffer Draw(int width, int height, PlaybackSnapshot playback, float[] bands)
    {
        var screen = new ScreenBuffer(width, height);
        screen.Write(0, 0, $" Yacplayer  {playback.State}  Vol {playback.Volume}%{(playback.Muted ? " MUTE" : "")}  Repeat {_playlist.Repeat}  Shuffle {(_playlist.Shuffle ? "on" : "off")}");
        screen.Write(0, 1, new string('-', width));
        if (_browserVisible)
        {
            DrawBrowser(screen, width, height);
            return screen;
        }
        var current = _playlist.CurrentIndex is int index ? _playlist.Items[index] : null;
        screen.Write(1, 2, current is null ? "No track" : $"{current.Artist} - {current.Title}");
        var duration = playback.Duration.TotalSeconds;
        var ratio = duration <= 0 ? 0 : Math.Clamp(playback.Position.TotalSeconds / duration, 0, 1);
        var barWidth = Math.Max(1, width - 22);
        var filled = Math.Clamp((int)(ratio * barWidth), 0, barWidth);
        var progressPrefix = $"{playback.Position:mm\\:ss} [";
        screen.Write(1, 3, progressPrefix);
        screen.Write(1 + progressPrefix.Length, 3, new string('█', filled), CellColor.Green);
        screen.Write(1 + progressPrefix.Length + filled, 3, $"{new string('.', barWidth - filled)}] {playback.Duration:mm\\:ss}");

        var visualTop = 5;
        var visualBottom = height - 3;
        var visualHeight = Math.Max(1, visualBottom - visualTop + 1);
        if (_settings.VisualizerMode == VisualizerMode.Spectrum)
        {
            for (var x = 0; x < width; x++)
            {
                var position = x * (bands.Length - 1d) / Math.Max(1, width - 1);
                var left = (int)position;
                var right = Math.Min(bands.Length - 1, left + 1);
                var amplitude = bands[left] * (1 - (position - left)) + bands[right] * (position - left);
                var barHeight = Math.Clamp((int)Math.Round(amplitude * visualHeight), 0, visualHeight);
                for (var level = 0; level < barHeight; level++)
                {
                    var heightRatio = level / (double)Math.Max(1, visualHeight - 1);
                    var color = heightRatio switch { >= 0.72 => CellColor.Red, >= 0.42 => CellColor.Yellow, _ => CellColor.Green };
                    screen.Write(x, visualBottom - level, "█", color);
                }
            }
        }

        screen.Write(0, height - 1, _help ? "Esc close help" : $" {_message}  |  ? help  Q quit");
        if (_help)
        {
            for (var y = 2; y < height - 2; y++) screen.Write(2, y, new string(' ', Math.Max(0, width - 4)));
            screen.Write(4, 3, "KEYS: Space pause  X stop  N/P next/prev  Left/Right seek");
            screen.Write(4, 4, "+/- volume  M mute  Enter play  Del remove  S shuffle");
            screen.Write(4, 5, "R repeat  V visualizer  H fullscreen  Q quit  Esc close");
        }
        return screen;
    }

    private void DrawBrowser(ScreenBuffer screen, int width, int height)
    {
        var top = 2; var bottom = height - 3;
        var left = 0; var split = width >= 70 ? width * 2 / 3 : width; var right = width - 1;
        DrawBox(screen, left, top, split - 1, bottom, " FILES ");
        if (split < width) DrawBox(screen, split, top, right, bottom, " QUEUE ");
        screen.Write(2, top + 1, Fit(_browserPath, Math.Max(1, split - 5)), CellColor.Yellow);
        screen.Write(2, top + 2, "Name", CellColor.Cyan);
        screen.Write(Math.Max(2, split - 12), top + 2, "Type", CellColor.Cyan);
        var listTop = top + 3; var rows = Math.Max(1, bottom - listTop);
        if (_browserSelected < _browserScroll) _browserScroll = _browserSelected;
        if (_browserSelected >= _browserScroll + rows) _browserScroll = _browserSelected - rows + 1;
        for (var row = 0; row < rows; row++)
        {
            var index = row + _browserScroll;
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
            for (var row = 0; row < queueRows && row < _playlist.Items.Count; row++)
            {
                var marker = row == _playlist.CurrentIndex ? "*" : " ";
                screen.Write(split + 2, top + 2 + row, Fit($"{marker} {row + 1,3} {_playlist.Items[row].Title}", queueWidth), row == _playlist.CurrentIndex ? CellColor.Green : CellColor.Default);
            }
            if (_playlist.Items.Count == 0) screen.Write(split + 2, top + 2, "Queue is empty");
        }
        screen.Write(1, height - 2, Fit(" Enter Open/Play   Backspace Parent   A Add   F/Esc Close   Q Quit", width - 2), CellColor.Cyan);
        screen.Write(1, height - 1, Fit(_message, width - 2));
    }

    private static void DrawBox(ScreenBuffer screen, int left, int top, int right, int bottom, string title)
    {
        if (right - left < 4 || bottom - top < 2) return;
        screen.Write(left, top, "+" + new string('-', right - left - 1) + "+", CellColor.Cyan);
        screen.Write(left + 2, top, title, CellColor.Yellow);
        for (var y = top + 1; y < bottom; y++) { screen.Write(left, y, "|", CellColor.Cyan); screen.Write(right, y, "|", CellColor.Cyan); }
        screen.Write(left, bottom, "+" + new string('-', right - left - 1) + "+", CellColor.Cyan);
    }

    private static string Fit(string value, int width)
    {
        if (width <= 0) return "";
        return value.Length <= width ? value : width == 1 ? value[..1] : value[..(width - 1)] + "~";
    }
}
