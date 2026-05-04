using System.ComponentModel;
using System.Runtime.CompilerServices;
using CsStructureViewer.Analysis;
using CsStructureViewer.Layout;
using CsStructureViewer.Settings;
using Microsoft.Win32;

namespace CsStructureViewer.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ProjectAnalyzer _analyzer = new();
    private readonly LayoutEngine _layoutEngine = new();
    private readonly SettingsManager _settingsManager = new();
    private CancellationTokenSource? _cts;

    private LayoutResult? _layoutResult;
    public LayoutResult? LayoutResult
    {
        get => _layoutResult;
        private set => SetField(ref _layoutResult, value);
    }

    private bool _isAnalyzing;
    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set => SetField(ref _isAnalyzing, value);
    }

    public bool ShowWelcome => LayoutResult is null;
    public bool ShowGraph => LayoutResult is not null;

    public AppSettings Settings { get; }
    public AsyncRelayCommand OpenProjectCommand { get; }
    public RelayCommand CancelCommand { get; }

    public MainViewModel()
    {
        Settings = _settingsManager.Load();
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsAnalyzing);
    }

    private async Task OpenProjectAsync()
    {
        var dialog = new OpenFolderDialog { Title = "解析対象フォルダを選択" };
        if (dialog.ShowDialog() != true) return;

        IsAnalyzing = true;
        LayoutResult = null;
        _cts = new CancellationTokenSource();
        CancelCommand.RaiseCanExecuteChanged();

        try
        {
            var graph = await _analyzer.AnalyzeAsync(
                dialog.FolderName, Settings, cancellationToken: _cts.Token);
            LayoutResult = _layoutEngine.Calculate(graph);
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsAnalyzing = false;
            _cts.Dispose();
            _cts = null;
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(LayoutResult))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowWelcome)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowGraph)));
        }
    }
}
