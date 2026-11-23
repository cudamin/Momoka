using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

/// <summary>
/// 一部片的资源（种子）列表，作为 <see cref="MoviePilotSearchPanel"/> 之上的一层覆盖面板。内容全在
/// <see cref="MoviePilotResourceViewModel"/>；这里留的是：把服务和媒体交给视图模型并开搜、把下载确认接到本控件的
/// <c>XamlRoot</c>、以及面板顶上的「返回」「搜索资源」两颗键。
/// <para>
/// 订阅详情那份资源结果页已按用户令（2026-11-22）撤掉，入口改在搜索页的 MoviePilot 资源来源；这一层现在只服务
/// MoviePilot 订阅结果卡上的「搜索资源」。
/// </para>
/// </summary>
public sealed partial class MoviePilotResourcePanel : UserControl
{
    public MoviePilotResourcePanel()
    {
        InitializeComponent();
    }

    /// <summary>返回键按下：由宿主收起这层覆盖面板。</summary>
    public event EventHandler? Closed;

    internal MoviePilotResourceViewModel ViewModel { get; } = new();

    /// <summary>接上媒体、站点选择和下载对话框，等待用户确定搜索范围。</summary>
    internal void Open(MoviePilotService service, MoviePilotMedia media, ISystemLauncher launcher, MoviePilotMediaSearchOptions? options = null)
    {
        ViewModel.Attach(service, media, launcher, options);
        SearchProgress.Attach(ViewModel.SearchProgress);
        ResourceList.Attach(ViewModel.Browser);
        SearchSitePicker.Attach(ViewModel.SearchSites);
        ViewModel.UseDownloadPicker((resource, token) => MoviePilotDownloadDialog.ShowAsync(this, service, resource, token));
        ViewModel.ChooseSubtitlePath = async (subtitle, paths, token) =>
        {
            var path = new ComboBox { Header = "服务器保存路径", IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "留空使用 MoviePilot 默认路径" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(path, "SubtitleSavePath");
            foreach (var value in paths) path.Items.Add(value);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "下载字幕",
                PrimaryButtonText = "下载",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                Content = path,
                RequestedTheme = ActualTheme
            };
            using var cancelled = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            return await MoviePilotDialog.ShowAsync(dialog, service) == ContentDialogResult.Primary && !token.IsCancellationRequested ? path.Text : null;
        };
        ViewModel.PrepareSearch();
        // 调用方通常先 Open 再把面板显示出来；等这一拍同步代码走完再量标题栏那一格。
        DispatcherQueue.TryEnqueue(UpdateTitleBleed);
    }

    /// <summary>收起时收手：取消在飞的搜索。</summary>
    internal void Release()
    {
        ViewModel.Cancel();
        SearchSitePicker.ClosePopup();
        ResourceList.ClosePopups();
    }

    private void OnRetry(object sender, RoutedEventArgs e) => _ = ViewModel.SearchAsync();

    private void OnClose(object sender, RoutedEventArgs e) => Closed?.Invoke(this, EventArgs.Empty);

    private void OnPanelLoaded(object sender, RoutedEventArgs args) => UpdateTitleBleed();

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs args) => UpdateTitleBleed();

    /// <summary>
    /// 把面板的纸色往上铺到窗口顶：面板只从页面顶端（标题栏下面那 48 像素）开始画，上面那一格仍是详情页的
    /// 剧照（沉浸模式），两种颜色在那条线上断开。落位按本控件相对 XamlRoot 的实测偏移量算，商店模式那一档
    /// 外壳的实色标题栏压在它上面，屏上不变的仍是标题栏那条面色。
    /// </summary>
    private void UpdateTitleBleed()
    {
        if (XamlRoot?.Content is not UIElement root || ActualWidth <= 0)
        {
            TitleBleed.Visibility = Visibility.Collapsed;
            return;
        }
        var top = TransformToVisual(root).TransformPoint(default).Y;
        if (top <= 0.5)
        {
            TitleBleed.Visibility = Visibility.Collapsed;
            return;
        }
        TitleBleed.Height = top;
        TitleBleed.Margin = new Thickness(0, -top, 0, 0);
        TitleBleed.Visibility = Visibility.Visible;
    }
}
