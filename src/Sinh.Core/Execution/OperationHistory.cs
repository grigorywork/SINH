namespace Sinh.Core;

/// <summary>Optional old versions and mandatory recovery copies for destructive steps that still feed later copies.</summary>
internal sealed class OperationHistory(SyncPlan plan, ExecutionOptions options, string dataDirectory)
{
    private readonly HistoryStore history = new(dataDirectory);
    private readonly HashSet<(string? Root, string? Path)> sources = options.History ? [] : plan.Operations
        .Where(o => o.Kind == OperationKind.CopyFile).Select(o => (o.SourceRoot, o.SourceRelativePath)).ToHashSet();
    private readonly List<HistoryEntry> safetyCopies = [];

    internal async Task PreserveAsync(Operation operation, string target, CancellationToken token)
    {
        if (operation.Before is not { Directory: false } old) return;
        var safety = !options.History && sources.Contains((operation.Root, operation.RelativePath));
        if (!options.History && !safety) return;
        var identity = operation.Root == plan.Source.Root ? plan.Request.SourceIdentity : plan.Request.DestinationIdentity;
        var entry = await history.ArchiveAsync(plan.Request.PairId, target, old, options.HistoryLimitBytes, token, identity, safety).ConfigureAwait(false);
        if (safety) safetyCopies.Add(entry);
    }

    internal async Task CompleteAsync(CancellationToken token, Action<string> warning)
    {
        // Only after verified data and baseline commit. Cancellation/failure leaves recoverable entries in History.
        foreach (var entry in safetyCopies)
        {
            if (token.IsCancellationRequested) break;
            try { await history.DeleteAsync(entry).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warning(ex.Message); }
        }
    }
}
