using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

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

    // 直接绑定空 URL 会让 WinUI 的 string → ImageSource 转换抛异常；明确返回可空图片对象。
    public static Microsoft.UI.Xaml.Media.Imaging.BitmapImage? CreateGroupLogo(string? address)
        => AniBtArtwork.Create(address, 24);

    private void OnGroupLogoFailed(object sender, ExceptionRoutedEventArgs args)
    {
        if (sender is Image { Tag: AniBtResourceGroup group }) group.MarkLogoFailed();
    }
}
