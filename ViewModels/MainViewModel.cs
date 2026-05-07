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

    private GraphDisplayMode _displayMode = GraphDisplayMode.Class;
    public GraphDisplayMode DisplayMode
    {
        get => _displayMode;
        private set
        {
            if (SetField(ref _displayMode, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayModeButtonText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNamespaceMode)));
            }
        }
    }

    public string DisplayModeButtonText =>
        DisplayMode == GraphDisplayMode.Class
            ? "表示: クラス"
            : "表示: 名前空間";

    public bool IsNamespaceMode
    {
        get => DisplayMode == GraphDisplayMode.Namespace;
        set => ChangeDisplayMode(value ? GraphDisplayMode.Namespace : GraphDisplayMode.Class);
    }

    public AppSettings Settings { get; }
    public bool DebugClassTransparencyEnabled => Settings.DebugClassTransparencyEnabled;
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

    public void NotifySettingsChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DebugClassTransparencyEnabled)));
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
            var layoutResult = _layoutEngine.Calculate(graph, CanvasWidth, DisplayMode);
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

    private void ChangeDisplayMode(GraphDisplayMode mode)
    {
        if (DisplayMode == mode)
            return;

        DisplayMode = mode;
        if (LastFolderPath is not null && !IsAnalyzing)
            _ = RunAnalysisAsync(LastFolderPath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
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
        return true;
    }
}
