using System.Collections.Concurrent;
using Yacplayer.Audio;
using Yacplayer.Dsp;
using Yacplayer.Library;

namespace Yacplayer.Application;

/// <summary>
/// Owns the offline energy map of the current track. Scanning runs on the
/// thread pool against an independent decoder; completions cross back through
/// a bounded queue that the UI loop drains, mirroring the metadata loader.
/// </summary>
internal sealed class TrackEnergyMapLoader : IDisposable
{
    private readonly ConcurrentQueue<ScanCompletion> _completions = new();
    private TrackEntry? _target;
    private CancellationTokenSource? _cancellation;

    public TrackEnergyMap? CurrentMap { get; private set; }

    public void EnsureCurrent(Playlist playlist, bool enabled, CancellationToken appCancellation)
    {
        if (!enabled)
        {
            return;
        }

        var current = playlist.CurrentIndex is int index ? playlist.Items[index] : null;
        if (ReferenceEquals(current, _target))
        {
            return;
        }

        CancelCurrent();
        _target = current;
        CurrentMap = null;
        if (current is null)
        {
            return;
        }

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(appCancellation);
        _ = ScanAsync(current, _cancellation.Token);
    }

    public void ApplyCompletions()
    {
        while (_completions.TryDequeue(out var completion))
        {
            if (ReferenceEquals(completion.Entry, _target) && completion.Map is not null)
            {
                CurrentMap = completion.Map;
            }
        }
    }

    private async Task ScanAsync(TrackEntry entry, CancellationToken ct)
    {
        var map = await TrackEnergyScanner.ScanAsync(entry.Path, ct);
        if (!ct.IsCancellationRequested && map is not null)
        {
            _completions.Enqueue(new(entry, map));
        }
    }

    private void CancelCurrent()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose() => CancelCurrent();

    private sealed record ScanCompletion(TrackEntry Entry, TrackEnergyMap? Map);
}
