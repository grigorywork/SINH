using System.Security.Cryptography;
using System.Text;

namespace Sinh.Core;

public sealed class SyncPlanner
{
    private sealed record Version(Entry Entry, string Root, string Path);
    public static string Fingerprint(SyncRequest r) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Path.GetFullPath(r.Source), Path.GetFullPath(r.Destination), r.Mode, r.AllowDeletes, string.Join('\n', r.Exclusions ?? [])))));
    public SyncPlan Build(SyncRequest request, TreeSnapshot a, TreeSnapshot b, ConfirmedState? baseline = null)
    {
        PathRules.ValidatePair(a.Root, b.Root);
        if (request.Mode == SyncMode.TwoWay) throw new NotSupportedException("Двусторонний обмен ещё не включён. Используйте копирование обновлений или зеркало.");
        var fingerprint = Fingerprint(request);
        if (baseline?.Fingerprint != fingerprint) baseline = null;
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
