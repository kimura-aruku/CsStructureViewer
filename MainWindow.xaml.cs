using System.Windows;
using System.Windows.Threading;
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
        GraphCanvas.InitialViewportRequested += (_, _) => ScrollToInitialGraphViewport();
    }

    private void ScrollToInitialGraphViewport()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var offset = GraphCanvas.GetInitialScrollOffset(
                new Size(GraphScrollViewer.ViewportWidth, GraphScrollViewer.ViewportHeight));
            GraphScrollViewer.ScrollToHorizontalOffset(offset.X);
            GraphScrollViewer.ScrollToVerticalOffset(offset.Y);
        }));
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        var settingsWindow = new SettingsWindow(new SettingsViewModel(vm.Settings, new SettingsManager()))
        {
            Owner = this
        };
        if (settingsWindow.ShowDialog() == true)
            vm.NotifySettingsChanged();
    }
}
