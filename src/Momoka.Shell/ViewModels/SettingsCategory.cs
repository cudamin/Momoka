using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 设置左栏的一组分类。Momoka 组与单服务器时的产品组不折叠；多服务器时的服务器组可折叠，
/// 默认收起，靠组标题展开。
/// <para>
/// <see cref="Entries"/> 只含真正能打开的页面，供平铺下拉与选择用；枚举本组（左栏的 ItemsSource）
/// 时还会带上服务器原生目录的分区标题（<see cref="SettingsCategoryEntry.IsSection"/>），把 Emby 的
/// 「Emby Server／设备／高级」、Jellyfin 的五段、飞牛的两段原样分回来。
/// </para>
/// </summary>
public sealed partial class SettingsCategoryGroup : ObservableObject, IEnumerable<SettingsCategoryEntry>
{
    private readonly List<SettingsCategoryEntry> _entries = [];
    private readonly List<SettingsCategoryEntry> _rows = [];

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

    /// <summary>真正能打开的页面，不含分区标题。</summary>
    public IReadOnlyList<SettingsCategoryEntry> Entries => _entries;

    /// <summary>左栏实际画出来的行：分区标题与页面交错。</summary>
    public IReadOnlyList<SettingsCategoryEntry> Rows => _rows;

    internal SettingsCategoryEntry Add(string label, string? sourceKey = null, string? route = null, string? cardCategory = null)
    {
        var entry = new SettingsCategoryEntry(this, label, sourceKey, route, cardCategory);
        _entries.Add(entry);
        _rows.Add(entry);
        return entry;
    }

    /// <summary>服务器原生目录的分区标题（「Emby Server」「设备」「高级」等），只画不选。</summary>
    internal SettingsCategoryEntry AddSection(string label)
    {
        var section = new SettingsCategoryEntry(this, label, SourceKey, route: null, cardCategory: label, section: true);
        _rows.Add(section);
        return section;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ChevronGlyph));
        foreach (var row in _rows) row.RefreshExpansion();
    }

    public IEnumerator<SettingsCategoryEntry> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// 左栏的一项。单服务器时 <see cref="Key"/> 就是分类名（旧行为与命令行别名都还认）；多服务器时
/// 加上来源前缀，避免两台服务器的「控制台」「媒体库」撞成一个键。
/// </summary>
public sealed partial class SettingsCategoryEntry : ObservableObject
{
    internal SettingsCategoryEntry(SettingsCategoryGroup group, string label,
        string? sourceKey, string? route, string? cardCategory, bool section = false)
    {
        Group = group;
        Label = label;
        SourceKey = sourceKey;
        Route = route;
        CardCategory = cardCategory ?? label;
        IsSection = section;
        var key = section ? "\u001e" + label : label;
        Key = sourceKey is null ? key : sourceKey + "\u001f" + key;
    }

    public string Key { get; }
    public string Label { get; }

    /// <summary>这一行是服务器原生目录的分区标题，不是可打开的页面。</summary>
    public bool IsSection { get; }

    /// <summary>左栏行内二选一：分区标题只画标题，页面行只画图标与名称（组收起时整行由 Visibility 收走）。</summary>
    public Visibility SectionVisibility => IsSection ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EntryVisibility => IsSection ? Visibility.Collapsed : Visibility.Visible;
    public string PickerLabel => Group.SourceKey is null ? Label : Group.Name + " · " + Label;
    public string CardCategory { get; }
    public SettingsCategoryGroup Group { get; }
    public string? SourceKey { get; }
    public string? Route { get; }

    public Visibility Visibility => Group.IsExpanded ? Visibility.Visible : Visibility.Collapsed;

    internal void RefreshExpansion() => OnPropertyChanged(nameof(Visibility));

    public override string ToString() => Label;
}
