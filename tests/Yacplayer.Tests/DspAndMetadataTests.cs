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

    private sealed class ArraySampleProvider(float[] samples) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        public int Read(Span<float> buffer)
        {
            var count = Math.Min(buffer.Length, samples.Length - _position); samples.AsSpan(_position, count).CopyTo(buffer); _position += count; return count;
        }
    }
}
