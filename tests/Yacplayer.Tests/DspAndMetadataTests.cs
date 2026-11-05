using NAudio.Wave; using NAudio.Wave.SampleProviders; using Yacplayer.Audio; using Yacplayer.Dsp; using Yacplayer.Library;
namespace Yacplayer.Tests;
public class DspAndMetadataTests
{
    [Fact] public void SpectrumIsFiniteForSilenceAndTone()
    {
        const int size=2048, rate=48000; var ring=new SampleRingBuffer(size*2); var analyzer=new SpectrumAnalyzer(ring,size); var bands=new float[24]; ring.Write(new float[size]); Assert.True(analyzer.Analyze(rate,bands)); Assert.All(bands,v=>Assert.True(float.IsFinite(v)));
        var tone=Enumerable.Range(0,size).Select(i=>(float)Math.Sin(2*Math.PI*1000*i/rate)).ToArray(); ring.Write(tone); Assert.True(analyzer.Analyze(rate,bands)); Assert.True(bands.Max()>0); Assert.All(bands,v=>Assert.InRange(v,0,1));
    }
    [Fact] public void PcmTapCapturesSamplesBeforeOutputVolume()
    {
        var source = new ArraySampleProvider([0.8f, -0.4f]); var ring = new SampleRingBuffer(8); var tap = new PcmTapSampleProvider(source, ring); var volume = new VolumeSampleProvider(tap) { Volume = 0.25f }; var output = new float[2];
        Assert.Equal(2, volume.Read(output)); Assert.Equal([0.2f, -0.1f], output);
        var captured = new float[2]; Assert.Equal(2, ring.Read(captured)); Assert.Equal([0.8f, -0.4f], captured);
    }
    [Fact]
    public void RealtimeAnalysisTapCanBeDisabledIndependently()
    {
        var source = new ArraySampleProvider([0.1f, 0.2f, 0.3f, 0.4f]);
        var visualization = new SampleRingBuffer(8);
        var analysis = new SampleRingBuffer(8);
        var tap = new PcmTapSampleProvider(source, visualization, analysis);
        var output = new float[2];

        Assert.Equal(2, tap.Read(output));
        Assert.Equal(0, analysis.Count);

        tap.AnalysisEnabled = true;
        Assert.Equal(2, tap.Read(output));
        Assert.Equal(2, analysis.Count);
        Assert.Equal(4, visualization.Count);
    }
    [Fact]
    public void RealtimeAnalyzerFindsToneLevelAndSyntheticTempo()
    {
        const int sampleRate = 48000;
        const int frameSize = 1024;
        var ring = new SampleRingBuffer(frameSize * 4);
        var analyzer = new RealtimeAudioAnalyzer(ring);
        var snapshot = analyzer.Analyze(sampleRate);

        for (var frame = 0; frame < 400; frame++)
        {
            var amplitude = frame % 23 == 0 ? 0.9 : 0.05;
            var samples = new float[frameSize];
            for (var index = 0; index < samples.Length; index++)
            {
                var sampleIndex = frame * frameSize + index;
                samples[index] = (float)(amplitude * Math.Sin(2 * Math.PI * 440 * sampleIndex / sampleRate));
            }

            ring.Write(samples);
            snapshot = analyzer.Analyze(sampleRate);
        }

        Assert.True(snapshot.Available);
        Assert.InRange(snapshot.DominantFrequencyHz, 420, 460);
        Assert.InRange(snapshot.Bpm, 115, 130);
        Assert.True(snapshot.BeatConfidence > 0.3f);
        Assert.InRange(snapshot.PeakDbfs, -30, 0);
    }

    [Theory]
    [InlineData(97)]
    [InlineData(127)]
    [InlineData(173)]
    public void BeatTrackerRefinesTempoBeyondTheOnsetFrameGrid(double expectedBpm)
    {
        const int sampleRate = 48000;
        var ring = new SampleRingBuffer(65536);
        var analyzer = new RealtimeAudioAnalyzer(ring);

        var snapshot = FeedClickTrack(ring, analyzer, sampleRate, expectedBpm, 14);

        Assert.InRange(snapshot.Bpm, expectedBpm - 1.25, expectedBpm + 1.25);
        Assert.True(snapshot.BeatConfidence >= 0.35f);
    }

    [Fact]
    public void BeatTrackerKeepsPredictingThroughShortMissingBeatSection()
    {
        const int sampleRate = 48000;
        const int hopSize = 256;
        var ring = new SampleRingBuffer(65536);
        var analyzer = new RealtimeAudioAnalyzer(ring);
        var snapshot = FeedClickTrack(ring, analyzer, sampleRate, 120, 12);
        var predictedBeats = 0;
        var wasBeat = snapshot.BeatDetected;

        for (var offset = 0; offset < sampleRate * 3 / 2; offset += hopSize)
        {
            ring.Write(new float[Math.Min(hopSize, sampleRate * 3 / 2 - offset)]);
            snapshot = analyzer.Analyze(sampleRate);
            if (snapshot.BeatDetected && !wasBeat)
            {
                predictedBeats++;
            }

            wasBeat = snapshot.BeatDetected;
        }

        Assert.InRange(snapshot.Bpm, 118.5, 121.5);
        Assert.True(snapshot.BeatConfidence >= 0.2f);
        Assert.InRange(predictedBeats, 2, 4);
    }

    [Fact]
    public void BeatTrackerRelocksAfterARealTempoChange()
    {
        const int sampleRate = 48000;
        var ring = new SampleRingBuffer(65536);
        var analyzer = new RealtimeAudioAnalyzer(ring);

        var before = FeedClickTrack(ring, analyzer, sampleRate, 120, 8);
        var after = FeedClickTrack(ring, analyzer, sampleRate, 150, 14);

        Assert.InRange(before.Bpm, 118.5, 121.5);
        Assert.InRange(after.Bpm, 148, 152);
        Assert.True(after.BeatConfidence >= 0.3f);
    }

    [Fact] public async Task MetadataUsesFallbacksForMissingFile()
    {
        var path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".mp3");
        var metadata=await MetadataService.LoadAsync(path);
        Assert.Equal(System.IO.Path.GetFileNameWithoutExtension(path),metadata.Title);
        Assert.Equal("Unknown artist",metadata.Artist);
        Assert.Equal("",metadata.Album);
        Assert.Equal(TimeSpan.Zero,metadata.Duration);
    }
    [Fact] public async Task BrowserSortsDirectoriesFirstAndFilters()
    {
        var dir=Directory.CreateTempSubdirectory(); try { Directory.CreateDirectory(System.IO.Path.Combine(dir.FullName,"z")); await File.WriteAllBytesAsync(System.IO.Path.Combine(dir.FullName,"a.wav"),[]); await File.WriteAllTextAsync(System.IO.Path.Combine(dir.FullName,"b.txt"),""); var entries=await FileBrowser.EnumerateAsync(dir.FullName); Assert.True(entries[0].IsDirectory); Assert.Equal(2,entries.Count); } finally { dir.Delete(true); }
    }

    [Fact]
    public async Task BrowserEnumerationHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FileBrowser.EnumerateAsync(Environment.CurrentDirectory, cancellation.Token));
    }

    private sealed class ArraySampleProvider(float[] samples) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        public int Read(Span<float> buffer)
        {
            var count = Math.Min(buffer.Length, samples.Length - _position); samples.AsSpan(_position, count).CopyTo(buffer); _position += count; return count;
        }
    }

    private static RealtimeAudioSnapshot FeedClickTrack(
        SampleRingBuffer ring,
        RealtimeAudioAnalyzer analyzer,
        int sampleRate,
        double bpm,
        double durationSeconds)
    {
        const int chunkSize = 256;
        var sampleCount = (int)Math.Round(sampleRate * durationSeconds);
        var beatPeriod = sampleRate * 60 / bpm;
        var clickLength = Math.Max(1, sampleRate / 100);
        var snapshot = RealtimeAudioSnapshot.Empty;

        for (var offset = 0; offset < sampleCount; offset += chunkSize)
        {
            var samples = new float[Math.Min(chunkSize, sampleCount - offset)];
            for (var index = 0; index < samples.Length; index++)
            {
                var position = offset + index;
                var phase = position - Math.Floor(position / beatPeriod) * beatPeriod;
                if (phase >= clickLength)
                {
                    continue;
                }

                var envelope = Math.Exp(-phase / (sampleRate * 0.0025));
                samples[index] = (float)(envelope * (
                    0.7 * Math.Sin(2 * Math.PI * 1200 * phase / sampleRate) +
                    0.3 * Math.Sin(2 * Math.PI * 80 * phase / sampleRate)));
            }

            ring.Write(samples);
            snapshot = analyzer.Analyze(sampleRate);
        }

        return snapshot;
    }
}
