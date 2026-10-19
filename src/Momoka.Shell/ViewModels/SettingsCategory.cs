using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 设置左栏的一组分类。Momoka 组与单服务器时的产品组不折叠；多服务器时的服务器组可折叠，
/// 默认收起，靠组标题展开。
/// </summary>
public sealed partial class SettingsCategoryGroup : ObservableObject, IEnumerable<SettingsCategoryEntry>
{
    private readonly List<SettingsCategoryEntry> _entries = [];

    internal SettingsCategoryGroup(string name, string? sourceKey = null, bool collapsible = false, bool expanded = true)
    {
        Name = name;
        SourceKey = sourceKey;
        Collapsible = collapsible;
        IsExpanded = expanded;
    }

    public string Name { get; }

    /// <summary>这一组属于哪一条在线身份；Momoka 自己的组与单服务器产品组是 null。</summary>
    public string? SourceKey { get; }

    public bool Collapsible { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public Visibility ChevronVisibility => Collapsible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>不可折叠的组画一条普通标题（Momoka、单服务器的产品分组）。</summary>
    public Visibility CollapsedHeaderVisibility => Collapsible ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>可折叠的服务器组画带箭头的标题按钮。</summary>
    public Visibility CollapsibleHeaderVisibility => Collapsible ? Visibility.Visible : Visibility.Collapsed;

    public string ChevronGlyph => IsExpanded ? "\uE70E" : "\uE70D";

    public IReadOnlyList<SettingsCategoryEntry> Entries => _entries;

    internal SettingsCategoryEntry Add(string label, string? sourceKey = null, string? route = null, string? cardCategory = null)
    {
        var entry = new SettingsCategoryEntry(this, label, sourceKey, route, cardCategory);
        _entries.Add(entry);
        return entry;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ChevronGlyph));
        foreach (var entry in _entries) entry.RefreshExpansion();
    }

    public IEnumerator<SettingsCategoryEntry> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// 左栏的一项。单服务器时 <see cref="Key"/> 就是分类名（旧行为与命令行别名都还认）；多服务器时
/// 加上来源前缀，避免两台服务器的「控制台」「媒体库」撞成一个键。
/// </summary>
public sealed partial class SettingsCategoryEntry : ObservableObject
{
    internal SettingsCategoryEntry(SettingsCategoryGroup group, string label,
        string? sourceKey, string? route, string? cardCategory)
    {
        Group = group;
        Label = label;
        SourceKey = sourceKey;
        Route = route;
        CardCategory = cardCategory ?? label;
        Key = sourceKey is null ? label : sourceKey + "\u001f" + label;
    }

    public string Key { get; }
    public string Label { get; }
    public string PickerLabel => Group.SourceKey is null ? Label : Group.Name + " · " + Label;
    public string CardCategory { get; }
    public SettingsCategoryGroup Group { get; }
    public string? SourceKey { get; }
    public string? Route { get; }

    public Visibility Visibility => Group.IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    internal void RefreshExpansion() => OnPropertyChanged(nameof(Visibility));

    public override string ToString() => Label;
}
