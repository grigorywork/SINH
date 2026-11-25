namespace Sinh.Settings;

/// <summary>Maps an explicit folder selection to independent profile roots without changing names.</summary>
public static class FolderSelection
{
    public static IReadOnlyList<string> Accumulate(IEnumerable<string> existing, IEnumerable<string> added) => existing.Concat(added).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
    public sealed record Roots(string Source, string Destination);
    public static IReadOnlyList<Roots> Map(IEnumerable<string> sources, string destination, bool grouped)
    {
        var selected = sources.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0) throw new ArgumentException("Выберите хотя бы одну папку.");
        if (!grouped && selected.Length != 1) throw new ArgumentException("Для нескольких папок выберите общую папку назначения.");
        var parent = Path.GetFullPath(destination);
        var result = selected.Select(source => new Roots(source, grouped ? Path.Combine(parent, Path.GetFileName(Path.TrimEndingDirectorySeparator(source))) : parent)).ToArray();
        if (grouped && result.Any(p => string.IsNullOrEmpty(Path.GetFileName(Path.TrimEndingDirectorySeparator(p.Source))))) throw new ArgumentException("Выберите папки, а не весь диск.");
        if (result.Select(p => p.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length) throw new ArgumentException("У выбранных папок одинаковые имена. Добавьте их отдельно с разными назначениями.");
        return result;
    }
}
