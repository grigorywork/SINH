using System.Security.Cryptography;
using System.Text;

namespace Sinh.Core;

public sealed class SyncPlanner
{
    public static string Fingerprint(SyncRequest r) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', r.SourceIdentity ?? Path.GetFullPath(r.Source), r.DestinationIdentity ?? Path.GetFullPath(r.Destination), r.Mode, r.AllowDeletes, string.Join('\n', r.Exclusions ?? [])))));
    public SyncPlan Build(SyncRequest request, TreeSnapshot a, TreeSnapshot b, ConfirmedState? baseline = null)
    {
        PathRules.ValidatePair(a.Root, b.Root);
        (a,b)=TreePolicy.ProtectLinks(a,b);
        var fingerprint = Fingerprint(request);
        if (baseline?.Fingerprint != fingerprint) baseline = null;
        var desired = request.Mode switch
        {
            SyncMode.Updates or SyncMode.Mirror => OneWayMerge.Build(request, a, b),
            SyncMode.TwoWay => TwoWayMerge.Build(request, a, b, baseline),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Mode))
        };
        var operations = new List<Operation>();
        if (request.Mode == SyncMode.TwoWay) operations.AddRange(OperationPlanner.Build(a, desired.Entries));
        operations.AddRange(OperationPlanner.Build(b, desired.Entries));
        return new(request, a, b, operations, desired.Conflicts, baseline != null && !b.SameContent(baseline.Destination), fingerprint);
    }
}
