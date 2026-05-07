using System.ComponentModel;
using System.Runtime.CompilerServices;
using CsStructureViewer.Analysis;
using CsStructureViewer.Diagnostics;
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

    public double CanvasWidth { get; set; } = 1200.0;

    private string? _lastFolderPath;
    public string? LastFolderPath
    {
        get => _lastFolderPath;
        private set
        {
            SetField(ref _lastFolderPath, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowRefresh)));
        }
    }

    private LayoutResult? _layoutResult;
    public LayoutResult? LayoutResult
    {
        get => _layoutResult;
        private set => SetField(ref _layoutResult, value);
    }

    private string? _latestDiagnosticsPath;
    public string? LatestDiagnosticsPath
    {
        get => _latestDiagnosticsPath;
        private set => SetField(ref _latestDiagnosticsPath, value);
    }

    private bool _isAnalyzing;
    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set => SetField(ref _isAnalyzing, value);
    }

    public bool ShowWelcome => LayoutResult is null && !IsAnalyzing;
    public bool ShowGraph => LayoutResult is not null;
    public bool ShowRefresh => LastFolderPath is not null;

    public AppSettings Settings { get; }
    public AsyncRelayCommand OpenProjectCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand CancelCommand { get; }

    public MainViewModel()
    {
        Settings = _settingsManager.Load();
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => LastFolderPath is not null && !IsAnalyzing);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsAnalyzing);
    }

    private async Task OpenProjectAsync()
    {
        var dialog = new OpenFolderDialog { Title = "解析対象フォルダを選択" };
        if (dialog.ShowDialog() != true) return;

        LastFolderPath = dialog.FolderName;
        await RunAnalysisAsync(dialog.FolderName);
    }

    private async Task RefreshAsync()
    {
        if (LastFolderPath is null) return;
        await RunAnalysisAsync(LastFolderPath);
    }

    private async Task RunAnalysisAsync(string folderPath)
    {
        IsAnalyzing = true;
        LayoutResult = null;
        _cts = new CancellationTokenSource();
        CancelCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();

        try
        {
            var graph = await _analyzer.AnalyzeAsync(
                folderPath, Settings, cancellationToken: _cts.Token);
            var layoutResult = _layoutEngine.Calculate(graph, CanvasWidth);
            layoutResult.ProjectPath = folderPath;
            LatestDiagnosticsPath = LayoutDiagnosticsWriter.WriteLatest(layoutResult, folderPath);
            LayoutResult = layoutResult;
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsAnalyzing = false;
            _cts.Dispose();
            _cts = null;
            CancelCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
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
        if (name == nameof(IsAnalyzing))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowWelcome)));
        }
    }
}
