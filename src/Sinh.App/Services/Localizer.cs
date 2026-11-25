using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using Sinh.Settings;

namespace Sinh.App;

internal sealed class Localizer : INotifyPropertyChanged
{
    private static readonly Dictionary<string, string> English = LoadEnglish();
    internal static Localizer Current { get; } = new();
    private string language = "";
    public string Language
    {
        get => language;
        internal set
        {
            var next = value == "en" ? "en" : "ru";
            if (language == next) return;
            language = next;
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(next == "en" ? "en-US" : "ru-RU");
            PropertyChanged?.Invoke(this, new(nameof(Language)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal static string T(string russian) => Current.Language == "en" && English.TryGetValue(russian, out var english) ? english : russian;
    internal static string Error(Exception error)
    {
        var message = error.Message;
        if (English.ContainsKey(message)) return T(message);
        var colon = message.IndexOf(": ", StringComparison.Ordinal);
        return colon >= 0 ? T(message[..colon]) + message[colon..] : message;
    }
    internal static string Format(string key, params object?[] args)
    {
        var english = Current.Language == "en";
        var format = key switch
        {
            "ChangeCount" => english ? "Changes: {0}" : "Изменений: {0}",
            "SyncCompleted" => english ? "Synchronization completed · {0} operations." : "Синхронизация завершена · {0} операций.",
            "StagePath" => "{0} · {1}",
            "PairOperations" => english ? "{0} · operations: {1}" : "{0} · операций: {1}",
            "ProgressBytes" => english ? "{0:N1} / {1:N1} MiB" : "{0:N1} / {1:N1} МиБ",
            "ProgressScan" => english ? "Read: {0:N1} MiB" : "Прочитано: {0:N1} МиБ",
            "ProgressOperations" => english ? "{0:N0} / {1:N0} operations" : "{0:N0} / {1:N0} операций",
            "PlanSummary" => english ? "Operations: {0} · Deletions: {1} · Copy: {2:F1} MiB · Conflicts: {3}\nSource: {4}\nDestination: {5}" : "Операций: {0} · Удалений: {1} · Копирование: {2:F1} МиБ · Конфликтов: {3}\nИсточник: {4}\nНазначение: {5}",
            "ImportSummary" => english ? "Replace settings? Profiles: {0}; unavailable or invalid paths: {1}. All profiles will be paused; automatic runs and deletions disabled. Current settings will be backed up." : "Заменить настройки? Пар: {0}; недоступных или недопустимых путей: {1}. Все пары будут на паузе; автоматика и удаления выключены. Текущие настройки сохранятся отдельной копией.",
            "About" => english ? "SINH {0} · Windows 10/11 x64 (target systems).\nIndependent application; does not use the NoteLS protocol.\nAppearance reference: NoteLS 1.10.58, b1f4b1f8…\nWindows runtime and DPI checks are pending. See Windows file properties for signature information." : "SINH {0} · Windows 10/11 x64 (целевые ОС).\nОтдельная программа; протокол NoteLS не используется.\nОформление: NoteLS 1.10.58, b1f4b1f8…\nWindows-запуск и DPI-проверки ещё не выполнены. Сведения о подписи файла смотрите в свойствах Windows.",
            "HistoryIntro" => english ? "History for “{0}” · shared limit {1} MiB. Stored on this computer. Versions are kept until manually deleted." : "История пары «{0}» · лимит всей истории {1} МиБ. Хранится на этом компьютере. Версии сохраняются до удаления вручную.",
            "HistorySettings" => english ? "Replaced and deleted versions are stored on this computer, separately from synchronized folders. Shared limit: {0} MiB; synchronization stops when full. Versions are kept until manually deleted. Select a profile and click History to browse, restore or delete archived versions." : "Заменённые и удалённые версии сохраняются на этом компьютере отдельно от синхронизируемых папок. Лимит: {0} МиБ. При заполнении операция остановится. Версии сохраняются до удаления вручную. Просмотр, восстановление и удаление архивных версий: выберите пару на главной и нажмите «История».",
            _ => T(key)
        };
        if (key == "StagePath" && args.Length > 0) args[0] = T(args[0]?.ToString() ?? "");
        if (args.Length == 0) return format;
        return string.Format(CultureInfo.GetCultureInfo(english ? "en-US" : "ru-RU"), format, args);
    }
    private static Dictionary<string, string> LoadEnglish()
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream("Sinh.App.Localization.en.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
    internal static Binding Translation(string key) => new(nameof(Language)) { Source = Current, Mode = BindingMode.OneWay, Converter = TranslationConverter.Instance, ConverterParameter = key };
    internal static IValueConverter CaptionConverter { get; } = new CaptionTranslationConverter();
    private sealed class CaptionTranslationConverter : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) => T(value?.ToString() ?? "");
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    internal static void Attach(DependencyObject parent)
    {
        void Label(DependencyObject target, DependencyProperty property)
        {
            if (!BindingOperations.IsDataBound(target, property) && target.GetValue(property) is string value && English.ContainsKey(value)) BindingOperations.SetBinding(target, property, Translation(value));
        }
        // Only whitelisted app captions are bound. User inputs and existing data bindings are untouched.
        if (parent is Window) Label(parent, Window.TitleProperty);
        if (parent is TextBlock) Label(parent, TextBlock.TextProperty);
        if (parent is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Label) Label(parent, ContentControl.ContentProperty);
        if (parent is HeaderedContentControl) Label(parent, HeaderedContentControl.HeaderProperty);
        if (parent is FrameworkElement) Label(parent, FrameworkElement.ToolTipProperty);
        if (parent is DataGrid table) foreach (var column in table.Columns) Label(column, DataGridColumn.HeaderProperty);
        Label(parent, System.Windows.Automation.AutomationProperties.NameProperty);
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) Attach(child);
    }
    private sealed class TranslationConverter : IValueConverter
    {
        internal static readonly TranslationConverter Instance = new();
        public object Convert(object value, Type type, object parameter, CultureInfo culture) => T((string)parameter);
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider provider) => Localizer.Translation(key).ProvideValue(provider);
}

internal sealed class LocalizationService : IDisposable
{
    private readonly SettingsStore store;
    static LocalizationService() => EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => Localizer.Attach((Window)sender)));
    internal LocalizationService(SettingsStore store) { this.store = store; Localizer.Current.Language = store.Current.Appearance.Language; store.Changed += Changed; }
    private void Changed(object? sender, AppSettings settings) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Localizer.Current.Language = store.Current.Appearance.Language);
    public void Dispose() => store.Changed -= Changed;
}
