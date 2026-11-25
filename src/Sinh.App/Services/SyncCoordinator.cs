using System.Windows;
using Sinh.Core;

namespace Sinh.App;

internal sealed class SyncCoordinator : IDisposable
{
    private readonly MainViewModel model;
    private readonly StateStore stateStore = new(App.DataDirectory);
    private TaskCompletionSource completion = Completed();
    public Task Completion => completion.Task;
    private static TaskCompletionSource Completed() { var source = new TaskCompletionSource(); source.SetResult(); return source; }
    private CancellationTokenSource? cancellation;
    public SyncCoordinator(MainViewModel model)
    {
        this.model = model; model.RunPair = (pair, apply) => RunAsync(pair, apply); model.CancelRun = () => cancellation?.Cancel(); model.RefreshCommands();
        model.Store.Changed += SettingsChanged;
    }
    private void SettingsChanged(object? sender, Sinh.Settings.AppSettings settings)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (model.ActivePairId is { } id && model.Store.Current.Pairs.FirstOrDefault(p => p.Id == id)?.Paused == true) cancellation?.Cancel();
        });
    }
    internal async Task<bool> RunAutomaticAsync(Sinh.Settings.FolderPair resolved)
    {
        if (model.Busy || model.DialogOpen) return false;
        var pair = model.Pairs.FirstOrDefault(p => p.Pair.Id == resolved.Id);
        if (pair == null || pair.Pair.Paused) return false;
        await RunAsync(pair, true, true, resolved); return true;
    }
    private async Task RunAsync(PairViewModel pair, bool apply, bool automatic = false, Sinh.Settings.FolderPair? resolved = null)
    {
        if (model.Busy) return;
        using var cts = new CancellationTokenSource(); cancellation = cts; completion = new(TaskCreationOptions.RunContinuationsAsynchronously); model.ActivePairId = pair.Pair.Id; model.Busy = true;
        try
        {
            pair.Status = "Сравнение…";
            resolved ??= await Task.Run(() => WindowsVolumes.Resolve(pair.Pair));
            if (resolved == null) throw new System.IO.IOException("Настроенный носитель отключён.");
            await Task.Run(() => PairBoundaries.Validate(resolved, model.Store.Current.Pairs));
            pair.SetResolvedPaths(resolved.Source,resolved.Destination);
            var request = await Task.Run(()=>SyncScope.ExcludeInternalDirectory(new SyncRequest(resolved.Id, resolved.Source, resolved.Destination, (SyncMode)(int)resolved.Mode, resolved.AllowDeletes, resolved.Exclusions.ToArray(),SourceIdentity:WindowsVolumes.StableSource(resolved), DestinationIdentity: WindowsVolumes.StableDestination(resolved)),stateStore.DirectoryPath));
            if (automatic && pair.Pair.ApprovedRoots != SyncPlanner.Fingerprint(request)) { pair.Status = "Нужен ручной запуск после изменения настроек"; return; }
            await Task.Run(()=>new SyncExecutor(stateStore).RecoverAsync(contextAvailable:j=>WindowsVolumes.CanRecover(j,model.Store.Current.Pairs)));
            model.ReportProgress(new("Сравнение…", ""));
            var progress = new Progress<SyncProgress>(model.ReportProgress);
            var baseline = await Task.Run(() => stateStore.LoadAsync(request.PairId, cts.Token));
            var plan = await Task.Run(() => new ComparisonService().CompareAsync(request, baseline, progress, cts.Token));
            pair.SetStatus("ChangeCount", plan.Operations.Count); model.Status = "Сравнение завершено. Пользовательские файлы не изменялись.";
            if (!apply) { new PlanWindow(plan, false) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog(); return; }
            var reversed = false;
            if (plan.DestinationChanged && request.Mode != SyncMode.TwoWay)
            {
                ((App)System.Windows.Application.Current).ShowMain();
                ((App)System.Windows.Application.Current).Notify("Изменения в копии", pair.Name, "conflict");
                var direction = new DirectionWindow() { Owner = System.Windows.Application.Current.MainWindow };
                if (direction.ShowDialog() != true) return;
                if (direction.UseDestination)
                {
                    reversed = true;
                    var reverse = request with { Source = request.Destination, Destination = request.Source, SourceIdentity = request.DestinationIdentity, DestinationIdentity = request.SourceIdentity };
                    plan = await Task.Run(() => new ComparisonService().CompareAsync(reverse, progress: progress, cancellationToken: cts.Token));
                }
            }
            var review = PlanReviewPolicy.RequiresReview(plan, automatic, pair.Pair.ApprovedRoots);
            if (plan.Operations.Count > 0 && review)
            {
                ((App)System.Windows.Application.Current).ShowMain();
                if (new PlanWindow(plan, true) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog() != true) return;
            }
            pair.Status = "Копирование…";
            var settings = model.Store.Current;
            Func<bool> mounted = ()=>WindowsVolumes.ContextMatches(resolved);
            var limits=await Task.Run(()=>plan.Operations.Select(o=>o.Root).Distinct(StringComparer.OrdinalIgnoreCase).Select(WindowsVolumes.GetLimits).ToArray());
            var maxBytes=limits.Length>0?limits.Min(l=>l.MaxFileBytes):long.MaxValue;var maxName=limits.Length>0?limits.Min(l=>l.MaxComponentLength):int.MaxValue;
            var state = await Task.Run(() => new SyncExecutor(stateStore).ApplyAsync(plan, new(pair.Pair.History, settings.HistoryLimitMiB * 1048576, settings.SpeedLimitMiB, mounted,maxBytes,maxName,limits.Any(l=>l.ReadOnly)), progress, cts.Token));
            if (reversed) await stateStore.SaveAsync(request.PairId, new(SyncPlanner.Fingerprint(request), state.Destination, state.Source, state.ConfirmedAt), cts.Token);
            await model.Store.UpdateAsync(s => s with { Pairs = s.Pairs.Select(p => p.Id == request.PairId ? p with { LastSuccess = state.ConfirmedAt, ApprovedRoots = SyncPlanner.Fingerprint(request) } : p).ToArray() });
            model.SetStatus("SyncCompleted", plan.Operations.Count);
            pair.Status = "Готово";
            if (plan.Operations.Count > 0) ((App)System.Windows.Application.Current).Notify("Готово", Localizer.Format("PairOperations", pair.Name, plan.Operations.Count), "success");
        }
        catch (OperationCanceledException) { pair.Status = pair.Pair.Paused ? "На паузе" : "Остановлено"; model.Status = "Операция отменена."; }
        catch (Exception ex) { pair.Status = "Ошибка"; model.SetError(ex); ((App)System.Windows.Application.Current).Notify("Ошибка", Localizer.Error(ex), "error"); if (!automatic) MessageBox.Show(System.Windows.Application.Current.MainWindow, Localizer.Error(ex), Localizer.T("SINH — операция остановлена")); }
        finally { cancellation = null; model.ActivePairId = null; model.Busy = false; completion.TrySetResult(); }
    }
    public void Dispose() { model.Store.Changed -= SettingsChanged; cancellation?.Cancel(); model.RunPair = null; model.CancelRun = null; }
}
