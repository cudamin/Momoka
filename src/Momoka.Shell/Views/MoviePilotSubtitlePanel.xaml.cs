using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.Localization;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

/// <summary>
/// 搜索页「MoviePilot 字幕」那一格。内容全在 <see cref="MoviePilotSubtitleSearchViewModel"/>；这里留的是
/// 视图才做得了的几件事：把连接服务接进视图模型、把字幕保存路径那张小表接到本控件的 <c>XamlRoot</c>、
/// 以及把页面共享搜索框的提交转成一次找片。
/// <para>
/// 从订阅详情「搜索字幕」进来时已经知道是哪部片，调 <see cref="OpenAsync"/> 直接读字幕，不经过找片。
/// </para>
/// </summary>
public sealed partial class MoviePilotSubtitlePanel : UserControl
{
    public MoviePilotSubtitlePanel() => InitializeComponent();

    internal MoviePilotSubtitleSearchViewModel ViewModel { get; } = new();

    internal void Attach(MoviePilotService service)
    {
        ViewModel.Attach(service);
        SearchProgress.Attach(ViewModel.SearchProgress);
        ViewModel.ChooseSubtitlePath = async (subtitle, paths, token) =>
        {
            var path = new ComboBox { Header = UiText.Translate("服务器保存路径"), IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = UiText.Translate("留空使用 MoviePilot 默认路径") };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(path, "SubtitleSavePath");
            foreach (var value in paths) path.Items.Add(value);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = UiText.Translate("下载字幕"),
                PrimaryButtonText = UiText.Translate("下载"),
                CloseButtonText = UiText.Translate("取消"),
                DefaultButton = ContentDialogButton.Close,
                Content = path,
                RequestedTheme = ActualTheme
            };
            using var cancelled = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            return await MoviePilotDialog.ShowAsync(dialog, service) == ContentDialogResult.Primary && !token.IsCancellationRequested ? path.Text : null;
        };
    }

    internal Task SearchAsync(string keyword) => ViewModel.SearchAsync(keyword);

    /// <summary>订阅详情「搜索字幕」：已经知道是哪部片，直接读它的字幕。</summary>
    internal Task OpenAsync(MoviePilotMedia media, int? episode) => ViewModel.LoadSubtitlesAsync(media, episode);

    internal void Clear() => ViewModel.Clear();

    /// <summary>换来源或离页：取消在飞的搜索。</summary>
    internal void Release() => ViewModel.Release();
}
