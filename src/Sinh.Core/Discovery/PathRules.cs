namespace Sinh.Core;

public static class PathRules
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static string Root(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Не указан путь папки.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        FileAttributes attributes;
        // Exists() suppresses access/I/O errors and labels them all as a missing folder.
        try { attributes = File.GetAttributes(full); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new RootAccessException(full, RootFailure.Missing, ex); }
        catch (UnauthorizedAccessException ex) { throw new RootAccessException(full, RootFailure.AccessDenied, ex); }
        catch (IOException ex) { throw new RootAccessException(full, RootFailure.Unavailable, ex); }
        if ((attributes & FileAttributes.Directory) == 0) throw new RootAccessException(full, RootFailure.NotDirectory);
        RejectLinks(full); return full;
    }
    /// <summary>Use a volume GUID or UNC path for Windows I/O so a reused drive letter cannot redirect a commit.</summary>
    public static string StableRoot(string path)
    {
        var root = Root(path);
        return OperatingSystem.IsWindows() ? WindowsPathIdentity.Comparable(root) : root;
    }
    public static void ValidatePair(string a, string b)
    {
        a = Root(a); b = Root(b);
        if (string.Equals(Path.TrimEndingDirectorySeparator(b), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(b)!), Comparison)) throw new IOException("Для копии выберите отдельную папку на носителе, а не весь диск. Остальные файлы на флешке должны оставаться вне пары.");
        if (ContainsLexical(a, b) || ContainsLexical(b, a)) throw new IOException("Корни совпадают или вложены друг в друга. Выберите независимые папки.");
        if (OperatingSystem.IsWindows()) WindowsPathIdentity.RejectOverlap(a, b);
    }
    public static bool Contains(string root, string path)
    {
        if (ContainsLexical(root, path)) return true;
        return OperatingSystem.IsWindows() && WindowsPathIdentity.TryRelative(root, path, out _);
    }
    private static string Logical(string path)
    {
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + full[8..];
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) && full.Length > 6 && full[5] == ':') return full[4..];
        }
        return full;
    }
    private static bool ContainsLexical(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Logical(root)); path = Logical(path);
        return string.Equals(root, path, Comparison) || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, Comparison);
    }
    public static string RelativeTo(string root, string path)
    {
        if (ContainsLexical(root, path)) return Path.GetRelativePath(Logical(root), Logical(path));
        if (OperatingSystem.IsWindows() && WindowsPathIdentity.TryRelative(root, path, out var relative)) return relative;
        throw new IOException("Путь выходит за разрешённый корень.");
    }
    public static string Under(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new IOException("Недопустимый относительный путь.");
        if (relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Any(part => part is "." or "..")) throw new IOException("Недопустимый относительный путь.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!Contains(root, path) || string.Equals(Path.TrimEndingDirectorySeparator(root), path, Comparison)) throw new IOException("Путь выходит за разрешённый корень.");
        RejectLinks(path); return path;
    }
    public static void RejectLinks(string path)
    {
        // Inspect the entire ancestor chain, including junctions and the configured root.
        var current = Path.GetFullPath(path);
        while (current != null)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Ссылки и junction не обрабатываются: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }
}
