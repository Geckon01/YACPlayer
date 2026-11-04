using System.Collections.Concurrent;
using Yacplayer.Library;

namespace Yacplayer.Application;

internal sealed class CurrentTrackMetadataLoader : IDisposable
{
    private readonly ConcurrentQueue<MetadataCompletion> _completions = new();
    private TrackEntry? _target;
    private CancellationTokenSource? _cancellation;

    public void EnsureCurrent(Playlist playlist, CancellationToken appCancellation)
    {
        var current = playlist.CurrentIndex is int index ? playlist.Items[index] : null;
        if (ReferenceEquals(current, _target))
        {
            return;
        }

        CancelCurrent();
        _target = current;
        if (current is null || current.MetadataState != MetadataState.Pending)
        {
            return;
        }

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(appCancellation);
        _ = LoadAsync(current, _cancellation.Token);
    }

    public string? ApplyCompletions()
    {
        string? error = null;
        while (_completions.TryDequeue(out var completion))
        {
            if (!ReferenceEquals(completion.Entry, _target))
            {
                continue;
            }

            if (completion.Metadata is null)
            {
                completion.Entry.MetadataState = MetadataState.Failed;
                error = $"Cannot read metadata for {Path.GetFileName(completion.Entry.Path)}: {completion.Error}";
                continue;
            }

            completion.Entry.Title = completion.Metadata.Title;
            completion.Entry.Artist = completion.Metadata.Artist;
            completion.Entry.Album = completion.Metadata.Album;
            completion.Entry.Duration = completion.Metadata.Duration;
            completion.Entry.MetadataState = MetadataState.Loaded;
        }

        return error;
    }

    private async Task LoadAsync(TrackEntry entry, CancellationToken ct)
    {
        try
        {
            var metadata = await MetadataService.LoadAsync(entry.Path, ct);
            if (!ct.IsCancellationRequested)
            {
                _completions.Enqueue(new(entry, metadata, null));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                _completions.Enqueue(new(entry, null, ex.Message));
            }
        }
    }

    private void CancelCurrent()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose() => CancelCurrent();

    private sealed record MetadataCompletion(
        TrackEntry Entry,
        TrackMetadata? Metadata,
        string? Error);
}
