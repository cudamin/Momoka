using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Momoka.Shell.Views;

internal sealed record FnNasLibrariesRequest(IServiceProvider Services, string? SourceKey = null);

public sealed partial class FnNasLibrariesPage : Page, IShellContent, ISessionScopedPage
{
    private FnNasLibrariesRequest? _request;

    public bool IsCurrentSession => ViewModel.IsCurrentSession;
    public string? SourceKey => ViewModel.SourceKey;
    private ContentDialog? _dialog;
    internal FnNasLibrariesViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;

    public FnNasLibrariesPage()
    {
        InitializeComponent();
        ViewModel.UseConfirm(ConfirmAsync);
        ViewModel.PickPlaylist = () => FnNasPlaylistFile.PickAsync(XamlRoot);
        Loaded += (_, _) => HomeMotion.Reveal(PageLayout);
        Unloaded += (_, _) => Release();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not FnNasLibrariesRequest request) return;
        _request = request;
        // 此页唯一服务解析点；外部交互通过视图模型及已捕获的会话身份。
        ViewModel.Attach(request.Services.GetRequiredService<EmbySession>(), request.SourceKey);
        _ = ViewModel.ReloadAsync();
    }

    private async void OnLibraryClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FnNasLibraryRow row) await ViewModel.OpenAsync(row);
    }
    private async void OnScan(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FnNasLibraryRow row }) await ViewModel.ScanAsync(row);
    }
    private void OnRemoveFolder(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FnNasChoice row }) ViewModel.RemoveFolder(row);
    }
    private async void OnDirectoryClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FnNasChoice row) await ViewModel.BrowseDirectoryAsync(row);
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primary)
    {
        if (_dialog is not null || XamlRoot is null) return false;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
            PrimaryButtonText = primary,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        AutomationProperties.SetAutomationId(dialog, "FnNasLibrariesConfirmDialog");
        _dialog = dialog;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally { _dialog = null; }
    }

    public void Release()
    {
        ViewModel.Cancel();
        _dialog?.Hide();
        HomeMotion.Stop(PageLayout);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Release();
        base.OnNavigatedFrom(e);
    }
}
