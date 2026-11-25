namespace Sinh.Core;

internal sealed record PayloadSources(IReadOnlyDictionary<string, string> Files, bool Staged)
{
    internal static async Task<PayloadSources> PrepareAsync(SyncPlan plan, string work, ExecutionOptions options, IProgress<SyncProgress>? progress, CancellationToken token)
    {
        // One-way sources are never modified by this plan: stream straight to a verified destination temporary.
        // Two-way plans retain staging because an earlier operation can replace a later operation's source.
        var direct = plan.Request.Mode != SyncMode.TwoWay
            && plan.Operations.All(o => o.Root == plan.Destination.Root && (o.Kind != OperationKind.CopyFile || o.SourceRoot == plan.Source.Root));
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operation in plan.Operations.Where(o => o.Kind == OperationKind.CopyFile))
        {
            token.ThrowIfCancellationRequested(); var entry = operation.After!;
            var origin = operation.SourceRoot == plan.Source.Root ? plan.Source : operation.SourceRoot == plan.Destination.Root ? plan.Destination : null;
            if (origin == null || operation.SourceRelativePath == null || !entry.SameContent(origin.Find(operation.SourceRelativePath))) throw new IOException("Источник операции не входит в подтверждённый снимок пары.");
            if (files.ContainsKey(entry.Hash)) continue;
            var source = PathRules.Under(operation.SourceRoot!, operation.SourceRelativePath);
            if (direct) { files.Add(entry.Hash, source); continue; }
            var staged = Path.Combine(work, Guid.NewGuid().ToString("N") + ".data");
            await VerifiedCopy.CopyAsync(source, staged, entry, options.SpeedLimitMiB, progress, "Подготовка копии", token).ConfigureAwait(false);
            var verified = await TreeScanner.ReadFileAsync(staged, entry.RelativePath, token).ConfigureAwait(false);
            if (!entry.SameContent(verified)) throw new IOException("Не пройдена проверка подготовленной копии.");
            files.Add(entry.Hash, staged);
        }
        return new(files, !direct);
    }
}
