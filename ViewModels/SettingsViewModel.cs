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

    public SettingsViewModel(AppSettings settings, SettingsManager settingsManager)
    {
        _settings = settings;
        _settingsManager = settingsManager;

        foreach (var p in settings.ExcludePatterns)
            Patterns.Add(CreateItem(p));
    }

    private ExcludePatternItem CreateItem(string pattern)
    {
        var item = new ExcludePatternItem(pattern, AddAfter, Remove);
        item.PropertyChanged += (_, _) => Save();
        return item;
    }

    private void AddAfter(ExcludePatternItem item)
    {
        var idx = Patterns.IndexOf(item);
        Patterns.Insert(idx + 1, CreateItem(string.Empty));
        Save();
    }

    private void Remove(ExcludePatternItem item)
    {
        Patterns.Remove(item);
        Save();
    }

    public void Save()
    {
        _settings.ExcludePatterns.Clear();
        _settings.ExcludePatterns.AddRange(Patterns.Select(p => p.Pattern));
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
