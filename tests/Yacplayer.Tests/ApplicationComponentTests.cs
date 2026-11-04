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
            true);
        var visualization = new VisualizationFrame(new float[64], new float[4096], 0, analysis);
        var browser = new BrowserViewState("C:\\Music", [], 0, false);
        var renderer = new AppRenderer();

        var player = renderer.Draw(
            180,
            30,
            new(playlist, settings, playback, visualization, browser, false, false, "Ready"));
        var playerOutput = player.Diff(null);

        Assert.Contains("Realtime Audio [B]", playerOutput);
        Assert.Contains("BPM", playerOutput);
        Assert.Contains("Queue (1)", playerOutput);

        var browserScreen = renderer.Draw(
            180,
            30,
            new(playlist, settings, playback, visualization, browser, true, false, "Ready"));
        var browserOutput = browserScreen.Diff(null);

        Assert.Contains("FILES", browserOutput);
        Assert.Contains("QUEUE", browserOutput);
        Assert.Contains("C:\\Music", browserOutput);
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
