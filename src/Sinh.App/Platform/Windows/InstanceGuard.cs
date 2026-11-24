using System.Security.Principal;

namespace Sinh.App;

internal sealed class InstanceGuard : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle show;
    private RegisteredWaitHandle? registration;
    internal bool IsPrimary { get; }
    internal InstanceGuard()
    {
        var suffix = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        mutex = new Mutex(true, "Local\\SINH.Mutex." + suffix, out var primary); IsPrimary = primary;
        show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\SINH.Show." + suffix);
        if (!primary) show.Set();
    }
    internal void Listen(Action activate) => registration = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => activate(), null, Timeout.InfiniteTimeSpan, false);
    public void Dispose() { registration?.Unregister(null); show.Dispose(); if (IsPrimary) mutex.ReleaseMutex(); mutex.Dispose(); }
}
