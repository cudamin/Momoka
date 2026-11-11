using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Plex;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlexFilterChoice(string value, string title)
{
    public string Value { get; } = value;
    public string Title { get; } = title;
}

public sealed partial class PlexFilterRow(PlexLibraryFilter filter, Action<PlexFilterRow> changed) : ObservableObject
{
    private bool _restoring;
    public string Field { get; } = filter.Field;
    public string Label { get; } = filter.Title;
    public ObservableCollection<PlexFilterChoice> Options { get; } = [];
    [ObservableProperty]
    public partial PlexFilterChoice? SelectedOption { get; set; }
    internal void Restore(string value)
    {
        _restoring = true;
        SelectedOption = Options.FirstOrDefault(option => option.Value == value);
        if (SelectedOption is null && value.Length > 0)
        {
            var saved = new PlexFilterChoice(value, "已保存：" + value);
            Options.Add(saved);
            SelectedOption = saved;
        }
        _restoring = false;
    }
    partial void OnSelectedOptionChanged(PlexFilterChoice? value)
    {
        if (!_restoring && value is not null) changed(this);
    }
}
