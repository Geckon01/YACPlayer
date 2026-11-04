using System.Collections.Concurrent;
using Yacplayer.Configuration;
using Yacplayer.Library;

namespace Yacplayer.Application;

internal readonly record struct BrowserViewState(
    string Path,
    IReadOnlyList<BrowserEntry> Entries,
    int SelectedIndex,
    bool AtDrives);

internal sealed class BrowserController : IDisposable
{
    private readonly Playlist _playlist;
    private readonly PlayerSettings _settings;
    private readonly HashSet<string> _queuedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<BrowserLoadCompletion> _completions = new();
    private CancellationTokenSource? _loadCancellation;
    private long _loadGeneration;
    private string _path;
    private IReadOnlyList<BrowserEntry> _entries = [];
    private int _selectedIndex;
    private bool _atDrives;

    public BrowserController(Playlist playlist, PlayerSettings settings, string initialPath)
    {
        _playlist = playlist;
        _settings = settings;
        _path = initialPath;
        _queuedPaths.UnionWith(playlist.Items.Select(item => item.Path));
    }

    public BrowserViewState View => new(_path, _entries, _selectedIndex, _atDrives);

    public string StartLoad(string path, CancellationToken appCancellation)
    {
        CancelLoad();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(appCancellation);
        var generation = ++_loadGeneration;
        _path = path;
        _entries = [];
        _selectedIndex = 0;
        _atDrives = false;
        _ = LoadAsync(path, generation, _loadCancellation.Token);
        return $"Loading {path}";
    }

    public bool TryApplyCompletion(out string message)
    {
        message = "";
        var applied = false;
        while (_completions.TryDequeue(out var completion))
        {
            if (completion.Generation != _loadGeneration)
            {
                continue;
            }

            applied = true;
            _loadCancellation?.Dispose();
            _loadCancellation = null;

            if (completion.Error is not null)
            {
                message = $"Cannot open {completion.Path}: {completion.Error}";
                continue;
            }

            _path = completion.Path;
            _entries = completion.Entries;
            _settings.LastDirectory = completion.Path;
            message = completion.Entries.Count == 0
                ? "No audio files or subdirectories (use [..] to go back)"
                : $"{completion.Entries.Count} item(s)";
        }

        return applied;
    }

    public void MoveSelection(int delta)
    {
        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _entries.Count);
    }

    public string GoToParent(CancellationToken ct)
    {
        if (_atDrives)
        {
            return "This PC";
        }

        var parent = Directory.GetParent(_path);
        return parent is not null
            ? StartLoad(parent.FullName, ct)
            : ShowDrives();
    }

    public string ShowDrives()
    {
        CancelLoad();
        _atDrives = true;
        _path = "This PC";
        _selectedIndex = 0;
        _entries = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Select(drive => new BrowserEntry(drive.RootDirectory.FullName, true))
            .ToArray();
        return $"{_entries.Count} drive(s)";
    }

    public TrackEntry? OpenSelection(CancellationToken ct, out string message)
    {
        if (_selectedIndex == 0)
        {
            message = GoToParent(ct);
            return null;
        }

        var entryIndex = _selectedIndex - 1;
        if ((uint)entryIndex >= (uint)_entries.Count)
        {
            message = "No browser entry selected";
            return null;
        }

        var entry = _entries[entryIndex];
        if (entry.IsDirectory)
        {
            message = StartLoad(entry.Path, ct);
            return null;
        }

        var index = FindOrAddTrack(entry.Path);
        message = $"Playing {Path.GetFileName(entry.Path)}";
        return _playlist.Play(index);
    }

    public async Task<string> AppendSelectionAsync(CancellationToken ct)
    {
        if (_selectedIndex == 0)
        {
            return "Parent entry cannot be added";
        }

        var entryIndex = _selectedIndex - 1;
        if ((uint)entryIndex >= (uint)_entries.Count)
        {
            return "No browser entry selected";
        }

        var entry = _entries[entryIndex];
        var paths = entry.IsDirectory
            ? await Task.Run(() => FileBrowser.Scan(entry.Path, _settings.Recursive).ToArray(), ct)
            : [entry.Path];
        var additions = new List<TrackEntry>(paths.Length);
        foreach (var path in paths)
        {
            if (_queuedPaths.Add(path))
            {
                additions.Add(new TrackEntry(path));
            }
        }

        _playlist.AddRange(additions);
        return $"Added {additions.Count} track(s)";
    }

    public void ForgetPathIfUnused(string path)
    {
        if (!_playlist.Items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            _queuedPaths.Remove(path);
        }
    }

    public void CancelLoad()
    {
        _loadGeneration++;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private async Task LoadAsync(string path, long generation, CancellationToken ct)
    {
        try
        {
            var entries = await FileBrowser.EnumerateAsync(path, ct);
            if (!ct.IsCancellationRequested)
            {
                _completions.Enqueue(new(generation, path, entries, null));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                _completions.Enqueue(new(generation, path, [], ex.Message));
            }
        }
    }

    private int FindOrAddTrack(string path)
    {
        for (var index = 0; index < _playlist.Items.Count; index++)
        {
            if (string.Equals(_playlist.Items[index].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        _playlist.Add(new TrackEntry(path));
        _queuedPaths.Add(path);
        return _playlist.Items.Count - 1;
    }

    public void Dispose() => CancelLoad();

    private sealed record BrowserLoadCompletion(
        long Generation,
        string Path,
        IReadOnlyList<BrowserEntry> Entries,
        string? Error);
}
