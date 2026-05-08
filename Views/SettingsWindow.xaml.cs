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

            var row = FindAncestor<Grid>(textBox, element => ReferenceEquals(element.DataContext, item));
            if (row != null)
                EnsureFullyVisible(row);

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

    private static T? FindAncestor<T>(DependencyObject child, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T target && predicate(target))
                return target;

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private static void EnsureFullyVisible(FrameworkElement element)
    {
        var scrollViewer = FindAncestor<ScrollViewer>(element, _ => true);
        if (scrollViewer == null) return;

        var top = element.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
        var bottom = top + element.ActualHeight;
        const double padding = 4;

        if (top < padding)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + top - padding);
        }
        else if (bottom > scrollViewer.ViewportHeight - padding)
        {
            scrollViewer.ScrollToVerticalOffset(
                scrollViewer.VerticalOffset + bottom - scrollViewer.ViewportHeight + padding);
        }
    }
}
