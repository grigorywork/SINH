using System.IO;
using System.Windows;
using Sinh.Core;
using Sinh.Settings;

namespace Sinh.App;

/// <summary>One editing and persistence path shared by the main window and settings.</summary>
internal static class FolderPairEditor
{
    internal static async Task EditAsync(Window owner, SettingsStore store, FolderPair? original)
    {
        var displayed = original == null ? null : await Task.Run(() => WindowsVolumes.Resolve(original) ?? original);
        var dialog = new PairWindow(displayed) { Owner = owner };
        if (dialog.ShowDialog() != true) return;
        var results = dialog.Results;
        await Task.Run(() => Validate(results, store.Current.Pairs));
        await store.UpdateAsync(s =>
        {
            // Revalidate against the latest snapshot before writing; tray pause changes survive editing.
            Validate(results, s.Pairs);
            var replacements = results.ToDictionary(p => p.Id);
            return s with { Pairs = original == null ? [.. s.Pairs, .. results] : s.Pairs.Select(p => replacements.TryGetValue(p.Id, out var replacement) ? replacement with { Paused = p.Paused } : p).ToArray() };
        });
    }
    private static void Validate(IReadOnlyList<FolderPair> results, IEnumerable<FolderPair> existing)
    {
        foreach (var result in results)
        {
            PathRules.ValidatePair(result.Source, result.Destination);
            PairBoundaries.Validate(result, existing.Concat(results));
        }
    }
    internal static void PrepareDestinations(IReadOnlyList<FolderSelection.Roots> roots, string parent, bool grouped)
    {
        // Validate the existing parent before creating explicitly selected child directories.
        if (grouped) foreach (var root in roots) PathRules.ValidatePair(root.Source, parent);
        foreach (var root in roots)
        {
            if (grouped) { PathRules.RejectLinks(root.Destination); Directory.CreateDirectory(root.Destination); }
            PathRules.ValidatePair(root.Source, root.Destination);
        }
    }
}
