using System.Windows.Media;

namespace Sinh.App;

internal sealed record FontChoice(string Key, string Label);

internal static class FontCatalog
{
    internal static readonly (string Id, string Name)[] BuiltIns =
    [
        ("roboto", "Roboto"), ("open_sans", "Open Sans"), ("montserrat", "Montserrat"),
        ("inter", "Inter"), ("pt_sans", "PT Sans"), ("nunito", "Nunito"),
        ("rubik", "Rubik"), ("manrope", "Manrope"), ("playfair", "Playfair Display"),
        ("merriweather", "Merriweather")
    ];
    private static readonly Dictionary<string, FontFamily> cache = new(StringComparer.Ordinal);
    internal static FontFamily Resolve(string key)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;
        FontFamily family;
        try
        {
            if (key.StartsWith("builtin:", StringComparison.Ordinal))
            {
                var id = key[8..];
                var entry = BuiltIns.FirstOrDefault(f => f.Id == id);
                // The pinned static Merriweather files retain this legacy name (name ID 1); WPF uses it.
                var familyName = entry.Id == "merriweather" ? "Merriweather Light" : entry.Name;
                family = entry.Id != null
                    ? new FontFamily(AssetUris.Base, $"./Assets/Fonts/{entry.Id}/#{familyName}")
                    : System.Windows.SystemFonts.MessageFontFamily;
            }
            else family = key switch
            {
                "System" => System.Windows.SystemFonts.MessageFontFamily,
                "system:sans" => new FontFamily("Segoe UI"),
                "system:serif" => new FontFamily("Georgia"),
                "system:mono" => new FontFamily("Consolas"),
                "system:hand" => new FontFamily("Segoe Print"),
                _ => new FontFamily(key)
            };
        }
        catch (ArgumentException) { family = System.Windows.SystemFonts.MessageFontFamily; }
        catch (UriFormatException) { family = System.Windows.SystemFonts.MessageFontFamily; }
        if (cache.Count >= 24) cache.Remove(cache.Keys.First());
        cache[key] = family;
        return family;
    }
    internal static IEnumerable<FontChoice> Choices()
    {
        yield return new("System", "Системный");
        foreach (var family in BuiltIns) yield return new("builtin:" + family.Id, family.Name + " · встроенный");
        yield return new("system:sans", "Гротеск · Segoe UI");
        yield return new("system:serif", "С засечками · Georgia");
        yield return new("system:mono", "Моноширинный · Consolas");
        yield return new("system:hand", "Рукописный · Segoe Print");
        foreach (var family in Fonts.SystemFontFamilies.OrderBy(f => f.Source)) yield return new(family.Source, family.Source);
    }
}
