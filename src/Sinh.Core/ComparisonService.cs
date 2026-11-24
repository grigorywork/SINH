namespace Sinh.Core;

public sealed class ComparisonService
{
    private readonly TreeScanner scanner = new();
    private readonly SyncPlanner planner = new();
    public async Task<SyncPlan> CompareAsync(SyncRequest request, ConfirmedState? baseline = null, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        PathRules.ValidatePair(request.Source, request.Destination);
        var a = await scanner.ScanAsync(request.Source, request.Exclusions, progress, cancellationToken).ConfigureAwait(false);
        var b = await scanner.ScanAsync(request.Destination, request.Exclusions, progress, cancellationToken).ConfigureAwait(false);
        return planner.Build(request, a, b, baseline);
    }
}
