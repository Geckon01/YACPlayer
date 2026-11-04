namespace Yacplayer.Library;

public sealed record BrowserEntry(string Path, bool IsDirectory);

public static class FileBrowser
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav",
        ".mp3",
        ".m4a",
        ".aac",
        ".wma",
        ".flac"
    };

    public static Task<IReadOnlyList<BrowserEntry>> EnumerateAsync(
        string path,
        CancellationToken ct = default) => Task.Run<IReadOnlyList<BrowserEntry>>(
        () => Enumerate(path, ct),
        ct);

    public static IEnumerable<string> Scan(string root, bool recursive)
    {
        if (File.Exists(root))
        {
            if (Extensions.Contains(System.IO.Path.GetExtension(root)))
            {
                yield return System.IO.Path.GetFullPath(root);
            }

            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.TryPop(out var dir))
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (Extensions.Contains(System.IO.Path.GetExtension(file)))
                {
                    yield return System.IO.Path.GetFullPath(file);
                }
            }

            if (!recursive)
            {
                continue;
            }

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var child in dirs)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
                catch
                {
                }
            }
        }
    }

    private static IReadOnlyList<BrowserEntry> Enumerate(string path, CancellationToken ct)
    {
        try
        {
            var entries = new List<BrowserEntry>();
            foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();

                bool isDirectory;
                try
                {
                    isDirectory = (item.Attributes & FileAttributes.Directory) != 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (isDirectory || Extensions.Contains(item.Extension))
                {
                    entries.Add(new BrowserEntry(item.FullName, isDirectory));
                }
            }

            entries.Sort(static (left, right) =>
            {
                var directoryOrder = right.IsDirectory.CompareTo(left.IsDirectory);
                return directoryOrder != 0
                    ? directoryOrder
                    : StringComparer.OrdinalIgnoreCase.Compare(
                        System.IO.Path.GetFileName(left.Path),
                        System.IO.Path.GetFileName(right.Path));
            });
            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
