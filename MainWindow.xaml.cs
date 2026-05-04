using System.Windows;
using CsStructureViewer.Settings;
using CsStructureViewer.ViewModels;
using CsStructureViewer.Views;

namespace CsStructureViewer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel { CanvasWidth = Width };
        DataContext = vm;
        SizeChanged += (_, e) => vm.CanvasWidth = e.NewSize.Width;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        var settingsWindow = new SettingsWindow(new SettingsViewModel(vm.Settings, new SettingsManager()))
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }
}
