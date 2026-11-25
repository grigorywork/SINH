namespace Sinh.Core;

internal static class TwoWayMerge
{
    internal static DesiredTree Build(SyncRequest request, TreeSnapshot a, TreeSnapshot b, ConfirmedState? baseline)
    {
        var ai = a.Entries.Values.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
        var bi = b.Entries.Values.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
        var ba = baseline?.Source.Entries.Values.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        var bb = baseline?.Destination.Entries.Values.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        var desired = new Dictionary<string, DesiredVersion>(StringComparer.Ordinal);
        var conflicts = new List<Conflict>();
        var names = ai.Keys.Concat(bi.Keys).Concat(ba.Keys).Concat(bb.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length).ToArray();
        var reserved = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Alternative(string path, string hash)
        {
            var ext = Path.GetExtension(path); var stem = path[..^ext.Length];
            if (ext.Length == 0) stem = path;
            var tag = hash.Length >= 8 ? hash[..8] : "folder";
            var candidate = stem + " (USB-" + tag + ")" + ext; var n = 2;
            // Keep the original long filename intact; put the conflicting version in a named subfolder.
            if (Path.GetFileName(candidate).Length > 240)
            {
                candidate = Path.Combine(Path.GetDirectoryName(path) ?? "", "SINH conflicts USB", tag, Path.GetFileName(path));
                while (!reserved.Add(candidate)) candidate = Path.Combine(Path.GetDirectoryName(path) ?? "", "SINH conflicts USB", tag + "-" + n++, Path.GetFileName(path));
                return candidate;
            }
            while (!reserved.Add(candidate)) candidate = stem + " (USB-" + tag + "-" + n++ + ")" + ext;
            return candidate;
        }
        var redirects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var preservedSourceDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Same(Entry? x, Entry? y) => x == null ? y == null : x.SameContent(y) && x.RelativePath == y!.RelativePath;
        void Put(Entry? entry, string root) { if (entry != null) desired[entry.RelativePath] = new(entry, root, entry.RelativePath); }
        foreach (var path in names)
        {
            var av = ai.GetValueOrDefault(path); var bv = bi.GetValueOrDefault(path);
            if (preservedSourceDirectories.Any(parent => path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                Put(av, a.Root);
                continue;
            }
            // Preserve entire alternate B subtree for a file/directory collision.
            var redirect = redirects.Keys.Where(k => path.StartsWith(k + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).OrderByDescending(k => k.Length).FirstOrDefault();
            if (redirect != null && bv != null)
            {
                var renamed = redirects[redirect] + bv.RelativePath[redirect.Length..];
                desired[renamed] = new(bv with { RelativePath = renamed }, b.Root, bv.RelativePath);
                Put(av, a.Root); continue;
            }
            if (av != null && bv != null && av.Directory != bv.Directory)
            {
                Put(av, a.Root); var alternate = Alternative(bv.RelativePath, bv.Hash);
                desired[alternate] = new(bv with { RelativePath = alternate }, b.Root, bv.RelativePath);
                if (bv.Directory) redirects[bv.RelativePath] = alternate;
                else if (av.Directory) preservedSourceDirectories.Add(av.RelativePath);
                conflicts.Add(new(path, "Файл и папка: обе версии сохранены, USB-версия переименована.")); continue;
            }
            var changedA = !Same(av, ba.GetValueOrDefault(path)); var changedB = !Same(bv, bb.GetValueOrDefault(path));
            if (av?.SameContent(bv) == true) { Put(changedB && !changedA ? bv : av, changedB && !changedA ? b.Root : a.Root); continue; }
            if (!changedA && !changedB) { Put(av ?? bv, av != null ? a.Root : b.Root); continue; }
            if (changedA && !changedB) { if (av != null) Put(av, a.Root); else if (!request.AllowDeletes) Put(bv, b.Root); continue; }
            if (!changedA && changedB) { if (bv != null) Put(bv, b.Root); else if (!request.AllowDeletes) Put(av, a.Root); continue; }
            if (av == null || bv == null)
            {
                Put(av ?? bv, av != null ? a.Root : b.Root);
                if (baseline != null && (av != null || bv != null)) conflicts.Add(new(path, "Изменение против удаления: изменённая версия сохранена."));
                continue;
            }
            Put(av, a.Root);
            var alternatePath = Alternative(bv.RelativePath, bv.Hash);
            desired[alternatePath] = new(bv with { RelativePath = alternatePath }, b.Root, bv.RelativePath);
            conflicts.Add(new(path, "Обе изменённые версии сохранены. Версия USB имеет суффикс USB-."));
        }
        // Retain necessary parents when a child was edited while its parent was removed on the other side.
        TreePolicy.PreserveContainers(desired,a,b);
        var directories = desired.Values.Where(v => v.Entry.Directory).ToDictionary(v => v.Entry.RelativePath, StringComparer.OrdinalIgnoreCase);
        var files = desired.Values.Where(v => !v.Entry.Directory).Select(v => v.Entry.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var value in desired.Values.ToArray())
        {
            var parent = Path.GetDirectoryName(value.Entry.RelativePath);
            while (!string.IsNullOrEmpty(parent))
            {
                if (files.Contains(parent)) throw new IOException("Неоднозначный конфликт структуры папок: " + parent);
                if (!directories.ContainsKey(parent))
                {
                    var dir = new DesiredVersion(new(parent, true, 0, "", DateTime.UtcNow), a.Root, parent);
                    desired[parent] = dir; directories[parent] = dir;
                }
                parent = Path.GetDirectoryName(parent);
            }
        }
        var normalized = new Dictionary<string, DesiredVersion>(StringComparer.Ordinal);
        foreach (var value in desired.Values.OrderBy(v => v.Entry.RelativePath.Length))
        {
            var path = value.Entry.RelativePath; var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && directories.TryGetValue(parent, out var canonical)) path = Path.Combine(canonical.Entry.RelativePath, Path.GetFileName(path));
            var canonicalValue = value with { Entry = value.Entry with { RelativePath = path } };
            normalized[path] = canonicalValue;
            if (canonicalValue.Entry.Directory) directories[path] = canonicalValue;
        }
        return new(normalized, conflicts);
    }
}
