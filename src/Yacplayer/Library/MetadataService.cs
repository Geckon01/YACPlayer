namespace Yacplayer.Library;
public static class MetadataService
{
    public static Task LoadAsync(TrackEntry entry, CancellationToken ct = default) => Task.Run(() =>
    {
        try { ct.ThrowIfCancellationRequested(); var track = new ATL.Track(entry.Path); entry.Title = string.IsNullOrWhiteSpace(track.Title) ? System.IO.Path.GetFileNameWithoutExtension(entry.Path) : track.Title; entry.Artist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown artist" : track.Artist; entry.Album = track.Album ?? ""; entry.Duration = TimeSpan.FromMilliseconds(track.DurationMs); entry.MetadataState = MetadataState.Loaded; }
        catch { entry.MetadataState = MetadataState.Failed; }
    }, ct);
}
