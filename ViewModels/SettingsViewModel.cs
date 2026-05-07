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
    public RelayCommand AddInternalPatternCommand { get; }
    public bool DebugClassTransparencyEnabled { get; set; }

    public SettingsViewModel(AppSettings settings, SettingsManager settingsManager)
    {
        _settings = settings;
        _settingsManager = settingsManager;

        foreach (var p in settings.ExcludePatterns)
            Patterns.Add(CreateItem(p, Patterns));

        foreach (var p in settings.InternalExcludePatterns)
            InternalPatterns.Add(CreateItem(p, InternalPatterns));

        DebugClassTransparencyEnabled = settings.DebugClassTransparencyEnabled;

        AddInternalPatternCommand = new RelayCommand(() =>
        {
            InternalPatterns.Add(CreateItem(string.Empty, InternalPatterns));
        });
    }

    private ExcludePatternItem CreateItem(string pattern, ObservableCollection<ExcludePatternItem> list)
    {
        ExcludePatternItem? item = null;
        item = new ExcludePatternItem(
            pattern,
            addAfter: _ =>
            {
                var idx = list.IndexOf(item!);
                list.Insert(idx + 1, CreateItem(string.Empty, list));
            },
            remove: _ =>
            {
                list.Remove(item!);
            });
        return item;
    }

    public void Save()
    {
        _settings.ExcludePatterns.Clear();
        _settings.ExcludePatterns.AddRange(Patterns.Select(p => p.Pattern));
        _settings.InternalExcludePatterns.Clear();
        _settings.InternalExcludePatterns.AddRange(InternalPatterns.Select(p => p.Pattern));
        _settings.DebugClassTransparencyEnabled = DebugClassTransparencyEnabled;
        _settingsManager.Save(_settings);
    }
}

public class ExcludePatternItem : INotifyPropertyChanged
{
    private string _pattern;

    public string Pattern
    {
        get => _pattern;
        set => SetField(ref _pattern, value);
    }

    public RelayCommand AddAfterCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public ExcludePatternItem(
        string pattern,
        Action<ExcludePatternItem> addAfter,
        Action<ExcludePatternItem> remove)
    {
        _pattern = pattern;
        AddAfterCommand = new RelayCommand(() => addAfter(this));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
