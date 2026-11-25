using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Sinh.Core;

internal static class WindowsPathIdentity
{
    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInfo
    {
        public uint Attributes, CreateLow, CreateHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    private sealed record Identity(string Canonical, uint Volume, uint IndexHigh, uint IndexLow);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder name, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);

    private static Identity Read(string path)
    {
        // P/Invoke does not add .NET's long-path prefix; callers include console/test hosts without our manifest.
        var native = Path.GetFullPath(path);
        if (!native.StartsWith(@"\\?\", StringComparison.Ordinal))
            native = native.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + native[2..] : @"\\?\" + native;
        using var handle = CreateFile(native, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Не удалось проверить физический путь: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
        if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Не удалось проверить идентичность папки: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 1); // Volume GUID for local aliases.
        if (length == 0) length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0); // UNC fallback.
        if (length == 0 || length >= buffer.Capacity) throw new IOException("Не удалось определить окончательный путь: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
        return new(Path.TrimEndingDirectorySeparator(buffer.ToString()), info.VolumeSerial, info.IndexHigh, info.IndexLow);
    }
    private static bool Same(Identity a, Identity b) => a.Volume != 0 && (a.IndexHigh != 0 || a.IndexLow != 0) && a.Volume == b.Volume && a.IndexHigh == b.IndexHigh && a.IndexLow == b.IndexLow;
    internal static void RejectOverlap(string source, string destination)
    {
        var a = Read(source); var b = Read(destination);
        bool Within(string root, string path) => string.Equals(root, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + "\\", StringComparison.OrdinalIgnoreCase);
        if (Within(a.Canonical, b.Canonical) || Within(b.Canonical, a.Canonical) || Same(a, b)) throw new IOException("Корни совпадают или вложены друг в друга через альтернативный путь Windows.");
        // Detect aliases through local SMB shares and drive mappings where file IDs are available.
        foreach (var (path, other) in new[] { (source, b), (destination, a) })
        {
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
            while (!string.IsNullOrEmpty(parent))
            {
                if (Same(Read(parent), other)) throw new IOException("Корни вложены друг в друга через альтернативный путь Windows.");
                var next = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(parent));
                if (next == parent) break; parent = next;
            }
        }
    }
    internal static string Comparable(string path)
    {
        var cursor = Path.GetFullPath(path); var missing = new Stack<string>();
        while (!Directory.Exists(cursor) && !File.Exists(cursor))
        {
            var trimmed = Path.TrimEndingDirectorySeparator(cursor); var parent = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrEmpty(parent) || parent == cursor) return path;
            missing.Push(Path.GetFileName(trimmed)); cursor = parent;
        }
        var canonical = Read(cursor).Canonical;
        while (missing.TryPop(out var part)) canonical = Path.Combine(canonical, part);
        return canonical;
    }
    internal static bool TryRelative(string root, string path, out string relative)
    {
        var canonicalRoot = Comparable(root); var canonicalPath = Comparable(path);
        if (string.Equals(canonicalRoot, canonicalPath, StringComparison.OrdinalIgnoreCase) || canonicalPath.StartsWith(Path.EndsInDirectorySeparator(canonicalRoot) ? canonicalRoot : canonicalRoot + "\\", StringComparison.OrdinalIgnoreCase))
        { relative = Path.GetRelativePath(canonicalRoot, canonicalPath); return true; }
        if (!Directory.Exists(root)) { relative = ""; return false; }
        // Different volume-GUID roots cannot alias one another, even if cloned serial numbers match.
        if (canonicalRoot.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) && canonicalPath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
        { relative = ""; return false; }
        var identity = Read(root); var cursor = Path.GetFullPath(path); var parts = new Stack<string>();
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((Directory.Exists(cursor) || File.Exists(cursor)) && Same(identity, Read(cursor))) { relative = parts.Count == 0 ? "." : Path.Combine(parts.ToArray()); return true; }
            var trimmed = Path.TrimEndingDirectorySeparator(cursor); var parent = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrEmpty(parent) || parent == cursor) break;
            parts.Push(Path.GetFileName(trimmed)); cursor = parent;
        }
        relative = ""; return false;
    }
}
