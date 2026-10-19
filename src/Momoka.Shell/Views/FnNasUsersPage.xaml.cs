using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Momoka.Shell.Views;

internal sealed record FnNasUsersRequest(IServiceProvider Services, string? SourceKey = null);

public sealed partial class FnNasUsersPage : Page, IShellContent, ISessionScopedPage
{
    private FnNasUsersRequest? _request;

    public bool IsCurrentSession => ViewModel.IsCurrentSession;
    public string? SourceKey => ViewModel.SourceKey;
    private ContentDialog? _dialog;
    internal FnNasUsersViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;

    public FnNasUsersPage()
    {
        InitializeComponent();
        ViewModel.UseConfirm(ConfirmAsync);
        Loaded += (_, _) => HomeMotion.Reveal(PageLayout);
        Unloaded += (_, _) => Release();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not FnNasUsersRequest request) return;
        _request = request;
        // 此页唯一服务解析点；外部交互通过视图模型及已捕获的会话身份。
        ViewModel.Attach(request.Services.GetRequiredService<EmbySession>(), request.SourceKey);
        _ = ViewModel.ReloadAsync();
    }

    private void OnUserClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FnNasUserRow row) ViewModel.Open(row);
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
        AutomationProperties.SetAutomationId(dialog, "FnNasUsersConfirmDialog");
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
