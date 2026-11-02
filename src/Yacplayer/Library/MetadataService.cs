namespace Yacplayer.Library;

public sealed record TrackMetadata(string Title, string Artist, string Album, TimeSpan Duration);

public static class MetadataService
{
    private static readonly SemaphoreSlim LoadGate = new(1, 1);

    public static async Task<TrackMetadata> LoadAsync(string path, CancellationToken ct = default)
    {
        await LoadGate.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var track = new ATL.Track(path);
                ct.ThrowIfCancellationRequested();
                return new TrackMetadata(
                    string.IsNullOrWhiteSpace(track.Title) ? System.IO.Path.GetFileNameWithoutExtension(path) : track.Title,
                    string.IsNullOrWhiteSpace(track.Artist) ? "Unknown artist" : track.Artist,
                    track.Album ?? "",
                    TimeSpan.FromMilliseconds(track.DurationMs));
            }, ct);
        }
        finally { LoadGate.Release(); }
    }
}
