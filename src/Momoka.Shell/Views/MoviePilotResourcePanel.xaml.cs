using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>
/// 一部片的资源（种子）列表，作为 <see cref="MoviePilotSearchPanel"/> 之上的一层覆盖面板。内容全在
/// <see cref="MoviePilotResourceViewModel"/>；这里留的是：把服务和媒体交给视图模型并开搜、把下载确认接到本控件的
/// <c>XamlRoot</c>、以及那颗返回键。进入面板后先选站点，由搜索按钮发起请求。
/// <para>
/// 是覆盖面板而不是 <see cref="Windows.UI.Popups"/> 或 ContentDialog：下载要弹 <see cref="ConfirmDialog"/>（一个
/// ContentDialog），而 WinUI 同时只让一个 ContentDialog 在场，列表若也是对话框就打架。做成面板，确认框就能照弹。
/// </para>
/// </summary>
public sealed partial class MoviePilotResourcePanel : UserControl
{
    public MoviePilotResourcePanel()
    {
        InitializeComponent();
    }

    /// <summary>返回键按下：由 <see cref="MoviePilotSearchPanel"/> 收起这层覆盖面板。</summary>
    public event EventHandler? Closed;

    internal MoviePilotResourceViewModel ViewModel { get; } = new();

    /// <summary>接上媒体、站点选择和下载对话框，等待用户确定搜索范围。</summary>
    internal void Open(MoviePilotService service, MoviePilotMedia media, ISystemLauncher launcher, MoviePilotMediaSearchOptions? options = null)
    {
        ViewModel.Attach(service, media, launcher, options);
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
}
