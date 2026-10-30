using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>
/// 「在 MoviePilot 搜索其他版本」独立窗口里装的内容。全部内容在 <see cref="MoviePilotVersionsViewModel"/>；这里
/// 留的是视图才做得了的几件事：把连接服务和确认对话框接进视图模型、把搜索框的提交转成一次搜索、以及进来时按
/// 算好的关键字填框并立刻开搜。
/// <para>
/// 和别的页一样自建视图模型、构造时就建好（<c>x:Bind</c> 不许有 null 根）；确认对话框走
/// <see cref="ConfirmDialog.For"/>，和其它页问「不可撤销的事」是同一条路。
/// </para>
/// <para>
/// 搜索框与站点入口照搜索页那一份做（<see cref="SearchSiteSlot"/>）：站点在框内、放大镜左边（2026-10-30
/// 用户令）。这一页没有来源栏，所以框下没有第二条站点行。
/// </para>
/// </summary>
public sealed partial class MoviePilotVersionsView : UserControl
{
    /// <summary>站点下拉的宽度上限，窄窗收一档 —— 和搜索页同一个数（见 LibraryPage.ApplyHeaderLayout）。</summary>
    private double _siteCap;

    /// <summary>站点入口已经搬进搜索框模板了。窗口是建一次留着复用的，搬过就不再搬。</summary>
    private bool _sitePlaced;

    public MoviePilotVersionsView()
    {
        InitializeComponent();
        SearchInput.Loaded += (_, _) => PlaceSearchSites();
        VersionsLayout.SizeChanged += (_, _) => FitSitePicker();
    }

    internal MoviePilotVersionsViewModel ViewModel { get; } = new();

    /// <summary>
    /// 由 <see cref="MoviePilotWindow"/> 每次弹出前调一次：接上连接服务与确认对话框，把算好的关键字填进搜索框
    /// 并立刻搜。填进框里而不是只搜一次，是为了让用户看得见发的是什么词、也好在原地改词放宽范围。
    /// <paramref name="episode"/> 是集页面带来的目标集（词里没有集号了，搜完由浏览器自动按它筛）；
    /// 用户在框里改词自己搜走的是 <see cref="OnSearch"/>，不带这个参数，自动筛选随之解除。
    /// </summary>
    internal void Search(MoviePilotService service, string keyword, ISystemLauncher launcher, int? episode = null)
    {
        Attach(service, launcher);
        SearchInput.Text = keyword;
        _ = ViewModel.SearchAsync(keyword, episode);
    }

    internal void Attach(MoviePilotService service, ISystemLauncher launcher, bool sharedInput = false)
    {
        ViewModel.Attach(service, launcher);
        Browser.Attach(ViewModel.Browser);
        SearchSitePicker.Attach(ViewModel.SearchSites);
        ViewModel.UseDownloadPicker((resource, token) => MoviePilotDownloadDialog.ShowAsync(this, service, resource, token));
        // 页内嵌（搜索页）时整条搜索区都收起来，不只是框和站点：外层那一格还带着 12 的下边距，
        // 留着它就是搜索结果上方多出来的一道空白（搜索页紧凑布局的探针当场量得出来）。
        SearchEntry.Visibility = sharedInput ? Visibility.Collapsed : Visibility.Visible;
        SearchInput.Visibility = sharedInput ? Visibility.Collapsed : Visibility.Visible;
        // 页内嵌（搜索页）用的是页自己那颗框和站点入口，这一颗跟着框一起收起；独立窗口里等搬进框里再露出来。
        SearchSitePicker.Visibility = SearchInput.Visibility == Visibility.Visible && _sitePlaced
            ? Visibility.Visible
            : Visibility.Collapsed;
        VersionsLayout.Padding = new Thickness(sharedInput ? 0 : 16);
    }

    internal Task SearchAsync(string keyword) => ViewModel.SearchAsync(keyword);

    /// <summary>窗口收起时收手：取消在飞的搜索。</summary>
    internal void Release()
    {
        ViewModel.Cancel();
        SearchSitePicker.ClosePopup();
        Browser.ClosePopups();
    }

    private void OnSearch(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = ViewModel.SearchAsync(args.QueryText ?? string.Empty);

    /// <summary>
    /// 把站点入口交给搜索框模板、排在放大镜左边。模板是框架给的，只有 Loaded 之后才建得出来；
    /// 搬完才让它露面 —— 搬之前它是 SearchEntry 的兄弟，露着会压在输入文字上。
    /// </summary>
    private void PlaceSearchSites()
    {
        if (!SearchSiteSlot.Place(SearchInput, SearchSitePicker)) return;
        _sitePlaced = true;
        SearchSitePicker.Visibility = SearchInput.Visibility;
        FitSitePicker();
    }

    /// <summary>窄窗把站点下拉收窄一档（搜索框里的可用宽度要留给关键词），宽窄两档和搜索页一致。</summary>
    private void FitSitePicker()
    {
        var width = Math.Max(0,
            VersionsLayout.ActualWidth - VersionsLayout.Padding.Left - VersionsLayout.Padding.Right);
        var cap = width < 520 ? 108 : 180;
        if (Math.Abs(cap - _siteCap) < 0.5) return;
        _siteCap = cap;
        SearchSitePicker.SetCompact(cap);
    }
}
