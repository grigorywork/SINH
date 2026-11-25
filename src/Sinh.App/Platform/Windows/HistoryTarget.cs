using System.IO;
using Sinh.Core;
using Sinh.Settings;

namespace Sinh.App;

internal sealed record HistoryTarget(HistoryEntry Entry, string Root, Func<bool> Available)
{
    internal static HistoryTarget Resolve(HistoryEntry entry, FolderPair pair)
    {
        var mounts = WindowsVolumes.Mounts();
        foreach (var destination in new[] { false, true })
        {
            var identity = destination ? WindowsVolumes.StableDestination(pair) : WindowsVolumes.StableSource(pair);
            var configured = destination ? pair.Destination : pair.Source;
            if (entry.RootIdentity != null ? entry.RootIdentity != identity : !PathRules.Contains(configured, entry.OriginalPath)) continue;
            var root = WindowsVolumes.ResolveSide(pair, destination, mounts) ?? throw new IOException("Носитель для восстановления недоступен или изменился.");
            var relative = entry.RootIdentity != null ? entry.Version.RelativePath : PathRules.RelativeTo(configured, entry.OriginalPath);
            var stableRoot = PathRules.StableRoot(root);
            var target = PathRules.Under(stableRoot, relative);
            return new(entry with { OriginalPath = target }, stableRoot, () => Directory.Exists(stableRoot) && (identity == null || WindowsVolumes.IdentityMatches(identity, stableRoot)));
        }
        throw new IOException("Исходная папка этой версии больше не входит в текущую пару.");
    }
}
