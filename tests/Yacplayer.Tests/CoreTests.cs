using Yacplayer.Audio;
using Yacplayer.Cli;
using Yacplayer.Configuration;
using Yacplayer.Library;
using Yacplayer.Terminal;
namespace Yacplayer.Tests;

public class CoreTests
{
    [Fact] public void CliRejectsUnknownAndBadFps() { Assert.False(Arguments.Parse(["--wat"]).IsValid); Assert.False(Arguments.Parse(["--fps", "42"]).IsValid); Assert.True(Arguments.Parse(["--help"]).Help); }
    [Fact] public void AssemblyUsesFourPartReleaseVersion() { Assert.Equal(new Version(1, 26, 5, 11), typeof(Yacplayer.App).Assembly.GetName().Version); Assert.Contains("1.26.5.11", Arguments.HelpText); }
    [Fact] public void PlaylistRepeatAndRemovalAreSafe() { var p = new Playlist(1); Assert.Null(p.Next()); p.Add(new("a")); p.Add(new("b")); Assert.Equal("a", p.Next()!.Path); Assert.Equal("b", p.Next()!.Path); Assert.Null(p.Next(true)); p.SetRepeat(RepeatMode.All); Assert.Equal("a", p.Next(true)!.Path); p.Remove(0); Assert.Single(p.Items); }
    [Fact] public void ShuffleDoesNotRepeatWithinCycle() { var p = new Playlist(2); p.AddRange(Enumerable.Range(0, 5).Select(i => new TrackEntry(i.ToString()))); p.ToggleShuffle(); var paths = Enumerable.Range(0, 5).Select(_ => p.Next()!.Path).ToArray(); Assert.Equal(5, paths.Distinct().Count()); }
    [Fact]
    public void PlaylistCanRestoreRepeatAndShuffleWithoutRefillingAnActiveCycle()
    {
        var playlist = new Playlist(2);
        playlist.AddRange(Enumerable.Range(0, 3).Select(i => new TrackEntry(i.ToString())));
        playlist.SetRepeat(RepeatMode.All);
        playlist.SetShuffle(true);

        var first = playlist.Next()!.Path;
        playlist.SetShuffle(true);
        var remaining = new[] { playlist.Next()!.Path, playlist.Next()!.Path };

        Assert.Equal(RepeatMode.All, playlist.Repeat);
        Assert.True(playlist.Shuffle);
        Assert.Equal(3, remaining.Append(first).Distinct().Count());
    }
    [Fact]
    public void PlaylistBatchInsertPreservesTheActiveShuffleCycle()
    {
        var playlist = new Playlist(7);
        playlist.AddRange(Enumerable.Range(0, 4).Select(i => new TrackEntry(i.ToString())));
        playlist.SetShuffle(true);

        var played = new List<string>
        {
            playlist.Next()!.Path,
            playlist.Next()!.Path
        };

        playlist.AddRange([new TrackEntry("4"), new TrackEntry("5")]);
        TrackEntry? next;
        while ((next = playlist.Next()) is not null)
        {
            played.Add(next.Path);
        }

        Assert.Equal(6, played.Count);
        Assert.Equal(6, played.Distinct().Count());
    }
    [Fact]
    public void AudioEventQueueIsBoundedAndDropsTheOldestEvent()
    {
        var events = new AudioEventQueue(2);
        events.Publish(new AudioEvent(AudioEventKind.Ended, 1));
        events.Publish(new AudioEvent(AudioEventKind.Error, 2, "old error"));
        events.Publish(new AudioEvent(AudioEventKind.Ended, 3));

        Assert.True(events.TryRead(out var first));
        Assert.True(events.TryRead(out var second));
        Assert.False(events.TryRead(out _));
        Assert.Equal(2, first.Generation);
        Assert.Equal(3, second.Generation);
    }
    [Fact]
    public void HelpDoesNotAdvertiseTheUnimplementedFullscreenControl()
    {
        Assert.DoesNotContain("H fullscreen", Arguments.HelpText, StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void RingWrapsAndDropsOldest() { var r = new SampleRingBuffer(4); r.Write([1, 2, 3, 4, 5]); Span<float> output = stackalloc float[4]; Assert.Equal(4, r.Read(output)); Assert.Equal(new float[] { 2, 3, 4, 5 }, output.ToArray()); Assert.Equal(0, r.Count); }
    [Fact] public void RingCanReadLatestSamplesAndDiscardBacklog() { var r = new SampleRingBuffer(8); r.Write([1, 2, 3, 4, 5, 6]); Span<float> output = stackalloc float[3]; Assert.Equal(3, r.ReadLatest(output)); Assert.Equal(new float[] { 4, 5, 6 }, output.ToArray()); Assert.Equal(0, r.Count); }
    [Fact] public void ScreenDiffIsEmptyForIdenticalFrames() { var a = new ScreenBuffer(5, 2); a.Write(0, 0, "hello"); var b = new ScreenBuffer(5, 2); b.Write(0, 0, "hello"); Assert.Equal("", b.Diff(a)); b.Write(4, 1, "x"); Assert.Contains("x", b.Diff(a)); }
    [Fact] public void ScreenDiffEmitsColorAndDetectsColorOnlyChange() { var a = new ScreenBuffer(1, 1); a.Write(0, 0, "█", CellColor.Green); Assert.Contains("\u001b[32m", a.Diff(null)); var b = new ScreenBuffer(1, 1); b.Write(0, 0, "█", CellColor.Red); var diff = b.Diff(a); Assert.Contains("\u001b[31m", diff); Assert.Contains("█", diff); }
    [Fact] public void ScreenDiffEmitsTrueColorForeground() { var screen = new ScreenBuffer(1, 1); screen.Write(0, 0, "█", CellColor.Rgb(12, 34, 56)); Assert.Contains("\u001b[38;2;12;34;56m", screen.Diff(null)); }
    [Fact] public void ScreenDiffBatchesAdjacentCells() { var screen = new ScreenBuffer(8, 1); screen.Write(0, 0, "████████", CellColor.Green); var diff = screen.Diff(null); Assert.Equal(1, diff.Split("\u001b[1;", StringSplitOptions.None).Length - 1); }
    [Fact] public void ScreenDiffRepaintsRowsThatContainVariableWidthUnicode() { var previous = new ScreenBuffer(8, 1); previous.Write(0, 0, "曲"); var current = new ScreenBuffer(8, 1); current.Write(0, 0, "曲"); var diff = current.Diff(previous); Assert.Contains("\u001b[2K", diff); Assert.Contains("曲", diff); var plainPrevious = new ScreenBuffer(8, 1); plainPrevious.Write(0, 0, "████"); var plainCurrent = new ScreenBuffer(8, 1); plainCurrent.Write(0, 0, "████"); Assert.Equal("", plainCurrent.Diff(plainPrevious)); var cleared = new ScreenBuffer(8, 1); Assert.Contains("\u001b[2K", cleared.Diff(current)); }
    [Fact] public void ListViewportKeepsTargetVisibleWithMargin() { var viewport = new ListViewport(); viewport.EnsureVisible(50, 100, 10); Assert.Equal(42, viewport.Offset); viewport.EnsureVisible(43, 100, 10); Assert.Equal(42, viewport.Offset); viewport.EnsureVisible(10, 100, 10); Assert.Equal(9, viewport.Offset); }
    [Fact] public void ListViewportClampsAfterCollectionOrWindowChanges() { var viewport = new ListViewport(); viewport.EnsureVisible(90, 100, 10); Assert.Equal(82, viewport.Offset); viewport.EnsureVisible(2, 3, 20); Assert.Equal(0, viewport.Offset); viewport.EnsureVisible(0, 0, 10); Assert.Equal(0, viewport.Offset); }
    [Fact]
    public async Task SettingsPersistRealtimeAnalysisToggle()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var store = new SettingsStore(System.IO.Path.Combine(directory.FullName, "analysis.json"));
            await store.SaveAsync(new PlayerSettings { RealtimeAnalysisEnabled = false });
            var loaded = await store.LoadAsync();
            Assert.False(loaded.RealtimeAnalysisEnabled);
        }
        finally
        {
            directory.Delete(true);
        }
    }
    [Fact]
    public async Task SettingsPersistStartupDirectory()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = System.IO.Path.Combine(directory.FullName, "settings.json");
            var store = new SettingsStore(path);
            await store.SaveAsync(new PlayerSettings { StartupDirectory = directory.FullName });

            var loaded = await store.LoadAsync();

            Assert.Equal(directory.FullName, loaded.StartupDirectory);
        }
        finally
        {
            directory.Delete(true);
        }
    }
    [Fact] public async Task SettingsRoundTripAndValidate() { var dir = Directory.CreateTempSubdirectory(); try { var path = System.IO.Path.Combine(dir.FullName, "s.json"); var store = new SettingsStore(path); await store.SaveAsync(new() { Volume = 999, Fps = 7 }); var loaded = await store.LoadAsync(); Assert.Equal(100, loaded.Volume); Assert.Equal(30, loaded.Fps); await File.WriteAllTextAsync(path, "{"); loaded = await store.LoadAsync(); Assert.Equal(75, loaded.Volume); } finally { dir.Delete(true); } }
}
