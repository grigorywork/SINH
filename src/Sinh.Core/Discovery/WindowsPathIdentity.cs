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
        using var handle = CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Не удалось проверить физический путь: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
        if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Не удалось проверить идентичность папки: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 1); // Volume GUID for local aliases.
        if (length == 0) length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0); // UNC fallback.
        if (length == 0 || length >= buffer.Capacity) throw new IOException("Не удалось определить окончательный путь: " + path, new Win32Exception(Marshal.GetLastWin32Error()));
        return new(buffer.ToString().TrimEnd('\\'), info.VolumeSerial, info.IndexHigh, info.IndexLow);
    }
    private static bool Same(Identity a, Identity b) => a.Volume != 0 && (a.IndexHigh != 0 || a.IndexLow != 0) && a.Volume == b.Volume && a.IndexHigh == b.IndexHigh && a.IndexLow == b.IndexLow;
    internal static void RejectOverlap(string source, string destination)
    {
        var a = Read(source); var b = Read(destination);
        bool Within(string root, string path) => string.Equals(root, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
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
}
