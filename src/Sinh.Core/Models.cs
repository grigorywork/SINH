using System.Collections.ObjectModel;

namespace Sinh.Core;

public enum SyncMode { Updates, Mirror, TwoWay }
public enum OperationKind { DeleteFile, DeleteDirectory, CreateDirectory, CopyFile }
public sealed record Entry(string RelativePath, bool Directory, long Length, string Hash, DateTime LastWriteUtc)
{
    public bool SameContent(Entry? other) => other != null && Directory == other.Directory && (Directory || (Length == other.Length && Hash == other.Hash));
}
public sealed record TreeSnapshot(string Root, IReadOnlyDictionary<string, Entry> Entries)
{
    public static TreeSnapshot Create(string root, IEnumerable<Entry> entries) => new(root, new ReadOnlyDictionary<string, Entry>(entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal)));
    public Entry? Find(string path) => Entries.GetValueOrDefault(path);
    public bool SameContent(TreeSnapshot other) => Entries.Count == other.Entries.Count && Entries.All(e => e.Value.SameContent(other.Find(e.Key)));
}
public sealed record SyncRequest(Guid PairId, string Source, string Destination, SyncMode Mode = SyncMode.Updates, bool AllowDeletes = false, string[]? Exclusions = null, string? SourceIdentity = null, string? DestinationIdentity = null);
public sealed record Operation(OperationKind Kind, string Root, string RelativePath, Entry? Before, Entry? After, string? SourceRoot = null, string? SourceRelativePath = null)
{
    public string Action => Kind switch { OperationKind.CopyFile => Before == null ? "Добавить" : "Обновить", OperationKind.CreateDirectory => "Создать папку", OperationKind.DeleteDirectory => "Удалить папку", _ => "Удалить файл" };
    public string Path => System.IO.Path.Combine(Root, RelativePath);
    public long Bytes => After?.Length ?? 0;
}
public sealed record Conflict(string RelativePath, string Resolution);
public sealed record SyncPlan(SyncRequest Request, TreeSnapshot Source, TreeSnapshot Destination, IReadOnlyList<Operation> Operations, IReadOnlyList<Conflict> Conflicts, bool DestinationChanged, string Fingerprint)
{
    public int Deletes => Operations.Count(o => o.Kind is OperationKind.DeleteFile or OperationKind.DeleteDirectory);
    public long CopyBytes => Operations.Where(o => o.Kind == OperationKind.CopyFile).Sum(o => o.Bytes);
}
public sealed record ConfirmedState(string Fingerprint, TreeSnapshot Source, TreeSnapshot Destination, DateTimeOffset ConfirmedAt);
public sealed record SyncProgress(string Stage, string Path, long Completed = 0, long Total = 0);
