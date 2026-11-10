using Yacplayer.Audio;
using Yacplayer.Configuration;
using Yacplayer.Dsp;
using Yacplayer.Presentation;

namespace Yacplayer.Tests;

public class TrackEnergyTests
{
    private static RealtimeAudioSnapshot LoudFeatures() =>
        new(true, -12, -3, 12, 0.01f, 1500, 440, 120, 0.7f, false, 1f);

    [Fact]
    public void QuietSectionIsCalmAndChorusFiresWithAnticipation()
    {
        // 40 s: the first 30 s are quiet, the last 10 s are a loud chorus.
        var blocks = new float[40];
        for (var i = 0; i < 30; i++)
        {
            blocks[i] = -40;
        }

        for (var i = 30; i < 40; i++)
        {
            blocks[i] = -20;
        }

        var map = new TrackEnergyMap(blocks, 1.0);

        // Deep in the quiet intro there is no lift and no upcoming chorus yet.
        var intro = map.Sample(5);
        Assert.True(Math.Abs(intro.Lift) < 0.5);
        Assert.True(intro.Approach < 0.5);

        // Right when the chorus starts, the lift against the preceding section is full.
        var chorus = map.Sample(31);
        Assert.True(chorus.Lift > 10);
        Assert.True(chorus.Approach <= 0.5);

        // Eight seconds before the chorus the upcoming section is already louder.
        var beforeDrop = map.Sample(22);
        Assert.True(beforeDrop.Approach > 5);
        Assert.True(beforeDrop.Lift < 0.5);
    }

    [Fact]
    public void DramaScalesPulseAmplitudeAndGlidesBackAfterTheSection()
    {
        // 40 s: quiet intro, five seconds of chorus at 30 s, quiet outro.
        var blocks = new float[40];
        Array.Fill(blocks, -40f, 0, 30);
        Array.Fill(blocks, -20f, 30, 5);
        Array.Fill(blocks, -40f, 35, 5);
        var map = new TrackEnergyMap(blocks, 1.0);

        // Same constant bass energy: without the map the pulse is the baseline.
        var baseline = new ReactiveThemeController(timeScale: 1000);
        ReactiveTheme baselineTheme = default;
        for (var frame = 0; frame <= 100; frame++)
        {
            baselineTheme = baseline.Update(LoudFeatures(), true, ReactiveUiMode.Visualizer, frame * 80L);
        }

        // In the quiet intro the arrangement scales the same pulse down.
        var calm = new ReactiveThemeController(timeScale: 1000);
        ReactiveTheme calmTheme = default;
        for (var frame = 0; frame <= 100; frame++)
        {
            calmTheme = calm.Update(LoudFeatures(), true, ReactiveUiMode.Visualizer, frame * 80L, map, 5);
        }

        Assert.True(calmTheme.Pulse < baselineTheme.Pulse * 0.7);

        // In the chorus the same pulse is pushed well above the baseline.
        var loud = new ReactiveThemeController(timeScale: 1000);
        ReactiveTheme loudTheme = default;
        for (var frame = 0; frame <= 100; frame++)
        {
            loudTheme = loud.Update(LoudFeatures(), true, ReactiveUiMode.Visualizer, frame * 80L, map, 32);
        }

        Assert.True(loudTheme.Pulse > baselineTheme.Pulse * 1.4);

        // After the chorus ends the drama glides back down over seconds.
        ReactiveTheme after = loudTheme;
        for (var frame = 101; frame <= 400; frame++)
        {
            after = loud.Update(LoudFeatures(), true, ReactiveUiMode.Visualizer, frame * 80L, map, 37);
        }

        Assert.True(after.Pulse < loudTheme.Pulse * 0.8);
    }

    [Fact]
    public void MissingMapKeepsTheExactNoArrangementBehavior()
    {
        var withNull = new ReactiveThemeController(timeScale: 1000);
        var overload = new ReactiveThemeController(timeScale: 1000);
        ReactiveTheme nullTheme = default;
        ReactiveTheme overloadTheme = default;
        for (var frame = 0; frame <= 60; frame++)
        {
            nullTheme = withNull.Update(LoudFeatures(), true, ReactiveUiMode.Subtle, frame * 80L);
            overloadTheme = overload.Update(
                LoudFeatures(), true, ReactiveUiMode.Subtle, frame * 80L, null, 12);
        }

        Assert.Equal(nullTheme, overloadTheme);
    }

    [Fact]
    public async Task ScannerReportsQuietThenLoudBlocksFromAGeneratedWav()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "structure.wav");
            WriteStructuredWav(path, seconds: 20, quietAmplitude: 0.02, loudAmplitude: 0.7, chorusFromSeconds: 10);

            var map = await TrackEnergyScanner.ScanAsync(path, CancellationToken.None);

            Assert.NotNull(map);
            Assert.Equal(20, map!.BlockCount);
            var quiet = map.Sample(2);
            var chorus = map.Sample(15);
            Assert.True(chorus.Now > quiet.Now + 10);
            Assert.True(chorus.Lift > 5);
            Assert.True(map.Sample(6).Approach > 3);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task ScannerReturnsNullForUnreadableFiles()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "broken.wav");
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4, 5]);
            Assert.Null(await TrackEnergyScanner.ScanAsync(path, CancellationToken.None));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void WriteStructuredWav(
        string path,
        int seconds,
        double quietAmplitude,
        double loudAmplitude,
        int chorusFromSeconds)
    {
        const int sampleRate = 8000;
        var frames = sampleRate * seconds;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write((uint)(36 + frames * 2));
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16u);
        writer.Write((ushort)1); // PCM
        writer.Write((ushort)1); // mono
        writer.Write((uint)sampleRate);
        writer.Write((uint)(sampleRate * 2));
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write((uint)(frames * 2));

        for (var frame = 0; frame < frames; frame++)
        {
            var amplitude = frame / sampleRate >= chorusFromSeconds ? loudAmplitude : quietAmplitude;
            var sample = (short)(amplitude * Math.Sin(2 * Math.PI * 220 * frame / sampleRate) * short.MaxValue);
            writer.Write(sample);
        }
    }
}
