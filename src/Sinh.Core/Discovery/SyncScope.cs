namespace Sinh.Core;

public static class SyncScope
{
    public static bool IsInternalTemporaryName(string path)
    {
        var name = Path.GetFileName(path).AsSpan();
        var prefix = name.StartsWith(".sinh-restore-", StringComparison.OrdinalIgnoreCase) ? 14 : name.StartsWith(".sinh-", StringComparison.OrdinalIgnoreCase) ? 6 : 0;
        return prefix > 0 && name.Length == prefix + 32 + 4 && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(name.Slice(prefix, 32), "N", out _);
    }
    public static SyncRequest ExcludeInternalDirectory(SyncRequest request, string dataDirectory)
    {
        var patterns = (request.Exclusions ?? []).ToList();
        foreach (var root in new[] { request.Source, request.Destination })
        {
            if (!PathRules.Contains(root, dataDirectory)) continue;
            var relative = PathRules.RelativeTo(root, dataDirectory);
            if (relative == ".") throw new IOException("Служебную папку SINH нельзя использовать как корень синхронизации.");
            patterns.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
        }
        return request with { Exclusions = patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    }
    internal static void ValidateInternalDirectory(SyncRequest request, string dataDirectory)
    {
        var rules = new ExclusionRules(request.Exclusions);
        foreach (var root in new[] { request.Source, request.Destination })
        {
            if (!PathRules.Contains(root, dataDirectory)) continue;
            var relative = PathRules.RelativeTo(root, dataDirectory);
            if (relative == "." || !rules.MatchesPathOrParent(relative)) throw new IOException("Служебная область SINH должна быть исключена из выбранной пары.");
        }
    }
}
