using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 媒体库搜索结果里的一组同类型条目。组与它自己的卡片清单都保持同一个实例：分页只会往对应的组里追加，
/// 清空一次搜索才把每组同时清掉；屏上那一排因此不会因为下一页到达而整块重建。
/// <para>
/// 空组不出现在 <c>SearchGroups</c> 里（不是留一个收起的外壳）：版面排的是这一份可见清单，
/// 收起的外壳仍会被排进一条空格，组与组之间的距离就不对了。
/// </para>
/// </summary>
public sealed partial class SearchResultGroup : ObservableObject
{
    internal SearchResultGroup(LibrarySearchGroupKind kind)
    {
        Kind = kind;
        Title = LibrarySearchGroups.TitleOf(kind);
    }

    internal LibrarySearchGroupKind Kind { get; }

    public string Title { get; }

    public ObservableCollection<CardItem> Cards { get; } = [];

    /// <summary>组里这一排卡片的格宽，跟着窗口的格宽走；行高由视图模型按同一档算好递进来。</summary>
    [ObservableProperty]
    public partial int CardWidth { get; set; }

    [ObservableProperty]
    public partial double RowHeight { get; set; }
}
