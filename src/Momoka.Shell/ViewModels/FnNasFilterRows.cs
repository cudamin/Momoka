using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 飞牛筛选面板上的一个可选值。一行同时只有一个值生效（官网页端也是「全部」加一组单选文字），
/// 所以选中一个就把同一行其余项取消；再点已选中的那一项不会让这一行变成空。
/// </summary>
public sealed partial class FnNasFilterChoice(string value, string label, FnNasFilterRow row) : ObservableObject
{
    public string Value { get; } = value;
    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => row.ChoiceChanged(this, value);
}

/// <summary>
/// 「影视分类 / 类型 / 分辨率…」中的一行。值保持服务器原始形状（题材 id、4k、HDR10），
/// 中文只活在 <see cref="FnNasFilterChoice.Label"/> 里，查询不被显示文字污染。
/// </summary>
public sealed partial class FnNasFilterRow(string key, string label, Action changed) : ObservableObject
{
    private bool _syncing;

    public string Key { get; } = key;
    public string Label { get; } = label;
    public ObservableCollection<FnNasFilterChoice> Choices { get; } = [];

    internal string SelectedValue => Choices.FirstOrDefault(choice => choice.IsSelected)?.Value ?? "";
    internal string SelectedLabel => Choices.FirstOrDefault(choice => choice.IsSelected)?.Label ?? "";

    internal void Add(string value, string display) => Choices.Add(new FnNasFilterChoice(value, display, this));

    /// <summary>回填一份已有选择；选中的值已经不在这张表里就退回「全部」。</summary>
    internal void Restore(string value)
    {
        if (Choices.All(choice => choice.Value != value)) value = "";
        _syncing = true;
        foreach (var choice in Choices) choice.IsSelected = choice.Value == value;
        _syncing = false;
    }

    internal void Reset() => Restore("");

    internal void ChoiceChanged(FnNasFilterChoice choice, bool selected)
    {
        if (_syncing) return;

        if (!selected)
        {
            // 一行必须留一个值：「全部」也是值。再点一次已选中的项不该把整行清成空白。
            _syncing = true;
            choice.IsSelected = true;
            _syncing = false;
            return;
        }

        _syncing = true;
        foreach (var other in Choices)
            if (!ReferenceEquals(other, choice)) other.IsSelected = false;
        _syncing = false;
        changed();
    }
}
