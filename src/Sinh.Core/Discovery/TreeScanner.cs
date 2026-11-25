using System.IO.Enumeration;
using System.Security.Cryptography;

namespace Sinh.Core;

public sealed class TreeScanner
{
    public async Task<TreeSnapshot> ScanAsync(string path, string[]? exclusions = null, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var root = PathRules.Root(path);
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>(); var links = new List<string>(); var rules = new ExclusionRules(exclusions);
        var pending = new Stack<string>(); pending.Push(root);
        long bytes = 0;
        var lastReport = Environment.TickCount64;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested(); PathRules.RejectLinks(directory);
            // Enumeration errors are fatal: a partial snapshot must never produce a deletion plan.
            foreach (var full in System.IO.Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, full);
                if (rules.Matches(relative)) { skipped.Add(relative); continue; }
                var attributes = File.GetAttributes(full);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped.Add(relative); links.Add(relative); continue; }
                PathRules.RejectLinks(full);
                if (!names.Add(relative)) throw new IOException("Имена отличаются только регистром в одном каталоге; автоматическое применение заблокировано: " + relative);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                Entry entry;
                if (isDirectory) { entry = new(relative, true, 0, "", System.IO.Directory.GetLastWriteTimeUtc(full)); pending.Push(full); }
                else { entry = await ReadFileAsync(full, relative, cancellationToken).ConfigureAwait(false); bytes += entry.Length; }
                entries.Add(relative, entry);
                if (Environment.TickCount64 - lastReport > 100) { progress?.Report(new("Сканирование", relative, bytes)); lastReport = Environment.TickCount64; }
            }
        }
        PathRules.Root(root);
        progress?.Report(new("Сканирование завершено", root, bytes, bytes));
        return TreeSnapshot.Create(root, entries.Values, skipped, links);
    }
    public static async Task<Entry> ReadFileAsync(string path, string relative, CancellationToken cancellationToken = default)
    {
        PathRules.RejectLinks(path);
        var before = new FileInfo(path); var length = before.Length; var time = before.LastWriteTimeUtc;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
        var after = new FileInfo(path);
        if (input.Length != length || after.Length != length || after.LastWriteTimeUtc != time) throw new IOException("Файл изменился во время чтения: " + path);
        return new(relative, false, length, hash, time);
    }
}
