using System.Security.Cryptography;
using System.Text;

namespace Sinh.Core;

public sealed class SyncPlanner
{
    private sealed record Version(Entry Entry, string Root, string Path);
    public static string Fingerprint(SyncRequest r) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', r.SourceIdentity ?? Path.GetFullPath(r.Source), r.DestinationIdentity ?? Path.GetFullPath(r.Destination), r.Mode, r.AllowDeletes, string.Join('\n', r.Exclusions ?? [])))));
    public SyncPlan Build(SyncRequest request, TreeSnapshot a, TreeSnapshot b, ConfirmedState? baseline = null)
    {
        PathRules.ValidatePair(a.Root, b.Root);
        var fingerprint = Fingerprint(request);
        if (baseline?.Fingerprint != fingerprint) baseline = null;
        if (request.Mode == SyncMode.TwoWay) return BuildTwoWay(request, a, b, baseline, fingerprint);
        var desired = new Dictionary<string, Version>(StringComparer.Ordinal);
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
        var operations = PlanSide(b, desired);
        var changed = baseline != null && !b.SameContent(baseline.Destination);
        return new(request, a, b, operations, [], changed, fingerprint);
    }
    private static SyncPlan BuildTwoWay(SyncRequest request, TreeSnapshot a, TreeSnapshot b, ConfirmedState? baseline, string fingerprint)
    {
        var ai = a.Entries.Values.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
        var bi = b.Entries.Values.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
        var ba = baseline?.Source.Entries.Values.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        var bb = baseline?.Destination.Entries.Values.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        var desired = new Dictionary<string, Version>(StringComparer.Ordinal);
        var conflicts = new List<Conflict>();
        var names = ai.Keys.Concat(bi.Keys).Concat(ba.Keys).Concat(bb.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length).ToArray();
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
                while (names.Contains(candidate, StringComparer.OrdinalIgnoreCase) || desired.Keys.Contains(candidate, StringComparer.OrdinalIgnoreCase)) candidate = Path.Combine(Path.GetDirectoryName(path) ?? "", "SINH conflicts USB", tag + "-" + n++, Path.GetFileName(path));
                return candidate;
            }
            while (names.Contains(candidate, StringComparer.OrdinalIgnoreCase) || desired.Keys.Contains(candidate, StringComparer.OrdinalIgnoreCase)) candidate = stem + " (USB-" + tag + "-" + n++ + ")" + ext;
            return candidate;
        }
        var redirects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var preservedSourceDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Same(Entry? x, Entry? y) => x == null ? y == null : x.SameContent(y) && x.RelativePath == y!.RelativePath;
        void Put(Entry? entry, string root) { if (entry != null) desired[entry.RelativePath] = new(entry, root, entry.RelativePath); }
        foreach (var path in names)
        {
            var av = ai.GetValueOrDefault(path); var bv = bi.GetValueOrDefault(path);
            if(preservedSourceDirectories.Any(parent=>path.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
            {
                Put(av,a.Root);
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
                else if(av.Directory)preservedSourceDirectories.Add(av.RelativePath);
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
        var directories = desired.Values.Where(v => v.Entry.Directory).ToDictionary(v => v.Entry.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var value in desired.Values.ToArray())
        {
            var parent = Path.GetDirectoryName(value.Entry.RelativePath);
            while (!string.IsNullOrEmpty(parent))
            {
                if (desired.Values.Any(v => !v.Entry.Directory && string.Equals(v.Entry.RelativePath, parent, StringComparison.OrdinalIgnoreCase))) throw new IOException("Неоднозначный конфликт структуры папок: " + parent);
                if (!directories.ContainsKey(parent))
                {
                    var dir = new Version(new(parent, true, 0, "", DateTime.UtcNow), a.Root, parent);
                    desired[parent] = dir; directories[parent] = dir;
                }
                parent = Path.GetDirectoryName(parent);
            }
        }
        var normalized = new Dictionary<string, Version>(StringComparer.Ordinal);
        foreach (var value in desired.Values.OrderBy(v => v.Entry.RelativePath.Length))
        {
            var path = value.Entry.RelativePath; var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && directories.TryGetValue(parent, out var canonical)) path = Path.Combine(canonical.Entry.RelativePath, Path.GetFileName(path));
            var canonicalValue = value with { Entry = value.Entry with { RelativePath = path } };
            normalized[path] = canonicalValue;
            if (canonicalValue.Entry.Directory) directories[path] = canonicalValue;
        }
        var operations = PlanSide(a, normalized); operations.AddRange(PlanSide(b, normalized));
        return new(request, a, b, operations, conflicts, baseline != null && !b.SameContent(baseline.Destination), fingerprint);
    }
    private static List<Operation> PlanSide(TreeSnapshot before, Dictionary<string, Version> desired)
    {
        var operations = new List<Operation>();
        foreach (var entry in before.Entries.Values.OrderByDescending(e => e.RelativePath.Length))
        {
            if (!desired.TryGetValue(entry.RelativePath, out var next) || next.Entry.Directory != entry.Directory)
                operations.Add(new(entry.Directory ? OperationKind.DeleteDirectory : OperationKind.DeleteFile, before.Root, entry.RelativePath, entry, null));
        }
        foreach (var next in desired.Values.OrderBy(e => e.Entry.RelativePath.Length))
        {
            var entry = next.Entry; var old = before.Find(entry.RelativePath);
            if (entry.Directory)
            {
                if (old?.Directory != true) operations.Add(new(OperationKind.CreateDirectory, before.Root, entry.RelativePath, null, entry));
            }
            else if (!entry.SameContent(old)) operations.Add(new(OperationKind.CopyFile, before.Root, entry.RelativePath, old?.Directory == false ? old : null, entry, next.Root, next.Path));
        }
        return operations;
    }
}
