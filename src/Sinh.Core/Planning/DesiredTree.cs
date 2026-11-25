namespace Sinh.Core;

internal sealed record DesiredVersion(Entry Entry, string Root, string Path);
internal sealed record DesiredTree(Dictionary<string, DesiredVersion> Entries, IReadOnlyList<Conflict> Conflicts);
