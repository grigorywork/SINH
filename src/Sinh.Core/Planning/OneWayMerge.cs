namespace Sinh.Core;

internal static class OneWayMerge
{
    internal static DesiredTree Build(SyncRequest request, TreeSnapshot a, TreeSnapshot b)
    {
        var desired = new Dictionary<string, DesiredVersion>(StringComparer.Ordinal);
        if (request.Mode == SyncMode.Updates || !request.AllowDeletes)
            foreach (var e in b.Entries.Values) desired.Add(e.RelativePath, new(e, b.Root, e.RelativePath));
        foreach (var e in a.Entries.Values.OrderBy(e => e.RelativePath.Length))
        {
            // Preserve exact case. A case-only path change is implemented as a staged remove + create.
            foreach (var old in desired.Keys.Where(k => string.Equals(k, e.RelativePath, StringComparison.OrdinalIgnoreCase) && k != e.RelativePath).ToArray())
            {
                var children = desired.Keys.Where(k => k.StartsWith(old + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray();
                foreach (var child in children)
                {
                    var value = desired[child]; desired.Remove(child);
                    var renamed = e.RelativePath + child[old.Length..]; desired[renamed] = value with { Entry = value.Entry with { RelativePath = renamed } };
                }
                desired.Remove(old);
            }
            if (desired.TryGetValue(e.RelativePath, out var current) && current.Entry.Directory != e.Directory)
            {
                if (!request.AllowDeletes) throw new IOException("Файл и папка имеют одинаковое имя; для замены требуется явное разрешение удалений: " + e.RelativePath);
                foreach (var key in desired.Keys.Where(k => k.StartsWith(e.RelativePath + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray()) desired.Remove(key);
            }
            desired[e.RelativePath] = new(e, a.Root, e.RelativePath);
        }
        return new(desired, []);
    }
}
