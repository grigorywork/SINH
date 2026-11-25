namespace Sinh.Settings;

/// <summary>Removing a profile only changes configuration; its files and history are never deleted.</summary>
public static class ProfileList
{
    public static AppSettings Remove(AppSettings settings, IEnumerable<Guid> ids)
    {
        var removed = ids.ToHashSet();
        return settings with { Pairs = settings.Pairs.Where(p => !removed.Contains(p.Id)).ToArray() };
    }
}
