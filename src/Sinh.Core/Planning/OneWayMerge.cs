namespace Sinh.Core;

internal static class OneWayMerge
{
    internal static DesiredTree Build(SyncRequest request, TreeSnapshot a, TreeSnapshot b)
    {
        var desired = new Dictionary<string, DesiredVersion>(StringComparer.Ordinal);
        if (request.Mode == SyncMode.Updates || !request.AllowDeletes)
            foreach (var e in b.Entries.Values) desired.Add(e.RelativePath, new(e, b.Root, e.RelativePath));
        TreePolicy.PreserveContainers(desired, b);
        var protectedParents = TreePolicy.ProtectedParents(a, b);
        var aliases = desired.Keys.ToDictionary(path => path, path => path, StringComparer.OrdinalIgnoreCase);
        foreach (var e in a.Entries.Values.OrderBy(e => e.RelativePath.Length))
        {
            // Preserve exact case. A case-only path change is implemented as a staged remove + create.
            if (aliases.TryGetValue(e.RelativePath, out var old) && old != e.RelativePath)
            {
                if (protectedParents.Contains(old)) throw new IOException("Изменение регистра затрагивает каталог с исключёнными данными: " + old);
                var children = desired.Keys.Where(k => k.StartsWith(old + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray();
                foreach (var child in children)
                {
                    var value = desired[child]; desired.Remove(child); aliases.Remove(child);
                    var renamed = e.RelativePath + child[old.Length..]; desired[renamed] = value with { Entry = value.Entry with { RelativePath = renamed } }; aliases[renamed] = renamed;
                }
                desired.Remove(old); aliases.Remove(old);
            }
            if (desired.TryGetValue(e.RelativePath, out var current) && current.Entry.Directory != e.Directory)
            {
                if (protectedParents.Contains(e.RelativePath)) throw new IOException("Замена затрагивает каталог с исключёнными данными: " + e.RelativePath);
                if (!request.AllowDeletes) throw new IOException("Файл и папка имеют одинаковое имя; для замены требуется явное разрешение удалений: " + e.RelativePath);
                foreach (var key in desired.Keys.Where(k => k.StartsWith(e.RelativePath + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray()) { desired.Remove(key); aliases.Remove(key); }
            }
            desired[e.RelativePath] = new(e, a.Root, e.RelativePath); aliases[e.RelativePath] = e.RelativePath;
        }
        return new(desired, []);
    }
}
