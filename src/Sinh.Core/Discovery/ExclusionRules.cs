using System.IO.Enumeration;

namespace Sinh.Core;

public sealed class ExclusionRules(IEnumerable<string>? patterns)
{
    private readonly string[] patterns = (patterns ?? []).Select(p => p.Replace('\\', '/')).ToArray();
    public bool Matches(string relative)
    {
        var normalized = relative.Replace(Path.DirectorySeparatorChar, '/'); var name = Path.GetFileName(relative);
        return patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, normalized, true) || FileSystemName.MatchesSimpleExpression(p, name, true));
    }
    public bool MatchesPathOrParent(string relative)
    {
        var current = relative;
        while (!string.IsNullOrEmpty(current)) { if (Matches(current)) return true; current = Path.GetDirectoryName(current); }
        return false;
    }
}
