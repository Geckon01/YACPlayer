namespace Yacplayer.Library;
public enum MetadataState { Pending, Loaded, Failed }
public sealed record TrackEntry(string Path)
{
    public string Title { get; set; } = System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Artist { get; set; } = "Unknown artist";
    public string Album { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public MetadataState MetadataState { get; set; }
}
