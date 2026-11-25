using System.IO;
using Sinh.Core;
using Sinh.Settings;

namespace Sinh.App;

internal static class PairBoundaries
{
    internal static void Validate(FolderPair pair, IEnumerable<FolderPair> others)
    {
        var mounts = WindowsVolumes.Mounts();
        var current = WindowsVolumes.Resolve(pair, mounts) ?? pair;
        static bool Overlap(string a, string b) => PathRules.Contains(a, b) || PathRules.Contains(b, a);
        foreach (var configured in others.Where(p => p.Id != current.Id))
        {
            var other = WindowsVolumes.Resolve(configured, mounts);
            if (other == null) continue; // Rechecked before each run when that volume is available.
            if (Overlap(current.Destination, other.Destination) || Overlap(current.Destination, other.Source) || Overlap(current.Source, other.Destination)
                || ((current.Mode == Sinh.Settings.SyncMode.TwoWay || other.Mode == Sinh.Settings.SyncMode.TwoWay) && Overlap(current.Source, other.Source)))
                throw new IOException("Пара пересекается с назначением другой пары: " + other.Name + ". Выберите независимые папки копий.");
        }
    }
}
