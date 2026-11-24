using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Sinh.App;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static async Task<string?> SetEnabledAsync(bool enabled)
    {
        if (!enabled) { using var key = Registry.CurrentUser.OpenSubKey(RunKey, true); key?.DeleteValue("SINH", false); return null; }
        var source = Environment.ProcessPath ?? throw new IOException("Не найден запущенный EXE.");
        var folder = Path.Combine(App.DataDirectory, "Agent"); var destination = Path.Combine(folder, "SINH.exe");
        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "SINH.deps.json"))) throw new IOException("Помощник устанавливается из portable-сборки, содержащей один SINH.exe.");
            Directory.CreateDirectory(folder); var temporary = destination + ".tmp";
            await Task.Run(() => { File.Copy(source, temporary, true); File.Move(temporary, destination, true); });
        }
        using var run = Registry.CurrentUser.CreateSubKey(RunKey, true);
        run.SetValue("SINH", "\"" + destination + "\" --startup", RegistryValueKind.String);
        return string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ? null : destination;
    }
    internal static void RelaunchLocal(string executable)
    {
        Process.Start(new ProcessStartInfo(executable, "--handoff=" + Environment.ProcessId) { UseShellExecute = true });
        ((App)System.Windows.Application.Current).RequestExit();
    }
}
