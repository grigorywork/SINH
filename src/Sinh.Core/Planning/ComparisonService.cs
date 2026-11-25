namespace Sinh.Core;

public sealed class ComparisonService
{
    private readonly TreeScanner scanner = new();
    private readonly SyncPlanner planner = new();
    public async Task<SyncPlan> CompareAsync(SyncRequest request, ConfirmedState? baseline = null, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var (a, b) = await ScanPairAsync(request, progress, cancellationToken).ConfigureAwait(false);
        return planner.Build(request, a, b, baseline);
    }
    internal async Task<(TreeSnapshot A, TreeSnapshot B)> ScanPairAsync(SyncRequest request, IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
    {
        PathRules.ValidatePair(request.Source, request.Destination);
        var a = await scanner.ScanAsync(request.Source, request.Exclusions, progress, cancellationToken).ConfigureAwait(false);
        var b = await scanner.ScanAsync(request.Destination, request.Exclusions, progress, cancellationToken).ConfigureAwait(false);
        return TreePolicy.ProtectLinks(a, b);
    }
}
