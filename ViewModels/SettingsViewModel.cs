using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CsStructureViewer.Settings;

namespace CsStructureViewer.ViewModels;

public class SettingsViewModel
{
    private readonly AppSettings _settings;
    private readonly SettingsManager _settingsManager;

    public ObservableCollection<ExcludePatternItem> Patterns { get; } = new();
    public ObservableCollection<ExcludePatternItem> InternalPatterns { get; } = new();
    public bool DebugClassTransparencyEnabled { get; set; }

    public SettingsViewModel(AppSettings settings, SettingsManager settingsManager)
    {
        _settings = settings;
        _settingsManager = settingsManager;

        foreach (var p in settings.ExcludePatternRules)
            Patterns.Add(CreateItem(p, Patterns));

        foreach (var p in settings.InternalExcludePatternRules)
            InternalPatterns.Add(CreateItem(p, InternalPatterns));

        DebugClassTransparencyEnabled = settings.DebugClassTransparencyEnabled;

    }

    private ExcludePatternItem CreateItem(ExcludePatternRule rule, ObservableCollection<ExcludePatternItem> list)
    {
        ExcludePatternItem? item = null;
        item = new ExcludePatternItem(
            rule.Pattern,
            rule.MatchFolder,
            rule.MatchNamespace,
            addAfter: _ =>
            {
                var idx = list.IndexOf(item!);
                list.Insert(idx + 1, CreateItem(CreateDefaultRule(), list));
            },
            remove: _ =>
            {
                list.Remove(item!);
            });
        return item;
    }

    private static ExcludePatternRule CreateDefaultRule() =>
        new()
        {
            Pattern = string.Empty,
            MatchFolder = true,
            MatchNamespace = true
        };

    public void Save()
    {
        _settings.ExcludePatterns.Clear();
        _settings.ExcludePatterns.AddRange(Patterns.Select(p => p.Pattern));
        _settings.InternalExcludePatterns.Clear();
        _settings.InternalExcludePatterns.AddRange(InternalPatterns.Select(p => p.Pattern));
        _settings.ExcludePatternRules.Clear();
        _settings.ExcludePatternRules.AddRange(Patterns.Select(p => p.ToRule()));
        _settings.InternalExcludePatternRules.Clear();
        _settings.InternalExcludePatternRules.AddRange(InternalPatterns.Select(p => p.ToRule()));
        _settings.DebugClassTransparencyEnabled = DebugClassTransparencyEnabled;
        _settingsManager.Save(_settings);
    }
}

public class ExcludePatternItem : INotifyPropertyChanged
{
    private string _pattern;
    private bool _matchFolder;
    private bool _matchNamespace;

    public string Pattern
    {
        get => _pattern;
        set => SetField(ref _pattern, value);
    }

    public bool MatchFolder
    {
        get => _matchFolder;
        set => SetField(ref _matchFolder, value);
    }

    public bool MatchNamespace
    {
        get => _matchNamespace;
        set => SetField(ref _matchNamespace, value);
    }

    public RelayCommand AddAfterCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public ExcludePatternItem(
        string pattern,
        bool matchFolder,
        bool matchNamespace,
        Action<ExcludePatternItem> addAfter,
        Action<ExcludePatternItem> remove)
    {
        _pattern = pattern;
        _matchFolder = matchFolder;
        _matchNamespace = matchNamespace;
        AddAfterCommand = new RelayCommand(() => addAfter(this));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public ExcludePatternRule ToRule() =>
        new()
        {
            Pattern = Pattern,
            MatchFolder = MatchFolder,
            MatchNamespace = MatchNamespace
        };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
