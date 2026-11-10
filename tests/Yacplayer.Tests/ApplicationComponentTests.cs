using Yacplayer.Application;
using Yacplayer.Audio;
using Yacplayer.Configuration;
using Yacplayer.Dsp;
using Yacplayer.Library;
using Yacplayer.Presentation;

namespace Yacplayer.Tests;

public class ApplicationComponentTests
{
    [Fact]
    public void RendererBuildsWideAnalysisAndBrowserLayouts()
    {
        var playlist = new Playlist();
        playlist.Add(new TrackEntry("C:\\Music\\track.mp3"));
        playlist.Play(0);
        var settings = new PlayerSettings { RealtimeAnalysisEnabled = true };
        var playback = new PlaybackSnapshot(
            PlaybackState.Playing,
            "C:\\Music\\track.mp3",
            TimeSpan.FromSeconds(12),
            TimeSpan.FromMinutes(3),
            48000,
            2,
            75,
            false);
        var analysis = new RealtimeAudioSnapshot(
            true,
            -18,
            -3,
            15,
            0.02f,
            2500,
            440,
            120,
            0.8f,
            true,
            0);
        var visualization = new VisualizationFrame(new float[64], new float[4096], 0, analysis);
        var browser = new BrowserViewState("C:\\Music", [], 0, false);
        var hiddenSettings = new SettingsViewState(false, 0, false, "", "");
        var renderer = new AppRenderer();

        var player = renderer.Draw(
            180,
            30,
            new(playlist, settings, playback, visualization, browser, hiddenSettings, false, false, "Ready"));
        var playerOutput = player.Diff(null);

        Assert.Contains("Realtime Audio [B]", playerOutput);
        Assert.Contains("BPM", playerOutput);
        Assert.Contains("=^o.o^=", playerOutput);
        Assert.Contains("Queue (1)", playerOutput);

        var restingVisualization = visualization with
        {
            RealtimeAudio = analysis with { BeatDetected = false }
        };
        var restingScreen = renderer.Draw(
            180,
            30,
            new(playlist, settings, playback, restingVisualization, browser, hiddenSettings, false, false, "Ready"));
        Assert.Contains("=^-.-^=", restingScreen.Diff(null));

        var browserScreen = renderer.Draw(
            180,
            30,
            new(playlist, settings, playback, visualization, browser, hiddenSettings, true, false, "Ready"));
        var browserOutput = browserScreen.Diff(null);

        Assert.Contains("FILES", browserOutput);
        Assert.Contains("QUEUE", browserOutput);
        Assert.Contains("C:\\Music", browserOutput);
    }

    [Fact]
    public void SettingsControllerChangesValuesAndAcceptsExistingPathsImmediately()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var settings = new PlayerSettings { Volume = 75 };
            var controller = new SettingsController(settings);
            controller.Open();

            var volumeChange = controller.Handle(new ConsoleKeyInfo('+', ConsoleKey.OemPlus, false, false, false));
            Assert.True(volumeChange.Changed);
            Assert.Equal(80, settings.Volume);

            for (var index = 0; index < (int)SettingsItem.StartupDirectory; index++)
            {
                controller.Handle(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            }

            controller.Handle(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            foreach (var character in directory.FullName)
            {
                controller.Handle(new ConsoleKeyInfo(character, ConsoleKey.NoName, false, false, false));
            }

            var pathChange = controller.Handle(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            Assert.True(pathChange.Changed);
            Assert.Equal(directory.FullName, settings.StartupDirectory);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void RendererDrawsSettingsAsFullscreenView()
    {
        var playlist = new Playlist();
        var settings = new PlayerSettings { StartupDirectory = "C:\\Music" };
        var renderer = new AppRenderer();
        var screen = renderer.Draw(
            100,
            28,
            new(
                playlist,
                settings,
                new PlaybackSnapshot(PlaybackState.Stopped, null, default, default, 0, 0, 75, false),
                new(new float[64], new float[4096], 0, RealtimeAudioSnapshot.Empty),
                new("C:\\", [], 0, false),
                new(true, (int)SettingsItem.StartupDirectory, false, "", "Ready"),
                false,
                false,
                "Ready"));
        var output = screen.Diff(null);

        Assert.Contains("Settings [O]", output);
        Assert.Contains("Startup folder", output);
        Assert.Contains("C:\\Music", output);
        Assert.DoesNotContain("Now Playing", output);
    }

    [Fact]
    public void RendererDrawsExitConfirmationOverCurrentView()
    {
        var renderer = new AppRenderer();
        var screen = renderer.Draw(
            100,
            28,
            new(
                new Playlist(),
                new PlayerSettings(),
                new PlaybackSnapshot(PlaybackState.Stopped, null, default, default, 0, 0, 75, false),
                new(new float[64], new float[4096], 0, RealtimeAudioSnapshot.Empty),
                new("C:\\", [], 0, false),
                new(true, 0, false, "", "Ready"),
                false,
                false,
                "Ready",
                true,
                false));
        var output = screen.Diff(null);

        Assert.Contains("Sure exit?", output);
        Assert.Contains("[ Yep ]", output);
        Assert.Contains("[ Nope ]", output);
        Assert.Contains("Settings [O]", output);
    }

    [Fact]
    public void VisualizationPipelineRebuildsFftAnalyzerWhenSettingChanges()
    {
        var settings = new PlayerSettings { FftSize = 2048 };
        var pipeline = new VisualizationPipeline(settings);
        settings.FftSize = 4096;

        pipeline.Update(
            new PlaybackSnapshot(PlaybackState.Stopped, null, default, default, 0, 0, 75, false),
            settings,
            0);

        Assert.Equal(4096, pipeline.ActiveFftSize);
    }

    [Fact]
    public async Task BrowserControllerKeepsDuplicateFilteringInsideBrowserBoundary()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "track.mp3");
            await File.WriteAllBytesAsync(path, []);
            var playlist = new Playlist();
            var settings = new PlayerSettings();
            using var browser = new BrowserController(playlist, settings, directory.FullName);
            browser.StartLoad(directory.FullName, CancellationToken.None);

            var completed = false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (browser.TryApplyCompletion(out _))
                {
                    completed = true;
                    break;
                }

                await Task.Delay(10);
            }

            Assert.True(completed);
            browser.MoveSelection(1);
            Assert.Equal("Added 1 track(s)", await browser.AppendSelectionAsync(CancellationToken.None));
            Assert.Equal("Added 0 track(s)", await browser.AppendSelectionAsync(CancellationToken.None));
            Assert.Single(playlist.Items);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
