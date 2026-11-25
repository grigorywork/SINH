using System.IO;
using Sinh.Settings;

namespace Sinh.App;

/// <summary>Immutable editor input. Validation and volume discovery run on a worker, without UI controls.</summary>
internal sealed record FolderPairDraft(FolderPair Template, bool IsNew, string Name,
    IReadOnlyList<FolderSelection.Roots> Roots, string Parent, bool Grouped, UsbSide UsbSide,
    SyncMode Mode, bool AllowDeletes, bool History, bool AutoOnConnect, bool WatchChanges,
    int IntervalMinutes, string[] Exclusions)
{
    internal FolderPair[] Build(IReadOnlyList<FolderPair> existing)
    {
        // Check every profile and volume before creating explicitly selected destination children.
        if (AutoOnConnect) foreach (var root in Roots) WindowsVolumes.Bind(UsbSide == UsbSide.Source ? root.Source : Parent);
        if (Grouped) foreach (var root in Roots) Sinh.Core.PathRules.ValidatePair(root.Source, Parent);
        var results = Roots.Select(root =>
        {
            var a = root.Source; var b = root.Destination;
            var sourceBinding = WindowsVolumes.BindLocal(a); var targetBinding = WindowsVolumes.BindLocal(b);
            var changed = a != Template.Source || b != Template.Destination || Mode != Template.Mode || AllowDeletes != Template.AllowDeletes || UsbSide != Template.UsbSide;
            return Template with
            {
                Id = IsNew ? Guid.NewGuid() : Template.Id, Name = Grouped ? Path.GetFileName(Path.TrimEndingDirectorySeparator(a)) : Name,
                Source = a, Destination = b, UsbSide = UsbSide, Mode = Mode, AllowDeletes = AllowDeletes, History = History,
                Exclusions = Exclusions, ApprovedRoots = changed ? null : Template.ApprovedRoots,
                AutoOnConnect = AutoOnConnect, WatchChanges = WatchChanges, IntervalMinutes = IntervalMinutes,
                UsbId = targetBinding?.Id, UsbRelativePath = targetBinding?.Relative,
                SourceVolumeId = sourceBinding?.Id, SourceRelativePath = sourceBinding?.Relative,
                LastSuccess = changed ? null : Template.LastSuccess
            };
        }).ToArray();
        foreach (var result in results) PairBoundaries.Validate(result, existing.Concat(results));
        FolderPairEditor.PrepareDestinations(Roots, Parent, Grouped);
        return results;
    }
}
