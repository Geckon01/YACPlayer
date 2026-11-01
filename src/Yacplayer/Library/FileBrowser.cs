namespace Yacplayer.Library;
public sealed record BrowserEntry(string Path, bool IsDirectory);
public static class FileBrowser
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac" };
    public static Task<IReadOnlyList<BrowserEntry>> EnumerateAsync(string path, CancellationToken ct = default) => Task.Run<IReadOnlyList<BrowserEntry>>(() =>
    {
        try { return Directory.EnumerateFileSystemEntries(path).Select(p => new BrowserEntry(p, Directory.Exists(p))).Where(e => e.IsDirectory || Extensions.Contains(System.IO.Path.GetExtension(e.Path))).OrderByDescending(e => e.IsDirectory).ThenBy(e => System.IO.Path.GetFileName(e.Path), StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }, ct);
    public static IEnumerable<string> Scan(string root, bool recursive)
    {
        if (File.Exists(root)) { if (Extensions.Contains(System.IO.Path.GetExtension(root))) yield return System.IO.Path.GetFullPath(root); yield break; }
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var dir))
        {
            string[] files; try { files = Directory.GetFiles(dir); } catch { continue; }
            foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase)) if (Extensions.Contains(System.IO.Path.GetExtension(file))) yield return System.IO.Path.GetFullPath(file);
            if (!recursive) continue;
            string[] dirs; try { dirs = Directory.GetDirectories(dir); } catch { continue; }
            foreach (var child in dirs) try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); } catch { }
        }
    }
}
