using System.Windows;
using Sinh.Core;

namespace Sinh.App;

internal sealed class SyncCoordinator : IDisposable
{
    private readonly MainViewModel model;
    private CancellationTokenSource? cancellation;
    public SyncCoordinator(MainViewModel model)
    {
        this.model=model; model.RunPair=RunAsync; model.CancelRun=()=>cancellation?.Cancel(); model.RefreshCommands();
    }
    private async Task RunAsync(PairViewModel pair, bool apply)
    {
        if(model.Busy)return;
        using var cts=new CancellationTokenSource();cancellation=cts;model.Busy=true;
        try
        {
            pair.Status="Сравнение…";
            var request=new SyncRequest(pair.Pair.Id,pair.Pair.Source,pair.Pair.Destination,(SyncMode)(int)pair.Pair.Mode,pair.Pair.AllowDeletes,pair.Pair.Exclusions);
            var progress=new Progress<SyncProgress>(p=>model.Status=p.Stage+" · "+p.Path);
            var plan=await Task.Run(()=>new ComparisonService().CompareAsync(request,progress:progress,cancellationToken:cts.Token));
            pair.Status=$"Изменений: {plan.Operations.Count}";model.Status="Сравнение завершено. Пользовательские файлы не изменялись.";
            new PlanWindow(plan,false){Owner=System.Windows.Application.Current.MainWindow}.ShowDialog();
        }
        catch(OperationCanceledException){pair.Status="Остановлено";model.Status="Операция отменена.";}
        catch(Exception ex){pair.Status="Ошибка";model.Status=ex.Message;MessageBox.Show(System.Windows.Application.Current.MainWindow,ex.Message,"SINH — операция остановлена");}
        finally{cancellation=null;model.Busy=false;}
    }
    public void Dispose(){cancellation?.Cancel();model.RunPair=null;model.CancelRun=null;}
}
