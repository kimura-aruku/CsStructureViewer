using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CsStructureViewer.ViewModels;

namespace CsStructureViewer.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PatternAdded += FocusAddedPattern;
        Closed += SettingsWindow_Closed;
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.PatternAdded -= FocusAddedPattern;
    }

    private void FocusAddedPattern(ExcludePatternItem item)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            var textBox = FindDescendant<TextBox>(
                this,
                element => ReferenceEquals(element.DataContext, item));

            if (textBox == null) return;

            textBox.BringIntoView();
            textBox.Focus();
            textBox.SelectAll();
        }, DispatcherPriority.ContextIdle);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.Save();

        DialogResult = true;
        Close();
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target && predicate(target))
                return target;

            var descendant = FindDescendant(child, predicate);
            if (descendant != null)
                return descendant;
        }

        return null;
    }
}
