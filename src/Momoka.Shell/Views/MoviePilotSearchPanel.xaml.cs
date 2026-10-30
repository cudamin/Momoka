using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>
/// MoviePilot 搜索页里那一段的视图。全部内容都是 <see cref="MoviePilotSearchViewModel"/> 的；这里留的只有
/// 视图才做得了的几件事：把连接服务和按季设置接进视图模型、把页面共享的搜索框提交转成一次搜索、点某条
/// 结果的「搜索资源」时把资源覆盖面板盖上来（那要 <c>XamlRoot</c>，只有在树里的控件给得出），以及把卡片
/// 被调用（点封面/片名/回车）转给视图模型的打开动作。
/// <para>
/// 和别的页一样自建视图模型、构造时就建好（<c>x:Bind</c> 不许有 null 根，见 <see cref="FilterPanel"/>）；季选择
/// 对话框使用 MoviePilot 的统一失效守卫；电影按钮按明确状态直接执行，不再二次确认。
/// </para>
/// </summary>
public sealed partial class MoviePilotSearchPanel : UserControl
{
    // 与详情页共用图片遮罩的墨色，透明端也保持相同 RGB，避免透明白插值形成亮带。
    public static Windows.UI.Color FadeStart() => Fade(0);
    public static Windows.UI.Color FadeMiddle() => Fade(180);
    public static Windows.UI.Color FadeEnd() => Fade(255);
    private static Windows.UI.Color Fade(byte alpha)
    {
        var ink = Momoka.Emby.DetailHero.ScrimInk;
        return Windows.UI.Color.FromArgb(alpha, ink.R, ink.G, ink.B);
    }

    private MoviePilotService? _service;
    private ISystemLauncher? _launcher;
    private bool _active;
    private int _visit;

    public MoviePilotSearchPanel()
    {
        InitializeComponent();
    }

    internal MoviePilotSearchViewModel ViewModel { get; } = new();

    /// <summary>
    /// 由 <see cref="LibraryPage"/> 在它那唯一一处解析块里调一次：把连接服务交给视图模型、把「电视剧订阅季设置」
    /// 接到本控件的 <c>XamlRoot</c> 上，并把「搜索资源」的入口交给视图模型（它只把媒体递回来，开面板归这里）。
    /// </summary>
    internal void Attach(MoviePilotService service, ISystemLauncher launcher,
        Action<MoviePilotMedia, MoviePilotSubscription?>? openMedia = null)
    {
        var beforeRelease = _visit;
        Release();
        if (_visit != unchecked(beforeRelease + 1)) return;
        _service = service;
        _launcher = launcher;
        _active = true;
        var queue = DispatcherQueue;
        var visit = _visit;
        ViewModel.Attach(service, action => queue.TryEnqueue(() => action()));
        if (!_active || visit != _visit) return;
        ViewModel.UseResourceOpener(ShowResourcesAsync);
        ViewModel.UseSeasonPicker((media, token) => MoviePilotSeasonPicker.ShowAsync(this, service, media, "", media.Season, token));
        if (openMedia is not null) ViewModel.UseMediaOpener(openMedia);
    }

    internal Task PrepareSearchAsync()
    {
        if (!_active) return Task.CompletedTask;
        ResourceOverlay.Release();
        ResourceOverlay.Visibility = Visibility.Collapsed;
        SearchContent.Visibility = Visibility.Visible;
        Results.Visibility = Visibility.Visible;
        return ViewModel.SearchAsync("");
    }

    /// <summary>页面上那个共享搜索框提交、且此刻在 MoviePilot 段时，由页面把词转到这里。</summary>
    internal Task SearchAsync(string term)
    {
        if (!_active) return Task.CompletedTask;
        ResourceOverlay.Release();
        ResourceOverlay.Visibility = Visibility.Collapsed;
        SearchContent.Visibility = Visibility.Visible;
        Results.Visibility = Visibility.Visible;
        return ViewModel.SearchAsync(term);
    }

    /// <summary>同页换搜索来源只取消本次查询，不永久停用，也不自动重新搜索。</summary>
    internal void CancelSearch()
    {
        ViewModel.Cancel();
        ResourceOverlay.Release();
        ResourceOverlay.Visibility = Visibility.Collapsed;
        SearchContent.Visibility = Visibility.Visible;
        Results.Visibility = Visibility.Visible;
    }

    /// <summary>真正离页时退订单例事件、解除页面回调并释放结果；返回时由 Attach 重新接线。</summary>
    internal void Release()
    {
        _active = false;
        var visit = ++_visit;
        _service = null;
        _launcher = null;
        ViewModel.Release();
        if (visit != _visit) return;
        ResourceOverlay.Release();
        ResourceOverlay.Visibility = Visibility.Collapsed;
        SearchContent.Visibility = Visibility.Visible;
        Results.Visibility = Visibility.Visible;
    }

    /// <summary>点某条结果的「搜索资源」：把资源覆盖面板盖上来并开搜。</summary>
    private Task ShowResourcesAsync(MoviePilotMedia media)
    {
        if (!_active || _service is null || _launcher is null) return Task.CompletedTask;

        ResourceOverlay.Open(_service, media, _launcher);
        SearchContent.Visibility = Visibility.Collapsed;
        Results.Visibility = Visibility.Collapsed;
        ResourceOverlay.Visibility = Visibility.Visible;
        return Task.CompletedTask;
    }

    /// <summary>点一张结果卡：进这部媒体的订阅页（唯一对上的订阅随行，见视图模型的 <c>Open</c>）。</summary>
    private void OnResultInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not MoviePilotResult result) return;
        OpenResult(result);
    }

    /// <summary>探针：绕开真实指针，直接走「卡片被调用」这一支。</summary>
    internal void OpenResult(MoviePilotResult result) => ViewModel.Open(result);

    /// <summary>资源面板的返回键：收起它，回到片子列表。</summary>
    private void OnResourceClosed(object? sender, EventArgs e)
    {
        ResourceOverlay.Visibility = Visibility.Collapsed;
        SearchContent.Visibility = Visibility.Visible;
        Results.Visibility = Visibility.Visible;
        ResourceOverlay.Release();
    }
}
