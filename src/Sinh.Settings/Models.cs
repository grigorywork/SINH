namespace Sinh.Settings;

public enum SyncMode { Updates, Mirror, TwoWay }
public enum Theme { System, Light, Dark }
public enum UsbSide { Destination, Source }
public enum CloseBehavior { Exit, Tray }

public sealed record Appearance
{
    public string Language { get; init; } = "ru";
    public Theme Theme { get; init; }
    public bool Glass { get; init; }
    public double Scale { get; init; } = 1;
    public string Accent { get; init; } = "System";
    public string Font { get; init; } = "System";
    public double Transparency { get; init; } = 50;
    public double Frost { get; init; }
    public string Tint { get; init; } = "Theme";
    public string Background { get; init; } = "Aurora";
}

public sealed record FolderPair
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Новая пара";
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public SyncMode Mode { get; init; }
    public bool AllowDeletes { get; init; }
    public bool History { get; init; } = true;
    public bool Paused { get; init; }
    public UsbSide UsbSide { get; init; }
    public bool AutoOnConnect { get; init; }
    public bool WatchChanges { get; init; }
    public int IntervalMinutes { get; init; }
    public IReadOnlyList<string> Exclusions { get; init; } = [];
    public Guid? UsbId { get; init; }
    public string? UsbRelativePath { get; init; }
    public Guid? SourceVolumeId { get; init; }
    public string? SourceRelativePath { get; init; }
    public DateTimeOffset? LastSuccess { get; init; }
    public string? ApprovedRoots { get; init; }
}

public sealed record AppSettings
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; init; } = CurrentSchema;
    public Appearance Appearance { get; init; } = new();
    public IReadOnlyList<FolderPair> Pairs { get; init; } = [];
    public bool AutoStart { get; init; }
    public bool StartMinimized { get; init; }
    public CloseBehavior CloseBehavior { get; init; }
    public bool NotifySuccess { get; init; } = true;
    public bool NotifyError { get; init; } = true;
    public bool NotifyConflict { get; init; } = true;
    public int MaxParallelFiles { get; init; } = 1;
    public int SpeedLimitMiB { get; init; }
    public int ScanMinutes { get; init; } = 10;
    public int LogDays { get; init; } = 30;
    public int ConfigurationBackupDays { get; init; }
    public long HistoryLimitMiB { get; init; } = 4096;
}
