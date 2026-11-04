using Yacplayer.Application;
using Yacplayer.Audio;
using Yacplayer.Configuration;
using Yacplayer.Dsp;
using Yacplayer.Library;
using Yacplayer.Terminal;

namespace Yacplayer.Presentation;

internal readonly record struct AppRenderState(
    Playlist Playlist,
    PlayerSettings Settings,
    PlaybackSnapshot Playback,
    VisualizationFrame Visualization,
    BrowserViewState Browser,
    bool BrowserVisible,
    bool HelpVisible,
    string Message);

internal sealed class AppRenderer
{
    private static readonly string VersionLabel = $" v{typeof(AppRenderer).Assembly.GetName().Version?.ToString(4) ?? "0.0.0.0"} ";
    private readonly ListViewport _browserViewport = new();
    private readonly ListViewport _queueViewport = new();
    private readonly ListViewport _browserQueueViewport = new();

    public ScreenBuffer Draw(int width, int height, AppRenderState state)
    {
        var screen = new ScreenBuffer(width, height);
        DrawBox(screen, 0, 0, width - 1, 2, " Yacplayer ", CellColor.Green);
        if (width >= VersionLabel.Length + 16)
        {
            screen.Write(width - VersionLabel.Length - 2, 0, VersionLabel, CellColor.White, CellBackground.BrightGreen, bold: true);
        }

        var playback = state.Playback;
        var playlist = state.Playlist;
        var settings = state.Settings;
        var playbackState = playback.State == PlaybackState.Playing
            ? "> PLAYING"
            : playback.State.ToString().ToUpperInvariant();
        var volumeBlocks = Math.Clamp(playback.Volume / 10, 0, 10);
        var header = $"{playbackState}  |  Vol {playback.Volume,3}% {new string('█', volumeBlocks)}{new string('.', 10 - volumeBlocks)}  |  Repeat: {playlist.Repeat}  |  Shuffle: {(playlist.Shuffle ? "On" : "Off")}  |  Output: WASAPI";
        screen.Write(14, 1, Fit(header, Math.Max(1, width - 16)), CellColor.White, bold: true);
        if (state.BrowserVisible)
        {
            DrawBrowser(screen, width, height, state);
            return screen;
        }

        var current = playlist.CurrentIndex is int index ? playlist.Items[index] : null;
        if (width < 90 || height < 22)
        {
            DrawBox(screen, 0, 3, width - 1, 7, " Now Playing ", CellColor.Green);
            screen.Write(2, 4, Fit(current is null ? "No track selected" : current.Title, width - 4), CellColor.White, bold: true);
            screen.Write(2, 5, Fit(current?.Artist ?? "Select a track with F", width - 4), CellColor.Green);
            DrawProgress(screen, 2, 6, width - 4, playback);
            DrawBox(screen, 0, 8, width - 1, height - 2, VisualizerTitle(settings.VisualizerMode), CellColor.Green);
            DrawVisualizer(screen, 1, 10, width - 2, Math.Max(1, height - 12), state);
        }
        else
        {
            DrawWidePlayer(screen, width, height, state, current);
        }

        var footer = state.HelpVisible
            ? "Esc Close help"
            : "Hotkeys: [Space] Play/Pause | [Left/Right] Seek | [Up/Down] Select | [F] Files | [S] Shuffle | [R] Repeat | [B] Analysis | [Q] Quit | [?] Help";
        screen.Write(0, height - 1, Fit(footer, width), CellColor.Green, bold: true);
        if (state.HelpVisible)
        {
            DrawHelp(screen, width, height);
        }

        return screen;
    }

    private void DrawWidePlayer(
        ScreenBuffer screen,
        int width,
        int height,
        AppRenderState state,
        TrackEntry? current)
    {
        var playlist = state.Playlist;
        var playback = state.Playback;
        var settings = state.Settings;
        var queueLeft = Math.Max(68, width * 72 / 100);
        var infoTop = height - 9;
        DrawBox(screen, 0, 3, queueLeft - 1, 9, " Now Playing ", CellColor.Green);
        screen.Write(3, 5, Fit(current is null ? "No track selected" : current.Title, queueLeft - 6), CellColor.White, bold: true);
        screen.Write(3, 6, Fit(current?.Artist ?? "Open the file browser with F", queueLeft - 6), CellColor.Green);
        screen.Write(3, 7, $"{Path.GetExtension(playback.Path ?? "").TrimStart('.').ToUpperInvariant(),-6} | {playback.SampleRate / 1000d:0.0} kHz | {playback.Channels} ch | {playback.Duration:mm\\:ss}");
        DrawProgress(screen, 3, 8, queueLeft - 6, playback);

        DrawQueue(screen, width, height, queueLeft, playlist);
        DrawBox(screen, 0, 10, queueLeft - 1, infoTop - 1, VisualizerTitle(settings.VisualizerMode), CellColor.Green);
        if (settings.VisualizerMode == VisualizerMode.Spectrum)
        {
            screen.Write(Math.Max(2, queueLeft - 33), 11, $"40 Hz - 18 kHz | {settings.FftSize} bins", CellColor.Cyan);
        }
        else if (settings.VisualizerMode == VisualizerMode.Waveform)
        {
            screen.Write(Math.Max(2, queueLeft - 31), 11, $"Realtime PCM | {playback.SampleRate:N0} Hz", CellColor.Cyan);
        }

        DrawVisualizer(screen, 1, 12, queueLeft - 2, Math.Max(1, infoTop - 12), state);
        DrawBottomPanels(screen, queueLeft, infoTop, height, state);
    }

    private void DrawQueue(ScreenBuffer screen, int width, int height, int queueLeft, Playlist playlist)
    {
        DrawBox(screen, queueLeft, 3, width - 1, height - 2, $" Queue ({playlist.Items.Count}) ", CellColor.Green);
        var queueRows = Math.Max(1, height - 8);
        _queueViewport.EnsureVisible(playlist.SelectedIndex, playlist.Items.Count, queueRows);
        for (var row = 0; row < queueRows; row++)
        {
            var itemIndex = _queueViewport.Offset + row;
            if (itemIndex >= playlist.Items.Count)
            {
                break;
            }

            var item = playlist.Items[itemIndex];
            var active = itemIndex == playlist.CurrentIndex;
            var selected = itemIndex == playlist.SelectedIndex;
            var text = $"{(active ? '>' : ' ')} {itemIndex + 1,2}. {item.Title}";
            screen.Write(
                queueLeft + 2,
                5 + row,
                Fit(text, width - queueLeft - 4),
                active ? CellColor.Black : selected ? CellColor.Yellow : CellColor.Default,
                active ? CellBackground.Green : CellBackground.Default,
                active);
        }
    }

    private static void DrawBottomPanels(
        ScreenBuffer screen,
        int queueLeft,
        int infoTop,
        int height,
        AppRenderState state)
    {
        if (state.Settings.RealtimeAnalysisEnabled && queueLeft >= 105)
        {
            var playbackRight = queueLeft / 3;
            var analysisRight = queueLeft * 2 / 3;
            DrawPlaybackPanel(screen, 0, playbackRight - 1, infoTop, height - 2, state.Playback, state.Playlist);
            DrawRealtimeAudioPanel(screen, playbackRight, analysisRight - 1, infoTop, height - 2, state.Visualization.RealtimeAudio);
            DrawAudioInfoPanel(screen, analysisRight, queueLeft - 1, infoTop, height - 2, state.Playback);
        }
        else
        {
            var middle = queueLeft / 2;
            DrawPlaybackPanel(screen, 0, middle - 1, infoTop, height - 2, state.Playback, state.Playlist);
            DrawAudioInfoPanel(screen, middle, queueLeft - 1, infoTop, height - 2, state.Playback);
        }
    }

    private static void DrawPlaybackPanel(
        ScreenBuffer screen,
        int left,
        int right,
        int top,
        int bottom,
        PlaybackSnapshot playback,
        Playlist playlist)
    {
        DrawBox(screen, left, top, right, bottom, " Playback ", CellColor.Green);
        var width = Math.Max(1, right - left - 5);
        screen.Write(left + 3, top + 2, Fit($"Volume   : {playback.Volume}% {new string('█', Math.Clamp(playback.Volume / 10, 0, 10))}", width), CellColor.Green);
        screen.Write(left + 3, top + 3, Fit($"Repeat   : {playlist.Repeat}", width));
        screen.Write(left + 3, top + 4, Fit($"Shuffle  : {(playlist.Shuffle ? "On" : "Off")}", width));
        screen.Write(left + 3, top + 5, Fit("Output   : WASAPI shared", width));
    }

    private static void DrawAudioInfoPanel(
        ScreenBuffer screen,
        int left,
        int right,
        int top,
        int bottom,
        PlaybackSnapshot playback)
    {
        DrawBox(screen, left, top, right, bottom, " Audio Info ", CellColor.Green);
        var width = Math.Max(1, right - left - 5);
        screen.Write(left + 3, top + 2, Fit($"Format      : {Path.GetExtension(playback.Path ?? "").TrimStart('.').ToUpperInvariant()}", width), CellColor.Green);
        screen.Write(left + 3, top + 3, Fit($"Sample rate : {playback.SampleRate:N0} Hz", width), CellColor.Green);
        screen.Write(left + 3, top + 4, Fit($"Channels    : {playback.Channels}", width), CellColor.Green);
        screen.Write(left + 3, top + 5, Fit($"Duration    : {playback.Duration:mm\\:ss}", width), CellColor.Green);
    }

    private static void DrawRealtimeAudioPanel(
        ScreenBuffer screen,
        int left,
        int right,
        int top,
        int bottom,
        RealtimeAudioSnapshot audio)
    {
        DrawBox(screen, left, top, right, bottom, " Realtime Audio [B] ", CellColor.Green);
        var width = Math.Max(1, right - left - 5);
        if (!audio.Available)
        {
            screen.Write(left + 3, top + 2, Fit("Waiting for PCM / learning tempo...", width), CellColor.Cyan);
            return;
        }

        var bpm = audio.Bpm > 0 ? $"{audio.Bpm,5:0.0}" : "  -- ";
        var rhythm = audio.Bpm <= 0
            ? "Learning"
            : audio.BeatConfidence >= 0.55f
                ? "Stable"
                : audio.BeatConfidence >= 0.3f
                    ? "Detected"
                    : "Weak";
        var beat = audio.BeatDetected ? "●" : "·";
        screen.Write(left + 3, top + 1, Fit($"BPM  : {bpm}  Conf {audio.BeatConfidence:P0}", width), CellColor.Cyan);
        screen.Write(left + 3, top + 2, Fit($"Beat : {beat}  Rhythm {rhythm}", width), audio.BeatDetected ? CellColor.Yellow : CellColor.Green, bold: audio.BeatDetected);
        screen.Write(left + 3, top + 3, Fit($"RMS  : {audio.RmsDbfs,6:0.0} dBFS  Peak {audio.PeakDbfs,6:0.0}", width));
        screen.Write(left + 3, top + 4, Fit($"Crest: {audio.CrestDb,5:0.0} dB    ZCR {audio.ZeroCrossingRate:P1}", width));
        screen.Write(left + 3, top + 5, Fit($"Centroid : {FormatFrequency(audio.SpectralCentroidHz)}", width), CellColor.Green);
        screen.Write(left + 3, top + 6, Fit($"Dominant : {FormatFrequency(audio.DominantFrequencyHz)}", width), CellColor.Green);
    }

    private static void DrawProgress(ScreenBuffer screen, int x, int y, int width, PlaybackSnapshot playback)
    {
        var duration = playback.Duration.TotalSeconds;
        var ratio = duration <= 0
            ? 0
            : Math.Clamp(playback.Position.TotalSeconds / duration, 0, 1);
        var barWidth = Math.Max(1, width - 13);
        var filled = Math.Clamp((int)(ratio * barWidth), 0, barWidth);
        screen.Write(x, y, $"{playback.Position:mm\\:ss} ");
        screen.Write(x + 6, y, new string('█', filled), CellColor.Green);
        screen.Write(x + 6 + filled, y, new string('·', barWidth - filled));
        screen.Write(x + width - 5, y, $"{playback.Duration:mm\\:ss}");
    }

    private static void DrawVisualizer(
        ScreenBuffer screen,
        int left,
        int top,
        int width,
        int height,
        AppRenderState state)
    {
        if (state.Settings.VisualizerMode == VisualizerMode.Spectrum)
        {
            DrawSpectrum(screen, left, top, width, height, state.Visualization.SpectrumBands);
        }
        else if (state.Settings.VisualizerMode == VisualizerMode.Waveform)
        {
            DrawWaveform(
                screen,
                left,
                top,
                width,
                height,
                state.Visualization.Waveform,
                state.Visualization.WaveformCount);
        }
    }

    private static void DrawSpectrum(
        ScreenBuffer screen,
        int left,
        int top,
        int width,
        int height,
        float[] bands)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var bottom = top + height - 1;
        for (var x = 0; x < width; x++)
        {
            var position = x * (bands.Length - 1d) / Math.Max(1, width - 1);
            var band = (int)position;
            var next = Math.Min(bands.Length - 1, band + 1);
            var amplitude = bands[band] * (1 - (position - band)) + bands[next] * (position - band);
            var barHeight = Math.Clamp((int)Math.Round(amplitude * height), 0, height);
            for (var level = 0; level < barHeight; level++)
            {
                var ratio = level / (double)Math.Max(1, height - 1);
                screen.Write(left + x, bottom - level, "█", SpectrumGradient(ratio));
            }

            if (barHeight > 0)
            {
                screen.Write(left + x, Math.Max(top, bottom - barHeight), "-", CellColor.Yellow, bold: true);
            }
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

    private static void DrawWaveform(
        ScreenBuffer screen,
        int left,
        int top,
        int width,
        int height,
        float[] samples,
        int count)
    {
        if (width <= 0 || height <= 0 || count <= 0)
        {
            return;
        }

        double peak = 0;
        for (var index = 0; index < count; index++)
        {
            peak = Math.Max(peak, Math.Abs(samples[index]));
        }

        if (peak < 0.0001)
        {
            return;
        }

        var center = top + (height - 1) / 2d;
        var scale = Math.Max(0, height - 1) / 2d;
        var gain = 0.9 / peak;
        for (var x = 0; x < width; x++)
        {
            var from = x * count / width;
            var to = Math.Max(from + 1, (x + 1) * count / width);
            var minimum = samples[from];
            var maximum = samples[from];
            for (var index = from + 1; index < Math.Min(count, to); index++)
            {
                minimum = Math.Min(minimum, samples[index]);
                maximum = Math.Max(maximum, samples[index]);
            }

            var upper = (int)Math.Round(center - Math.Clamp(maximum * gain, -1, 1) * scale);
            var lower = (int)Math.Round(center - Math.Clamp(minimum * gain, -1, 1) * scale);
            upper = Math.Clamp(upper, top, top + height - 1);
            lower = Math.Clamp(lower, top, top + height - 1);
            for (var y = Math.Min(upper, lower); y <= Math.Max(upper, lower); y++)
            {
                screen.Write(left + x, y, ".", CellColor.Green, bold: true);
            }
        }
    }

    private void DrawBrowser(ScreenBuffer screen, int width, int height, AppRenderState state)
    {
        var browser = state.Browser;
        var playlist = state.Playlist;
        var top = 3;
        var bottom = height - 3;
        var split = width >= 70 ? width * 2 / 3 : width;
        DrawBox(screen, 0, top, split - 1, bottom, " FILES ");
        if (split < width)
        {
            DrawBox(screen, split, top, width - 1, bottom, " QUEUE ");
        }

        screen.Write(2, top + 1, Fit(browser.Path, Math.Max(1, split - 5)), CellColor.Yellow);
        screen.Write(2, top + 2, "Name", CellColor.Cyan);
        screen.Write(Math.Max(2, split - 12), top + 2, "Type", CellColor.Cyan);
        var listTop = top + 3;
        var rows = Math.Max(1, bottom - listTop);
        _browserViewport.EnsureVisible(browser.SelectedIndex, browser.Entries.Count + 1, rows);
        for (var row = 0; row < rows; row++)
        {
            var index = row + _browserViewport.Offset;
            if (index > browser.Entries.Count)
            {
                break;
            }

            DrawBrowserEntry(screen, split, listTop + row, index, browser);
        }

        if (split < width)
        {
            DrawBrowserQueue(screen, width, top, bottom, split, playlist);
        }

        screen.Write(1, height - 2, Fit(" Enter Open/Play   Backspace Parent   A Add   F/Esc Close   Q Quit", width - 2), CellColor.Cyan);
        screen.Write(1, height - 1, Fit(state.Message, width - 2));
    }

    private static void DrawBrowserEntry(
        ScreenBuffer screen,
        int split,
        int row,
        int index,
        BrowserViewState browser)
    {
        var selected = index == browser.SelectedIndex;
        var color = selected ? CellColor.Yellow : CellColor.Default;
        if (index == 0)
        {
            var parentLabel = browser.AtDrives ? "[This PC]" : "[..]";
            screen.Write(2, row, $"{(selected ? '>' : ' ')} {parentLabel}", color);
            screen.Write(Math.Max(2, split - 12), row, browser.AtDrives ? "ROOT" : "PARENT", color);
            return;
        }

        var entry = browser.Entries[index - 1];
        var nameWidth = Math.Max(5, split - 16);
        var entryName = browser.AtDrives ? entry.Path : Path.GetFileName(entry.Path);
        screen.Write(2, row, $"{(selected ? '>' : ' ')} {Fit(entryName, nameWidth)}", color);
        screen.Write(
            Math.Max(2, split - 12),
            row,
            entry.IsDirectory ? "DIR" : Path.GetExtension(entry.Path).TrimStart('.').ToUpperInvariant(),
            entry.IsDirectory ? CellColor.Cyan : color);
    }

    private void DrawBrowserQueue(
        ScreenBuffer screen,
        int width,
        int top,
        int bottom,
        int split,
        Playlist playlist)
    {
        var queueWidth = Math.Max(1, width - split - 4);
        var queueRows = Math.Max(1, bottom - top - 2);
        var queueTarget = playlist.CurrentIndex ?? playlist.SelectedIndex;
        _browserQueueViewport.EnsureVisible(queueTarget, playlist.Items.Count, queueRows);
        for (var row = 0; row < queueRows; row++)
        {
            var index = _browserQueueViewport.Offset + row;
            if (index >= playlist.Items.Count)
            {
                break;
            }

            var marker = index == playlist.CurrentIndex ? "*" : " ";
            screen.Write(
                split + 2,
                top + 2 + row,
                Fit($"{marker} {index + 1,3} {playlist.Items[index].Title}", queueWidth),
                index == playlist.CurrentIndex ? CellColor.Green : CellColor.Default);
        }

        if (playlist.Items.Count == 0)
        {
            screen.Write(split + 2, top + 2, "Queue is empty");
        }
    }

    private static void DrawHelp(ScreenBuffer screen, int width, int height)
    {
        for (var y = 2; y < height - 2; y++)
        {
            screen.Write(2, y, new string(' ', Math.Max(0, width - 4)));
        }

        screen.Write(4, 3, "KEYS: Space pause  X stop  N/P next/prev  Left/Right seek");
        screen.Write(4, 4, "+/- volume  M mute  Enter play  Del remove  S shuffle");
        screen.Write(4, 5, "R repeat  V visualizer  B realtime analysis  Q quit  Esc close");
    }

    private static string VisualizerTitle(VisualizerMode mode) => mode switch
    {
        VisualizerMode.Spectrum => " Spectrum (FFT) ",
        VisualizerMode.Waveform => " Waveform ",
        _ => " Visualizer: Off "
    };

    private static string FormatFrequency(float value) => value >= 1000
        ? $"{value / 1000:0.00} kHz"
        : $"{value:0} Hz";

    private static void DrawBox(
        ScreenBuffer screen,
        int left,
        int top,
        int right,
        int bottom,
        string title) => DrawBox(screen, left, top, right, bottom, title, CellColor.Cyan);

    private static void DrawBox(
        ScreenBuffer screen,
        int left,
        int top,
        int right,
        int bottom,
        string title,
        CellColor color)
    {
        if (right - left < 4 || bottom - top < 2)
        {
            return;
        }

        var borderColor = color == CellColor.Green ? CellColor.BrightGreen : color;
        screen.Write(left, top, "+" + new string('-', right - left - 1) + "+", borderColor);
        if (color == CellColor.Green)
        {
            screen.Write(left + 2, top, title, CellColor.White, CellBackground.BrightGreen, true);
        }
        else
        {
            screen.Write(left + 2, top, title, CellColor.Yellow, bold: true);
        }

        for (var y = top + 1; y < bottom; y++)
        {
            screen.Write(left, y, "|", borderColor);
            screen.Write(right, y, "|", borderColor);
        }

        screen.Write(left, bottom, "+" + new string('-', right - left - 1) + "+", borderColor);
    }

    private static string Fit(string value, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        return value.Length <= width
            ? value
            : width == 1
                ? value[..1]
                : value[..(width - 1)] + "~";
    }
}
