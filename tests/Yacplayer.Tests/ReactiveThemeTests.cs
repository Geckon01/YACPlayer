using Yacplayer.Application;
using Yacplayer.Audio;
using Yacplayer.Configuration;
using Yacplayer.Dsp;
using Yacplayer.Library;
using Yacplayer.Presentation;
using Yacplayer.Terminal;

namespace Yacplayer.Tests;

public class ReactiveThemeTests
{
    private static RealtimeAudioSnapshot Features(float bass = 0.5f, float centroid = 1500f, float rmsDbfs = -20f) =>
        new(true, rmsDbfs, -3, 12, 0.01f, centroid, 440, 120, 0.7f, false, bass);

    [Fact]
    public void OffModeProducesInactiveThemeAndIgnoresFeatures()
    {
        var controller = new ReactiveThemeController();
        var theme = controller.Update(Features(bass: 1f), true, ReactiveUiMode.Off, 0);
        Assert.False(theme.Active);
        Assert.Equal(default, theme);
    }

    [Fact]
    public void UpdatesAreRateLimited()
    {
        var controller = new ReactiveThemeController();
        var first = controller.Update(Features(), true, ReactiveUiMode.Subtle, 1_000);
        var early = controller.Update(Features(bass: 1f), true, ReactiveUiMode.Subtle, 1_040);
        Assert.Equal(first, early);

        var later = controller.Update(Features(bass: 1f), true, ReactiveUiMode.Subtle, 1_200);
        Assert.NotEqual(first, later);
    }

    [Fact]
    public void BassPulseRisesWithAttackAndFallsBackToStaticAfterRelease()
    {
        // timeScale 1000 compresses the envelope: 80 ms frame * 1000 = 80 s of smoothing.
        var controller = new ReactiveThemeController(timeScale: 1000);
        controller.Update(Features(bass: 0f), true, ReactiveUiMode.Subtle, 0);

        var bright = controller.Update(Features(bass: 1f), true, ReactiveUiMode.Subtle, 80);
        Assert.True(bright.Active);

        // Many release frames later with no audio the parameters must return to base.
        var resting = bright;
        for (var frame = 1; frame <= 200; frame++)
        {
            resting = controller.Update(Features(bass: 1f), false, ReactiveUiMode.Subtle, 80 + frame * 80L);
        }

        var baseBorder = ReactiveThemeController.FromHsl(
            ReactiveThemeController.BaseHue,
            0.82 * 0.85,
            0.40);
        var baseTitle = ReactiveThemeController.BackgroundFromHsl(
            ReactiveThemeController.BaseHue,
            0.82,
            0.42);
        Assert.Equal(baseBorder, resting.Border);
        Assert.Equal(baseTitle, resting.TitleBackground);
    }

    [Fact]
    public void BassEnergyOnlyBrightensWithinBoundedAmplitude()
    {
        var controller = new ReactiveThemeController(timeScale: 1000);
        controller.Update(Features(bass: 0f), true, ReactiveUiMode.Subtle, 0);
        var bright = controller.Update(Features(bass: 1f), true, ReactiveUiMode.Subtle, 80);
        controller.Update(Features(bass: 0f), true, ReactiveUiMode.Subtle, 160);
        var dim = controller.Update(Features(bass: 0f), true, ReactiveUiMode.Subtle, 240);

        Assert.True(bright.TitleBackground.G > dim.TitleBackground.G);
        // Bounded change: at most about 20% of the 0..255 channel range.
        Assert.True(bright.TitleBackground.G - dim.TitleBackground.G <= 52);
    }

    [Fact]
    public void SpectralCentroidMovesHueWithinTheLimitedPalette()
    {
        var warm = new ReactiveThemeController(timeScale: 1000);
        var cool = new ReactiveThemeController(timeScale: 1000);
        for (var frame = 0; frame < 200; frame++)
        {
            warm.Update(Features(centroid: 200f), true, ReactiveUiMode.Subtle, frame * 80L);
            cool.Update(Features(centroid: 5800f), true, ReactiveUiMode.Subtle, frame * 80L);
        }

        var warmTheme = warm.Update(Features(centroid: 200f), true, ReactiveUiMode.Subtle, 200 * 80L);
        var coolTheme = cool.Update(Features(centroid: 5800f), true, ReactiveUiMode.Subtle, 200 * 80L);

        // Higher centroid slides the hue toward teal: less red, more blue.
        Assert.True(warmTheme.Border.R > coolTheme.Border.R);
        Assert.True(coolTheme.Border.B > warmTheme.Border.B);
        // The palette stays green-teal: never red/magenta dominated.
        Assert.True(warmTheme.Border.G > warmTheme.Border.R);
        Assert.True(coolTheme.Border.G >= coolTheme.Border.R);
    }

    [Fact]
    public void PausedPlaybackDriftsBackToBaseWithoutExceptions()
    {
        var controller = new ReactiveThemeController(timeScale: 1000);
        for (var frame = 0; frame < 10; frame++)
        {
            controller.Update(Features(bass: 1f, centroid: 5000f), true, ReactiveUiMode.Visualizer, frame * 80L);
        }

        var resting = controller.Update(
            RealtimeAudioSnapshot.Empty,
            false,
            ReactiveUiMode.Visualizer,
            10 * 80L + 100_000);
        Assert.True(resting.Active);
        Assert.Equal(
            ReactiveThemeController.BackgroundFromHsl(ReactiveThemeController.BaseHue, 0.82, 0.42),
            resting.TitleBackground);
    }

    [Fact]
    public void VisualizerModeRespondsMoreStronglyThanSubtle()
    {
        var subtle = new ReactiveThemeController(timeScale: 1000);
        var visualizer = new ReactiveThemeController(timeScale: 1000);
        subtle.Update(Features(bass: 0f), true, ReactiveUiMode.Subtle, 0);
        visualizer.Update(Features(bass: 0f), true, ReactiveUiMode.Visualizer, 0);

        var subtlePulse = subtle.Update(Features(bass: 1f), true, ReactiveUiMode.Subtle, 80);
        var visualizerPulse = visualizer.Update(Features(bass: 1f), true, ReactiveUiMode.Visualizer, 80);
        Assert.True(
            visualizerPulse.TitleBackground.G > subtlePulse.TitleBackground.G);
    }

    [Fact]
    public void EnergyModulatesSaturation()
    {
        var calm = new ReactiveThemeController(timeScale: 1000);
        var loud = new ReactiveThemeController(timeScale: 1000);
        for (var frame = 0; frame < 200; frame++)
        {
            calm.Update(Features(bass: 0.1f, rmsDbfs: -45f), true, ReactiveUiMode.Subtle, frame * 80L);
            loud.Update(Features(bass: 0.1f, rmsDbfs: -12f), true, ReactiveUiMode.Subtle, frame * 80L);
        }

        var calmTheme = calm.Update(Features(bass: 0.1f, rmsDbfs: -45f), true, ReactiveUiMode.Subtle, 200 * 80L);
        var loudTheme = loud.Update(Features(bass: 0.1f, rmsDbfs: -12f), true, ReactiveUiMode.Subtle, 200 * 80L);

        // Higher energy means a purer accent: the min/max channel spread widens.
        Assert.True(Spread(loudTheme.Border) > Spread(calmTheme.Border));
    }

    private static int Spread(CellColor color)
        => Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));

    [Fact]
    public void RendererUsesTrueColorWhenReactiveAndStaticWhenOff()
    {
        var playlist = new Playlist();
        var settings = new PlayerSettings();
        var playback = new PlaybackSnapshot(PlaybackState.Playing, "a.mp3", default, default, 48000, 2, 75, false);
        var frame = new VisualizationFrame(new float[64], new float[4096], 0, RealtimeAudioSnapshot.Empty);
        var browser = new BrowserViewState("C:\\", [], 0, false);
        var hiddenMenu = new SettingsViewState(false, 0, false, "", "");
        var renderer = new AppRenderer();

        var staticTheme = renderer.Draw(120, 30, new AppRenderState(
            playlist, settings, playback, frame, browser, hiddenMenu, false, false, "Ready"));
        Assert.Contains("\u001b[92m", staticTheme.Diff(null));
        Assert.DoesNotContain("\u001b[48;2;", staticTheme.Diff(null));

        var controller = new ReactiveThemeController(timeScale: 1000);
        controller.Update(Features(bass: 0.8f, centroid: 3000f), true, ReactiveUiMode.Subtle, 0);
        var theme = controller.Update(Features(bass: 0.8f, centroid: 3000f), true, ReactiveUiMode.Subtle, 80);
        var reactive = renderer.Draw(120, 30, new AppRenderState(
            playlist, settings, playback, frame, browser, hiddenMenu, false, false, "Ready",
            ExitConfirmationYesSelected: false, ReactiveTheme: theme));
        var output = reactive.Diff(null);
        Assert.Contains("\u001b[48;2;", output);
        Assert.Contains("\u001b[38;2;", output);
    }

    [Fact]
    public async Task ReactiveModePersistsThroughSettingsStore()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var store = new SettingsStore(Path.Combine(directory.FullName, "reactive.json"));
            await store.SaveAsync(new PlayerSettings { ReactiveUiMode = ReactiveUiMode.Visualizer });
            var loaded = await store.LoadAsync();
            Assert.Equal(ReactiveUiMode.Visualizer, loaded.ReactiveUiMode);

            // Existing users without the property keep the previous static look.
            await File.WriteAllTextAsync(
                Path.Combine(directory.FullName, "reactive.json"),
                """{"Volume": 60}""");
            loaded = await store.LoadAsync();
            Assert.Equal(ReactiveUiMode.Off, loaded.ReactiveUiMode);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void VersionLabelFlashesNewColorOnEachBeatAndFadesBetweenBeats()
    {
        var controller = new ReactiveThemeController();
        var beatFeatures = Features() with { BeatDetected = true };

        var onBeat = controller.Update(beatFeatures, true, ReactiveUiMode.Subtle, 0);
        Assert.Equal(1d, onBeat.VersionGlow);
        var firstHue = onBeat.VersionHue;

        // Between beats the flash decays toward the base white and reaches it.
        var between = controller.Update(Features(), true, ReactiveUiMode.Subtle, 600);
        Assert.True(between.VersionGlow < onBeat.VersionGlow);

        controller.Update(Features(), true, ReactiveUiMode.Subtle, 1200);
        controller.Update(Features(), true, ReactiveUiMode.Subtle, 1800);
        controller.Update(Features(), true, ReactiveUiMode.Subtle, 2400);
        var offBeat = controller.Update(Features(), true, ReactiveUiMode.Subtle, 3000);
        Assert.Equal(0d, offBeat.VersionGlow);

        // The next predicted beat flashes with a different color.
        var secondBeat = controller.Update(beatFeatures, true, ReactiveUiMode.Subtle, 3600);
        Assert.Equal(1d, secondBeat.VersionGlow);
        Assert.NotEqual(firstHue, secondBeat.VersionHue);
    }

    [Fact]
    public void VersionColorFadesToWhiteAndOffModeNeverFlashes()
    {
        var flash = ReactiveThemeController.VersionColor(210, 1);
        Assert.True(flash.R < 200); // saturated blue, not white
        var rested = ReactiveThemeController.VersionColor(210, 0);
        Assert.True(rested.R >= 250 && rested.R == rested.G && rested.G == rested.B);

        var controller = new ReactiveThemeController();
        var off = controller.Update(Features() with { BeatDetected = true }, true, ReactiveUiMode.Off, 0);
        Assert.Equal(0d, off.VersionGlow);
    }

    [Fact]
    public void RendererTintsVersionLabelWithThemeGlow()
    {
        var playlist = new Playlist();
        var playback = new PlaybackSnapshot(PlaybackState.Playing, "a.mp3", default, default, 48000, 2, 75, false);
        var frame = new VisualizationFrame(new float[64], new float[4096], 0, RealtimeAudioSnapshot.Empty);
        var browser = new BrowserViewState("C:\\", [], 0, false);
        var hiddenMenu = new SettingsViewState(false, 0, false, "", "");
        var renderer = new AppRenderer();

        var controller = new ReactiveThemeController();
        var theme = controller.Update(Features() with { BeatDetected = true }, true, ReactiveUiMode.Subtle, 0);
        var output = renderer.Draw(120, 30, new AppRenderState(
            playlist, new PlayerSettings(), playback, frame, browser, hiddenMenu, false, false, "Ready",
            ReactiveTheme: theme)).Diff(null);
        var expected = ReactiveThemeController.VersionColor(theme.VersionHue, theme.VersionGlow);
        Assert.Contains($"\u001b[38;2;{expected.R};{expected.G};{expected.B}m", output);
        Assert.Contains("v1.26.5.11", output);
    }

    [Fact]
    public void SpectrumKeepsStaticGradientWhenOffAndFollowsThemeWhenReactive()
    {
        var playlist = new Playlist();
        var playback = new PlaybackSnapshot(PlaybackState.Playing, "a.mp3", default, default, 48000, 2, 75, false);
        var bands = Enumerable.Repeat(1f, 64).ToArray();
        var frame = new VisualizationFrame(bands, new float[4096], 0, RealtimeAudioSnapshot.Empty);
        var browser = new BrowserViewState("C:\\", [], 0, false);
        var hiddenMenu = new SettingsViewState(false, 0, false, "", "");
        var renderer = new AppRenderer();

        var staticOutput = renderer.Draw(100, 24, new AppRenderState(
            playlist, new PlayerSettings(), playback, frame, browser, hiddenMenu, false, false, "Ready")).Diff(null);
        // Classic ramp: bottom green with the yellow marker.
        Assert.Contains("\u001b[38;2;0;190;0m", staticOutput);
        Assert.Contains("\u001b[33m", staticOutput);

        var controller = new ReactiveThemeController(timeScale: 1000);
        for (var f = 0; f < 200; f++)
        {
            controller.Update(Features(bass: 0.9f, centroid: 5800f), true, ReactiveUiMode.Subtle, f * 80L);
        }

        var theme = controller.Update(Features(bass: 0.9f, centroid: 5800f), true, ReactiveUiMode.Subtle, 200 * 80L);
        var reactiveOutput = renderer.Draw(100, 24, new AppRenderState(
            playlist, new PlayerSettings(), playback, frame, browser, hiddenMenu, false, false, "Ready",
            ReactiveTheme: theme)).Diff(null);

        // Bottom of the bars: hue from the theme, saturation clamped, lightness 0.34 + pulse/2.
        var expectedBottom = ReactiveThemeController.FromHsl(
            theme.Hue,
            Math.Clamp(theme.Saturation, 0.55, 0.95),
            Math.Clamp(0.34 + theme.Pulse * 0.5, 0.2, 0.85));
        Assert.Contains($"\u001b[38;2;{expectedBottom.R};{expectedBottom.G};{expectedBottom.B}m", reactiveOutput);
        // The yellow static marker must not appear once the theme is active.
        Assert.DoesNotContain("\u001b[33m", reactiveOutput);
    }

    [Fact]
    public void RendererThemesProgressBarAndWaveformWithTheActiveTheme()
    {
        var playlist = new Playlist();
        var playback = new PlaybackSnapshot(
            PlaybackState.Playing, "a.mp3", TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(100), 48000, 2, 75, false);
        var samples = new float[512];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.5f * (float)Math.Sin(i / 16d);
        }

        var frame = new VisualizationFrame(new float[64], samples, samples.Length, RealtimeAudioSnapshot.Empty);
        var browser = new BrowserViewState("C:\\", [], 0, false);
        var hiddenMenu = new SettingsViewState(false, 0, false, "", "");
        var renderer = new AppRenderer();
        var settings = new PlayerSettings { VisualizerMode = VisualizerMode.Waveform };

        var controller = new ReactiveThemeController(timeScale: 1000);
        for (var frameIndex = 0; frameIndex < 50; frameIndex++)
        {
            controller.Update(Features(bass: 0.9f, centroid: 2400f), true, ReactiveUiMode.Visualizer, frameIndex * 80L);
        }

        var theme = controller.Update(Features(bass: 0.9f, centroid: 2400f), true, ReactiveUiMode.Visualizer, 50 * 80L);
        var output = renderer.Draw(120, 30, new AppRenderState(
            playlist, settings, playback, frame, browser, hiddenMenu, false, false, "Ready",
            ReactiveTheme: theme)).Diff(null);

        var expectedProgress = ReactiveThemeController.FromHsl(
            theme.Hue,
            Math.Clamp(theme.Saturation, 0.55, 0.95),
            Math.Clamp(0.50 + theme.Pulse * 1.2, 0.4, 0.85));
        Assert.Contains($"\u001b[38;2;{expectedProgress.R};{expectedProgress.G};{expectedProgress.B}m", output);
        var expectedWave = ReactiveThemeController.FromHsl(
            theme.Hue,
            Math.Clamp(theme.Saturation, 0.55, 0.95),
            Math.Clamp(0.55 + theme.Pulse * 1.4, 0.45, 0.9));
        Assert.Contains($"\u001b[38;2;{expectedWave.R};{expectedWave.G};{expectedWave.B}m", output);

        // With the theme Off both return to the static green.
        var staticOutput = renderer.Draw(120, 30, new AppRenderState(
            playlist, settings, playback, frame, browser, hiddenMenu, false, false, "Ready")).Diff(null);
        Assert.Contains("\u001b[32m", staticOutput);
    }

    [Fact]
    public void SpectralDensityIsLowForToneAndHighForBroadbandNoise()
    {
        var toneRing = new SampleRingBuffer(65536);
        var toneAnalyzer = new RealtimeAudioAnalyzer(toneRing);
        var tone = FeedTone(toneRing, toneAnalyzer, 440f);

        var noiseRing = new SampleRingBuffer(65536);
        var noiseAnalyzer = new RealtimeAudioAnalyzer(noiseRing);
        var noise = FeedNoise(noiseRing, noiseAnalyzer);

        // A single timbral source concentrates energy in a few bands; a full
        // arrangement lights most of the spectrum up.
        Assert.True(tone.SpectralDensity < 0.5f);
        Assert.True(noise.SpectralDensity > tone.SpectralDensity + 0.2f);
    }

    [Fact]
    public void SparseInstrumentationSubduesTheResponseAndWideArrangementRestoresIt()
    {
        // Same loud bass energy: a lone instrument (low density) must react
        // well below a full arrangement (high density).
        var solo = new ReactiveThemeController(timeScale: 1000);
        var band = new ReactiveThemeController(timeScale: 1000);
        ReactiveTheme soloTheme = default;
        ReactiveTheme bandTheme = default;
        for (var frame = 0; frame <= 100; frame++)
        {
            soloTheme = solo.Update(
                Features(bass: 1f) with { SpectralDensity = 0.05f }, true, ReactiveUiMode.Visualizer, frame * 80L);
            bandTheme = band.Update(
                Features(bass: 1f) with { SpectralDensity = 1f }, true, ReactiveUiMode.Visualizer, frame * 80L);
        }

        Assert.True(soloTheme.Pulse < bandTheme.Pulse * 0.6);
    }

    [Fact]
    public void BarKickBlipsOnBeatOnlyForMeatySections()
    {
        // Quiet and sparse: even a warmed-up beat must not make the bars blink.
        var calm = new ReactiveThemeController();
        for (var frame = 0; frame < 3; frame++)
        {
            calm.Update(
                Features(bass: 0.1f) with { SpectralDensity = 0.2f }, true, ReactiveUiMode.Subtle, frame * 100L);
        }

        var calmBeat = calm.Update(
            Features(bass: 0.1f) with { BeatDetected = true, SpectralDensity = 0.2f },
            true, ReactiveUiMode.Subtle, 300);
        Assert.Equal(0d, calmBeat.BarKick);

        // Loud and wide: warm the envelopes, then the beat fires the kick.
        var meaty = new ReactiveThemeController();
        for (var frame = 0; frame < 3; frame++)
        {
            meaty.Update(
                Features(bass: 1f) with { SpectralDensity = 1f }, true, ReactiveUiMode.Visualizer, frame * 100L);
        }

        var meatyBeat = meaty.Update(
            Features(bass: 1f) with { BeatDetected = true, SpectralDensity = 1f },
            true, ReactiveUiMode.Visualizer, 300);
        Assert.Equal(1d, meatyBeat.BarKick);

        var rested = meaty.Update(
            Features(bass: 1f) with { SpectralDensity = 1f }, true, ReactiveUiMode.Visualizer, 360);
        Assert.True(rested.BarKick > 0 && rested.BarKick < 1d);

        var offBeat = meaty.Update(
            Features(bass: 1f) with { BeatDetected = true, SpectralDensity = 1f },
            true, ReactiveUiMode.Off, 420);
        Assert.Equal(0d, offBeat.BarKick);
    }

    [Fact]
    public void RendererBrightensSpectrumBarsOnBarKick()
    {
        var playlist = new Playlist();
        var playback = new PlaybackSnapshot(
            PlaybackState.Playing, "a.mp3", default, default, 48000, 2, 75, false);
        var bands = Enumerable.Repeat(1f, 64).ToArray();
        var frame = new VisualizationFrame(bands, new float[4096], 0, RealtimeAudioSnapshot.Empty);
        var browser = new BrowserViewState("C:\\", [], 0, false);
        var hiddenMenu = new SettingsViewState(false, 0, false, "", "");
        var renderer = new AppRenderer();

        var controller = new ReactiveThemeController();
        var theme = controller.Update(
            Features(bass: 1f) with { BeatDetected = true, SpectralDensity = 1f },
            true, ReactiveUiMode.Visualizer, 0);
        var output = renderer.Draw(100, 24, new AppRenderState(
            playlist, new PlayerSettings(), playback, frame, browser, hiddenMenu, false, false, "Ready",
            ReactiveTheme: theme)).Diff(null);

        var expectedBottom = ReactiveThemeController.FromHsl(
            theme.Hue,
            Math.Clamp(theme.Saturation, 0.55, 0.95),
            Math.Clamp(0.34 + theme.Pulse * 0.5 + theme.BarKick * 0.22, 0.2, 0.9));
        Assert.Contains($"\u001b[38;2;{expectedBottom.R};{expectedBottom.G};{expectedBottom.B}m", output);
    }

    [Fact]
    public void BassEnergyIsHighForLowToneAndLowForHighTone()
    {
        var bassRing = new SampleRingBuffer(65536);
        var bassAnalyzer = new RealtimeAudioAnalyzer(bassRing);
        var bass = FeedTone(bassRing, bassAnalyzer, 80f);

        var trebleRing = new SampleRingBuffer(65536);
        var trebleAnalyzer = new RealtimeAudioAnalyzer(trebleRing);
        var treble = FeedTone(trebleRing, trebleAnalyzer, 8000f);

        Assert.InRange(bass.BassEnergy, 0.2f, 1f);
        Assert.True(bass.BassEnergy > treble.BassEnergy * 2);
        Assert.InRange(treble.BassEnergy, 0f, 0.5f);
    }

    private static RealtimeAudioSnapshot FeedTone(
        SampleRingBuffer ring,
        RealtimeAudioAnalyzer analyzer,
        float frequency)
    {
        const int sampleRate = 48000;
        var snapshot = RealtimeAudioSnapshot.Empty;
        var written = 0;
        while (written < sampleRate * 2)
        {
            var block = new float[2048];
            for (var i = 0; i < block.Length; i++)
            {
                block[i] = 0.5f * (float)Math.Sin(2 * Math.PI * frequency * (written + i) / sampleRate);
            }

            ring.Write(block);
            written += block.Length;
            snapshot = analyzer.Analyze(sampleRate);
        }

        return snapshot;
    }

    private static RealtimeAudioSnapshot FeedNoise(
        SampleRingBuffer ring,
        RealtimeAudioAnalyzer analyzer)
    {
        const int sampleRate = 48000;
        var random = new Random(20261111);
        var snapshot = RealtimeAudioSnapshot.Empty;
        var written = 0;
        while (written < sampleRate * 2)
        {
            var block = new float[2048];
            for (var i = 0; i < block.Length; i++)
            {
                block[i] = 0.5f * (2 * (float)random.NextDouble() - 1);
            }

            ring.Write(block);
            written += block.Length;
            snapshot = analyzer.Analyze(sampleRate);
        }

        return snapshot;
    }
}
