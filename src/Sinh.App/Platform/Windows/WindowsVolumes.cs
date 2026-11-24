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
        return (id, Path.GetRelativePath(root, destination));
    }
    internal static FolderPair? Resolve(FolderPair pair)
    {
        if (pair.UsbId == null) return pair;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed) || !drive.IsReady || Identity(drive.RootDirectory.FullName) != pair.UsbId) continue;
                var destination = Path.GetFullPath(Path.Combine(drive.RootDirectory.FullName, pair.UsbRelativePath ?? ""));
                if (!PathRules.Contains(drive.RootDirectory.FullName, destination)) throw new IOException("Некорректная привязка папки USB.");
                return pair with { Destination = destination };
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }
    internal static string? StableDestination(FolderPair pair) => pair.UsbId is { } id ? "volume:" + id.ToString("D") + "/" + pair.UsbRelativePath : null;
    internal static bool IsStillMounted(Guid expected, string destination) => Identity(Path.GetPathRoot(destination)!) == expected;
}
