using Momoka.Shell.ViewModels;
using Momoka.Shell.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class AniBtResourceBrowser : UserControl
{
    public AniBtBrowserViewModel ViewModel { get; private set; } = new();

    public AniBtResourceBrowser() => InitializeComponent();

    internal void Attach(AniBtBrowserViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    internal void ClosePopups()
    {
        RssNotice.IsOpen = false;
        foreach (var picker in new[] { EpisodeFilter, GroupFilter, ResolutionFilter, SortOrder })
            picker.IsDropDownOpen = false;
    }

    private void OnCopyGroupRss(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: AniBtResourceGroup { RssUri: { } uri } }) return;
        try
        {
            new SystemClipboard().SetText(uri.AbsoluteUri);
            RssNotice.Message = "已复制字幕组 RSS 链接";
            RssNotice.Severity = InfoBarSeverity.Success;
        }
        catch (Exception error)
        {
            RssNotice.Message = "复制 RSS 链接失败：" + error.Message;
            RssNotice.Severity = InfoBarSeverity.Error;
        }
        RssNotice.IsOpen = true;
    }
}
