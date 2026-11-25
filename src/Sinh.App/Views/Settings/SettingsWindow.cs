using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sinh.Settings;

namespace Sinh.App;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore store;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    public SettingsWindow(SettingsStore store)
    {
        this.store = store; Title = "SINH — настройки"; Width = 720; Height = 760; MinWidth = 520; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var stack = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        stack.Children.Add(new TextBlock { Text = "Настройки", FontSize = 26, Margin = new Thickness(0, 0, 0, 16) });
        void Section(string title, Func<UIElement> build)
        {
            var expander = new Expander { Header = title };
            expander.Expanded += (_, _) => { if (expander.Content == null) expander.Content = build(); if (expander.Content is DependencyObject content) Localizer.Attach(content); };
            stack.Children.Add(expander);
        }
        Section("Внешний вид", AppearancePanel);
        Section("Пары папок", () => Text("Пары настраиваются на главном экране. Изменение одной пары не меняет остальные."));
        Section("Правила синхронизации", () => Text("Новые пары: ручное копирование обновлений A → B, без переноса удалений. Перед зеркалом и сменой корней потребуется план."));
        Section("Расписание", () => Text("Интервал, запуск при подключении носителя и наблюдение задаются отдельно у каждой пары. Первый запуск ручной. Файловые события объединяются; при пропущенных событиях выполняется полная контрольная сверка."));
        Section("Исключения", () => Text("У каждой пары свой список шаблонов. Шаблоны применяются к относительным путям. Ссылки и reparse points не обходятся."));
        Section("История и восстановление", HistoryPanel);
        Section("Производительность", PerformancePanel);
        Section("Уведомления и трей", StartupPanel);
        Section("О программе", () =>
        {
            var about = new StackPanel();
            about.Children.Add(Text($"SINH {App.DisplayVersion} · Windows 10/11 x64 (целевые ОС).\nОтдельная программа; протокол NoteLS не используется.\nОформление: NoteLS 1.10.58, b1f4b1f8…\nСборка не подписана. Windows-запуск и DPI-проверки ещё не выполнены."));
            about.Children.Add(new DeveloperSignature { HorizontalAlignment = HorizontalAlignment.Left });
            return about;
        });
        stack.Children.Add(status);
        var close = new Button { Content = "Закрыть", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) }; stack.Children.Add(close);
        Closing+=async(_,e)=>
        {
            if(!store.HasPendingSaves)return;
            e.Cancel=true;IsEnabled=false;
            try{await store.FlushAsync();IsEnabled=true;Close();}
            catch(Exception ex){IsEnabled=true;status.Text=ex.Message;}
        };
    }
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) };
    private async void Save(Func<AppSettings, AppSettings> update)
    {
        try { await store.UpdateAsync(update); status.Text = "Сохранено"; }
        catch (Exception ex) { status.Text = "Не удалось сохранить: " + ex.Message; }
    }
    private UIElement HistoryPanel()
    {
        var p = new StackPanel(); p.Children.Add(Text($"Заменённые и удалённые версии сохраняются на этом компьютере отдельно от синхронизируемых папок. Лимит: {store.Current.HistoryLimitMiB} МиБ. При заполнении операция остановится. Автоматическая очистка выключена. Просмотр и восстановление: выберите пару на главной и нажмите «История».")); return p;
    }
}

