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
        this.model=model; model.RunPair=RunAsync; model.CancelRun=()=>cancellation?.Cancel(); model.RefreshCommands();
    }
    private async Task RunAsync(PairViewModel pair, bool apply)
    {
        if(model.Busy)return;
        using var cts=new CancellationTokenSource();cancellation=cts;completion=new(TaskCreationOptions.RunContinuationsAsynchronously);model.Busy=true;
        try
        {
            pair.Status="Сравнение…";
            var request=new SyncRequest(pair.Pair.Id,pair.Pair.Source,pair.Pair.Destination,(SyncMode)(int)pair.Pair.Mode,pair.Pair.AllowDeletes,pair.Pair.Exclusions);
            var progress=new Progress<SyncProgress>(p=>model.Status=p.Stage+" · "+p.Path);
            var baseline=await Task.Run(()=>stateStore.LoadAsync(request.PairId,cts.Token));
            var plan=await Task.Run(()=>new ComparisonService().CompareAsync(request,baseline,progress,cts.Token));
            pair.Status=$"Изменений: {plan.Operations.Count}";model.Status="Сравнение завершено. Пользовательские файлы не изменялись.";
            if(!apply) { new PlanWindow(plan,false){Owner=System.Windows.Application.Current.MainWindow}.ShowDialog();return; }
            var reversed=false;
            if(plan.DestinationChanged)
            {
                var direction=new DirectionWindow(){Owner=System.Windows.Application.Current.MainWindow};
                if(direction.ShowDialog()!=true)return;
                if(direction.UseDestination)
                {
                    reversed=true;
                    var reverse=request with {Source=request.Destination,Destination=request.Source};
                    plan=await Task.Run(()=>new ComparisonService().CompareAsync(reverse,progress:progress,cancellationToken:cts.Token));
                }
            }
            if(plan.Operations.Count>0 && new PlanWindow(plan,true){Owner=System.Windows.Application.Current.MainWindow}.ShowDialog()!=true)return;
            pair.Status="Копирование…";
            var settings=model.Store.Current;
            var state=await Task.Run(()=>new SyncExecutor(stateStore).ApplyAsync(plan,new(pair.Pair.History,settings.HistoryLimitMiB*1048576,settings.SpeedLimitMiB),progress,cts.Token));
            if(reversed) await stateStore.SaveAsync(request.PairId,new(SyncPlanner.Fingerprint(request),state.Destination,state.Source,state.ConfirmedAt),cts.Token);
            await model.Store.UpdateAsync(s=>s with {Pairs=s.Pairs.Select(p=>p.Id==request.PairId?p with {LastSuccess=state.ConfirmedAt,ApprovedRoots=SyncPlanner.Fingerprint(request)}:p).ToArray()});
            model.Status=$"Синхронизация завершена · {plan.Operations.Count} операций.";
        }
        catch(OperationCanceledException){pair.Status="Остановлено";model.Status="Операция отменена.";}
        catch(Exception ex){pair.Status="Ошибка";model.Status=ex.Message;MessageBox.Show(System.Windows.Application.Current.MainWindow,ex.Message,"SINH — операция остановлена");}
        finally{cancellation=null;model.Busy=false;completion.TrySetResult();}
    }
    public void Dispose(){cancellation?.Cancel();model.RunPair=null;model.CancelRun=null;}
}
