using System.Windows;
namespace Sinh.App;
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); DataContext = model;
        Closed += (_, _) => model.Dispose();
    }
}
