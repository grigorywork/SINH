namespace Sinh.Core;

internal static class OperationPlanner
{
    internal static List<Operation> Build(TreeSnapshot before, Dictionary<string, DesiredVersion> desired)
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
