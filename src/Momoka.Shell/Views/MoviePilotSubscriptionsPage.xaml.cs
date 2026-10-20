using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Momoka.Shell.Views;

/// <summary>
/// 侧边栏那两条 MoviePilot 订阅入口要打开的一页。<see cref="Key"/> 是
/// <see cref="HomeLayout.MoviePilotMovies"/> 或 <see cref="HomeLayout.MoviePilotSeries"/> —— 主页那两排用的是
/// 同一对钥匙，所以标题和筛选都只有一处来源。
/// </summary>
/// <param name="Open">
/// 点一张订阅卡片之后往哪儿走。外壳递进来（打开订阅详情是外壳的事），页面只负责把卡片交出去。
/// </param>
internal sealed record MoviePilotSubscriptionsRequest(MoviePilotService Service, string Key, Action<MoviePilotSubscription> Open);

/// <summary>
/// 一页 MoviePilot 订阅（电影或电视剧）。数据、刷新与菜单全部来自
/// <see cref="MoviePilotSubscriptionsView"/> 那一套 —— 这一页只给它一个标题、一条读取中的转圈和一个空态，
/// 另加一层「连不上就退出去」的寿命管理。
/// </summary>
public sealed partial class MoviePilotSubscriptionsPage : Page, IShellContent
{
    private MoviePilotSubscriptionsRequest? _request;
    private MoviePilotHomeSubscriptions? _owner;

    public MoviePilotSubscriptionsPage() => InitializeComponent();

    /// <inheritdoc />
    public object? NavigationRequest => _request;

    internal MoviePilotSubscriptionsViewModel ViewModel => Subscriptions.ViewModel;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MoviePilotSubscriptionsRequest request) Attach(request);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Release();
        base.OnNavigatedFrom(e);
    }

    internal void Attach(MoviePilotSubscriptionsRequest request)
    {
        Release();
        _request = request;

        // 自己持一份订阅数据（读取、刷新计时器、连接生命周期都在它身上）：这一页和主页那两排读的是同一份
        // 服务器数据，但两边的存活期不一样，共用一只计时器只会让谁先走谁把它停掉。
        _owner = new MoviePilotHomeSubscriptions();
        var series = request.Key == HomeLayout.MoviePilotSeries;
        PageTitle.Text = HomeLayout.FixedTitle(request.Key)!;
        EmptyText.Text = series ? "暂无电视剧订阅。" : "暂无电影订阅。";
        Subscriptions.AttachRow(_owner, series ? _owner.Series : _owner.Movies, request.Service, request.Open);

        // 转圈／空态那两档绑的是**这一页的 ViewModel**，而它要等 AttachRow 把 Row 落定才指向这一页自己那份
        // 订阅数据（AttachRow 之前拿到的是 MoviePilotSubscriptionsView 的备用属主）。绑定路径换了一个对象，
        // 所以这里重算一遍 —— 与 MoviePilotSubscriptionsView 在 RowProperty 变化时做的是同一件事。
        Bindings.Update();
    }

    /// <inheritdoc />
    public void Release()
    {
        if (_owner is null) return;
        Subscriptions.DetachRow();
        Subscriptions.Release();
        _owner.Release();
        _owner = null;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = ViewModel.ReloadAsync();
}
