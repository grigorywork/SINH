namespace Sinh.Settings;

/// <summary>Presentation-only keys. Never probes disks or changes synchronization roots.</summary>
public static class FolderGrouping
{
    public sealed record Descriptor(string Key, string Path);
    public static string Parent(string path)
    {
        var text = path.TrimEnd('\\', '/');
        var separator = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
        if (separator < 0) return "";
        if (separator == 0 || separator == 2 && text[1] == ':') separator++;
        return text[..separator];
    }
    public static Descriptor Describe(FolderPair pair)
    {
        var parent = Parent(pair.Destination);
        var key = pair.UsbId is { } id && pair.UsbRelativePath is { } relative
            ? "volume:" + id.ToString("D") + "/" + Parent(relative).Replace('\\', '/').ToUpperInvariant()
            : "path:" + parent.Replace('\\', '/').ToUpperInvariant();
        return new(parent.Length == 0 ? "pair:" + pair.Id : key, parent);
    }
}
