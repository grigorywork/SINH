using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sinh.Settings;

namespace Sinh.App;

/// <summary>One source for editing; an accumulating checked selection for new profiles.</summary>
internal sealed class FolderSources : StackPanel
{
    private readonly bool multiple;
    private readonly TextBox source;
    private readonly StackPanel selection = new();
    private readonly List<CheckBox> choices = [];
    internal bool Grouped { get; private set; }
    internal IEnumerable<string> SelectedPaths => Grouped ? choices.Where(c => c.IsChecked == true).Select(c => (string)c.Tag) : [source.Text];
    internal FolderSources(string initial, bool multiple)
    {
        this.multiple = multiple;
        Children.Add(new TextBlock { Text = "Источник", Margin = new Thickness(0, 10, 0, 5) });
        source = new TextBox { Text = initial };
        System.Windows.Automation.AutomationProperties.SetName(source, "Источник");
        Children.Add(source); Children.Add(selection);
        var pick = new Button { Content = multiple ? "Добавить папки…" : "Выбрать папку…", HorizontalAlignment = HorizontalAlignment.Left };
        pick.Click += (_, _) =>
        {
            try
            {
                var dialog = new OpenFolderDialog { Title = Localizer.T("Выберите папки"), Multiselect = multiple };
                if (dialog.ShowDialog(Window.GetWindow(this)) == true) AddPaths(dialog.FolderNames);
            }
            catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), Localizer.Error(ex), "SINH"); }
        };
        Children.Add(pick);
    }
    internal void AddPaths(IEnumerable<string> added)
    {
        if (!multiple) { source.Text = added.First(); return; }
        var previous = choices.ToDictionary(c => (string)c.Tag, c => c.IsChecked == true, StringComparer.Ordinal);
        var existing = choices.Count > 0 ? choices.Select(c => (string)c.Tag) : string.IsNullOrWhiteSpace(source.Text) ? [] : new[] { source.Text };
        var paths = FolderSelection.Accumulate(existing, added);
        choices.Clear(); selection.Children.Clear(); Grouped = paths.Count > 1;
        source.Visibility = Grouped ? Visibility.Collapsed : Visibility.Visible;
        if (!Grouped) { source.Text = paths.FirstOrDefault() ?? ""; return; }
        source.Text = "";
        foreach (var path in paths)
        {
            var check = new CheckBox { Content = path, Tag = path, IsChecked = !previous.TryGetValue(path, out var enabled) || enabled, MinHeight = 40 };
            choices.Add(check); selection.Children.Add(check);
        }
    }
}
