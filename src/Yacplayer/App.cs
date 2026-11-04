using Yacplayer.Application;
using Yacplayer.Audio;
using Yacplayer.Cli;
using Yacplayer.Configuration;
using Yacplayer.Library;
using Yacplayer.Presentation;
using Yacplayer.Terminal;

namespace Yacplayer;

public sealed class App(Arguments arguments)
{
    private readonly Playlist _playlist = new();
    private readonly SettingsStore _store = new();
    private PlayerSettings _settings = new();
    private string _message = "Ready";
    private bool _helpVisible;
    private bool _browserVisible;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        _settings = await _store.LoadAsync(ct);
        ApplyCommandLineOverrides();
        RestorePlaylistSettings();

        var start = ResolveStartupPath();
        await LoadStartupQueueAsync(start, ct);
        var browserPath = Directory.Exists(start)
            ? start
            : Path.GetDirectoryName(start) ?? Environment.CurrentDirectory;
        _browserVisible = arguments.Path is null;

        using var browser = new BrowserController(_playlist, _settings, browserPath);
        using var metadata = new CurrentTrackMetadataLoader();
        var visualization = new VisualizationPipeline(_settings);
        var audioEvents = new AudioEventQueue();
        using var audio = new AudioEngine(
            visualization.VisualizationRing,
            audioEvents,
            visualization.AnalysisRing);
        ConfigureAudio(audio, start);

        if (Console.IsOutputRedirected || Console.IsInputRedirected)
        {
            Console.WriteLine($"Yacplayer: {_playlist.Items.Count} track(s) loaded. Interactive TUI requires a terminal.");
            return 0;
        }

        if (_browserVisible)
        {
            _message = browser.StartLoad(browserPath, ct);
        }

        visualization.InitializeAnalysisGeneration(audio.AnalysisGeneration);
        var renderer = new AppRenderer();
        using var terminal = new TerminalSession();
        ScreenBuffer? previous = null;
        while (!ct.IsCancellationRequested)
        {
            ProcessAudioEvents(audio, audioEvents);
            if (browser.TryApplyCompletion(out var browserMessage))
            {
                _message = browserMessage;
            }

            while (Console.KeyAvailable)
            {
                if (!await HandleAsync(Console.ReadKey(true), audio, browser, ct))
                {
                    goto done;
                }
            }

            if (!TryGetTerminalSize(out var width, out var height))
            {
                break;
            }

            metadata.EnsureCurrent(_playlist, ct);
            _message = metadata.ApplyCompletions() ?? _message;
            var playback = audio.Snapshot;
            var visualizationFrame = visualization.Update(playback, _settings, audio.AnalysisGeneration);
            var state = new AppRenderState(
                _playlist,
                _settings,
                playback,
                visualizationFrame,
                browser.View,
                _browserVisible,
                _helpVisible,
                _message);
            var screen = renderer.Draw(width, height, state);
            terminal.Render(screen.Diff(previous));
            previous = screen;

            try
            {
                await Task.Delay(1000 / _settings.Fps, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

    done:
        browser.CancelLoad();
        audio.Stop();
        CaptureRuntimeSettings(audio);
        try
        {
            await _store.SaveAsync(_settings, CancellationToken.None);
        }
        catch (IOException)
        {
        }

        return 0;
    }

    private void ApplyCommandLineOverrides()
    {
        if (arguments.Fps is int fps)
        {
            _settings.Fps = fps;
        }

        if (arguments.NoVisualizer)
        {
            _settings.VisualizerMode = VisualizerMode.Off;
        }

        if (arguments.Recursive)
        {
            _settings.Recursive = true;
        }
    }

    private void RestorePlaylistSettings()
    {
        _playlist.SetRepeat(_settings.RepeatMode);
        _playlist.SetShuffle(_settings.Shuffle);
    }

    private string ResolveStartupPath()
    {
        var start = arguments.Path
            ?? (!string.IsNullOrEmpty(_settings.LastDirectory)
                ? _settings.LastDirectory
                : Environment.CurrentDirectory);
        if (Directory.Exists(start))
        {
            _settings.LastDirectory = start;
        }

        return start;
    }

    private async Task LoadStartupQueueAsync(string start, CancellationToken ct)
    {
        if (arguments.Path is null)
        {
            return;
        }

        var files = await Task.Run(
            () => FileBrowser.Scan(start, _settings.Recursive).ToArray(),
            ct);
        _playlist.AddRange(files.Select(path => new TrackEntry(path)));
    }

    private void ConfigureAudio(AudioEngine audio, string start)
    {
        audio.SetVolume(_settings.Volume);
        if (_settings.Muted)
        {
            audio.ToggleMute();
        }

        audio.SetRealtimeAnalysis(_settings.RealtimeAnalysisEnabled);
        if (File.Exists(start) && _playlist.Items.Count > 0)
        {
            _playlist.Play(0);
            audio.Open(start);
        }
    }

    private async Task<bool> HandleAsync(
        ConsoleKeyInfo key,
        AudioEngine audio,
        BrowserController browser,
        CancellationToken ct)
    {
        if (_helpVisible)
        {
            if (key.Key is ConsoleKey.Escape or ConsoleKey.Oem2)
            {
                _helpVisible = false;
            }

            return true;
        }

        if (_browserVisible)
        {
            return await HandleBrowserKeyAsync(key, audio, browser, ct);
        }

        return HandlePlayerKey(key, audio, browser, ct);
    }

    private async Task<bool> HandleBrowserKeyAsync(
        ConsoleKeyInfo key,
        AudioEngine audio,
        BrowserController browser,
        CancellationToken ct)
    {
        switch (key.Key)
        {
            case ConsoleKey.Q:
                return false;
            case ConsoleKey.F:
            case ConsoleKey.Escape:
                _browserVisible = false;
                browser.CancelLoad();
                break;
            case ConsoleKey.UpArrow:
                browser.MoveSelection(-1);
                break;
            case ConsoleKey.DownArrow:
                browser.MoveSelection(1);
                break;
            case ConsoleKey.Backspace:
                _message = browser.GoToParent(ct);
                break;
            case ConsoleKey.Enter:
                var selectedTrack = browser.OpenSelection(ct, out _message);
                if (selectedTrack is not null)
                {
                    Play(selectedTrack, audio);
                    _browserVisible = false;
                }
                break;
            case ConsoleKey.A:
                _message = await browser.AppendSelectionAsync(ct);
                break;
        }

        return true;
    }

    private bool HandlePlayerKey(
        ConsoleKeyInfo key,
        AudioEngine audio,
        BrowserController browser,
        CancellationToken ct)
    {
        switch (key.Key)
        {
            case ConsoleKey.Q:
                return false;
            case ConsoleKey.Spacebar:
                audio.TogglePause();
                break;
            case ConsoleKey.X:
                audio.Stop();
                break;
            case ConsoleKey.N:
                Play(_playlist.Next(), audio);
                break;
            case ConsoleKey.P:
                Play(_playlist.Previous(), audio);
                break;
            case ConsoleKey.LeftArrow:
                audio.Seek(-5);
                break;
            case ConsoleKey.RightArrow:
                audio.Seek(5);
                break;
            case ConsoleKey.UpArrow:
                _playlist.Select(_playlist.SelectedIndex - 1);
                break;
            case ConsoleKey.DownArrow:
                _playlist.Select(_playlist.SelectedIndex + 1);
                break;
            case ConsoleKey.Enter:
                Play(_playlist.Play(_playlist.SelectedIndex), audio);
                break;
            case ConsoleKey.Delete:
                RemoveSelectedTrack(audio, browser);
                break;
            case ConsoleKey.M:
                audio.ToggleMute();
                break;
            case ConsoleKey.Add:
            case ConsoleKey.OemPlus:
                audio.SetVolume(audio.Snapshot.Volume + 5);
                break;
            case ConsoleKey.Subtract:
            case ConsoleKey.OemMinus:
                audio.SetVolume(audio.Snapshot.Volume - 5);
                break;
            case ConsoleKey.S:
                _playlist.ToggleShuffle();
                _message = $"Shuffle {(_playlist.Shuffle ? "on" : "off")}";
                break;
            case ConsoleKey.R:
                _playlist.SetRepeat((RepeatMode)(((int)_playlist.Repeat + 1) % 3));
                _message = $"Repeat {_playlist.Repeat}";
                break;
            case ConsoleKey.V:
                _settings.VisualizerMode = (VisualizerMode)(((int)_settings.VisualizerMode + 1) % 3);
                break;
            case ConsoleKey.B:
                ToggleRealtimeAnalysis(audio);
                break;
            case ConsoleKey.F:
                _browserVisible = true;
                _message = browser.View.AtDrives
                    ? browser.ShowDrives()
                    : browser.StartLoad(browser.View.Path, ct);
                break;
            case ConsoleKey.Oem2 when key.KeyChar == '?':
                _helpVisible = true;
                break;
        }

        return true;
    }

    private void RemoveSelectedTrack(AudioEngine audio, BrowserController browser)
    {
        var removedPath = (uint)_playlist.SelectedIndex < (uint)_playlist.Items.Count
            ? _playlist.Items[_playlist.SelectedIndex].Path
            : null;
        var current = _playlist.Remove(_playlist.SelectedIndex);
        if (removedPath is not null)
        {
            browser.ForgetPathIfUnused(removedPath);
        }

        if (current is null)
        {
            audio.Stop();
        }
        else if (audio.Snapshot.Path is not null
            && !string.Equals(current.Path, audio.Snapshot.Path, StringComparison.OrdinalIgnoreCase))
        {
            audio.Open(current.Path);
        }
    }

    private void ToggleRealtimeAnalysis(AudioEngine audio)
    {
        _settings.RealtimeAnalysisEnabled = !_settings.RealtimeAnalysisEnabled;
        audio.SetRealtimeAnalysis(_settings.RealtimeAnalysisEnabled);
        _message = $"Realtime audio analysis {(_settings.RealtimeAnalysisEnabled ? "on" : "off")}";
    }

    private void ProcessAudioEvents(AudioEngine audio, AudioEventQueue events)
    {
        while (events.TryRead(out var audioEvent))
        {
            if (audioEvent.Generation != audio.Generation)
            {
                continue;
            }

            if (audioEvent.Kind == AudioEventKind.Error)
            {
                _message = audioEvent.Message ?? "Audio playback failed";
                continue;
            }

            var next = _playlist.Next(true);
            if (next is not null)
            {
                audio.Open(next.Path);
            }
            else
            {
                _message = "Queue finished";
            }
        }
    }

    private void CaptureRuntimeSettings(AudioEngine audio)
    {
        _settings.Volume = audio.Snapshot.Volume;
        _settings.Muted = audio.Snapshot.Muted;
        _settings.RepeatMode = _playlist.Repeat;
        _settings.Shuffle = _playlist.Shuffle;
    }

    private static void Play(TrackEntry? track, AudioEngine audio)
    {
        if (track is not null)
        {
            audio.Open(track.Path);
        }
    }

    private static bool TryGetTerminalSize(out int width, out int height)
    {
        try
        {
            width = Math.Max(20, Console.WindowWidth);
            height = Math.Max(8, Console.WindowHeight);
            return true;
        }
        catch
        {
            width = 0;
            height = 0;
            return false;
        }
    }
}
