using Yacplayer.Audio; using Yacplayer.Dsp; using Yacplayer.Library;
namespace Yacplayer.Tests;
public class DspAndMetadataTests
{
    [Fact] public void SpectrumIsFiniteForSilenceAndTone()
    {
        const int size=2048, rate=48000; var ring=new SampleRingBuffer(size*2); var analyzer=new SpectrumAnalyzer(ring,size); var bands=new float[24]; ring.Write(new float[size]); Assert.True(analyzer.Analyze(rate,bands)); Assert.All(bands,v=>Assert.True(float.IsFinite(v)));
        var tone=Enumerable.Range(0,size).Select(i=>(float)Math.Sin(2*Math.PI*1000*i/rate)).ToArray(); ring.Write(tone); Assert.True(analyzer.Analyze(rate,bands)); Assert.True(bands.Max()>0); Assert.All(bands,v=>Assert.InRange(v,0,1));
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
}
