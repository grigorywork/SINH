using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sinh.Settings;
using Sinh.Core;

namespace Sinh.App;

internal static class WindowsVolumes
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder volumeName, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(string root, StringBuilder? name, uint nameLength, out uint serial, out uint maxComponent, out uint flags, StringBuilder fileSystem, uint fsLength);
    internal sealed record Limits(long MaxFileBytes, int MaxComponentLength, bool ReadOnly);
    internal static Limits GetLimits(string path)
    {
        var fs = new StringBuilder(64);
        if (!GetVolumeInformation(Path.GetPathRoot(path)!, null, 0, out _, out var maxComponent, out var flags, fs, (uint)fs.Capacity)) return new(long.MaxValue, int.MaxValue, false);
        var fat = fs.ToString().Equals("FAT32", StringComparison.OrdinalIgnoreCase) || fs.ToString().Equals("FAT", StringComparison.OrdinalIgnoreCase);
        return new(fat ? uint.MaxValue : long.MaxValue, maxComponent == 0 ? int.MaxValue : (int)maxComponent, (flags & 0x00080000) != 0);
    }

    private static Guid? Identity(string root)
    {
        var name = new StringBuilder(1024);
        if (!GetVolumeNameForVolumeMountPoint(root, name, name.Capacity)) return null;
        var text = name.ToString(); var start = text.IndexOf('{'); var end = text.IndexOf('}');
        return start >= 0 && end > start && Guid.TryParse(text.AsSpan(start, end - start + 1), out var id) ? id : null;
    }
    internal static (Guid Id, string Relative) Bind(string destination)
    {
        PathRules.Root(destination);
        var root = Path.GetPathRoot(destination)!;
        var drive = new DriveInfo(root);
        if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed) || string.Equals(root, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)) throw new IOException("Запуск при подключении доступен для отдельного локального носителя. Для обычной или сетевой папки используйте расписание.");
        var id = Identity(root) ?? throw new IOException("Windows не вернула постоянный ID носителя.");
        if (id == Identity(Path.GetPathRoot(Environment.SystemDirectory)!)) throw new IOException("Системный носитель не используется как подключаемый USB. Выберите расписание.");
        return (id, PathRules.RelativeTo(root, destination));
    }
    internal static (Guid Id, string Relative)? BindLocal(string path)
    {
        var root = Path.GetPathRoot(path)!; var id = Identity(root);
        return id.HasValue ? (id.Value, PathRules.RelativeTo(root, path)) : null;
    }
    internal static IReadOnlyDictionary<Guid, string> Mounts()
    {
        var result = new Dictionary<Guid, string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try { if (drive.DriveType is DriveType.Removable or DriveType.Fixed && drive.IsReady && Identity(drive.RootDirectory.FullName) is { } id) result[id] = drive.RootDirectory.FullName; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return result;
    }
    internal static string? ResolveSide(FolderPair pair, bool destination, IReadOnlyDictionary<Guid, string>? mounts = null)
    {
        var id = destination ? pair.UsbId : pair.SourceVolumeId;
        if (id == null) return destination ? pair.Destination : pair.Source;
        mounts ??= Mounts(); if (!mounts.TryGetValue(id.Value, out var root)) return null;
        var relative = (destination ? pair.UsbRelativePath : pair.SourceRelativePath) ?? "";
        var resolved = Path.GetFullPath(Path.Combine(root, relative));
        return PathRules.Contains(root, resolved) ? resolved : null;
    }
    internal static FolderPair? Resolve(FolderPair pair, IReadOnlyDictionary<Guid, string>? mounts = null)
    {
        if (pair.UsbId != null || pair.SourceVolumeId != null) mounts ??= Mounts();
        var a = ResolveSide(pair, false, mounts); var b = ResolveSide(pair, true, mounts);
        return a != null && b != null ? pair with { Source = a, Destination = b } : null;
    }
    internal static string? StableSource(FolderPair pair) => pair.SourceVolumeId is { } id ? "volume:" + id.ToString("D") + "/" + pair.SourceRelativePath : null;
    internal static string? StableDestination(FolderPair pair) => pair.UsbId is { } id ? "volume:" + id.ToString("D") + "/" + pair.UsbRelativePath : null;
    internal static bool IsStillMounted(Guid expected, string destination)
    {
        var root = Path.GetPathRoot(destination)!;
        var stable = @"\\?\Volume{" + expected.ToString("D") + @"}\";
        return root.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(root, stable, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root)
            : Identity(root) == expected;
    }
    internal static bool ContextMatches(FolderPair pair) => (pair.SourceVolumeId is not { } a || IsStillMounted(a, pair.Source)) && (pair.UsbId is not { } b || IsStillMounted(b, pair.Destination));
    internal static bool IdentityMatches(string identity, string path) => identity.StartsWith("volume:", StringComparison.Ordinal) && Guid.TryParse(identity[7..].Split('/')[0], out var id) && IsStillMounted(id, path);
    internal static bool CanRecover(JournalState journal, IEnumerable<FolderPair> pairs)
    {
        if (journal.TemporaryPath == null) return true;
        var inDestination = PathRules.Contains(journal.Destination, journal.TemporaryPath);
        var identity = inDestination ? journal.DestinationIdentity : journal.SourceIdentity;
        var path = inDestination ? journal.Destination : journal.Source;
        if (identity?.StartsWith("volume:", StringComparison.Ordinal) == true)
        {
            var text = identity[7..].Split('/')[0]; return Guid.TryParse(text, out var id) && IsStillMounted(id, path);
        }
        var pair = pairs.FirstOrDefault(p => p.Id == journal.PairId);
        if (inDestination && pair?.UsbId is { } expected) return IsStillMounted(expected, path);
        if (!inDestination && pair?.SourceVolumeId is { } sourceId) return IsStillMounted(sourceId, path);
        var root = Path.GetPathRoot(path)!;
        if (root.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        return new DriveInfo(root).DriveType != DriveType.Removable;
    }
}
