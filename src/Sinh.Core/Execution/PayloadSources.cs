namespace Sinh.Core;

internal sealed record PayloadSources(IReadOnlyDictionary<string, string> Files, bool Staged)
{
    internal static async Task<PayloadSources> PrepareAsync(SyncPlan plan, string work, ExecutionOptions options, IProgress<SyncProgress>? progress, CancellationToken token)
    {
        // Stream from roots this plan never modifies. Preserve payloads from mutable roots before any mutation.
        // A case-only rename can need B's extra files even in one-way Updates; A still needs no local duplicate.
        var changedRoots = plan.Operations.Select(o => o.Root).ToHashSet(StringComparer.Ordinal);
        var stagedAny = false;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operation in plan.Operations.Where(o => o.Kind == OperationKind.CopyFile))
        {
            token.ThrowIfCancellationRequested(); var entry = operation.After!;
            var origin = operation.SourceRoot == plan.Source.Root ? plan.Source : operation.SourceRoot == plan.Destination.Root ? plan.Destination : null;
            if (origin == null || operation.SourceRelativePath == null || !entry.SameContent(origin.Find(operation.SourceRelativePath))) throw new IOException("Источник операции не входит в подтверждённый снимок пары.");
            if (files.ContainsKey(entry.Hash)) continue;
            var source = PathRules.Under(operation.SourceRoot!, operation.SourceRelativePath);
            if (!changedRoots.Contains(operation.SourceRoot!)) { files.Add(entry.Hash, source); continue; }
            stagedAny = true;
            var staged = Path.Combine(work, Guid.NewGuid().ToString("N") + ".data");
            await VerifiedCopy.CopyAsync(source, staged, entry, options.SpeedLimitMiB, progress, "Подготовка копии", token).ConfigureAwait(false);
            var verified = await TreeScanner.ReadFileAsync(staged, entry.RelativePath, token).ConfigureAwait(false);
            if (!entry.SameContent(verified)) throw new IOException("Не пройдена проверка подготовленной копии.");
            files.Add(entry.Hash, staged);
        }
        return new(files, stagedAny);
    }
}
