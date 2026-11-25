namespace Sinh.Core;

public static class PathRules
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static string Root(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Не указан путь папки.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!System.IO.Directory.Exists(full)) throw new IOException("Папка недоступна (диск отключён или нет доступа): " + full);
        RejectLinks(full); return full;
    }
    public static void ValidatePair(string a, string b)
    {
        a = Root(a); b = Root(b);
        if (string.Equals(Path.TrimEndingDirectorySeparator(b), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(b)!), Comparison)) throw new IOException("Для копии выберите отдельную папку на носителе, а не весь диск. Остальные файлы на флешке должны оставаться вне пары.");
        if (Contains(a, b) || Contains(b, a)) throw new IOException("Корни совпадают или вложены друг в друга. Выберите независимые папки.");
        if (OperatingSystem.IsWindows()) WindowsPathIdentity.RejectOverlap(a, b);
    }
    public static bool Contains(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); path = Path.GetFullPath(path);
        return string.Equals(root, path, Comparison) || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, Comparison);
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
