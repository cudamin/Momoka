using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Momoka.Shell.Views;

internal sealed record FnNasPasswordRequest(IServiceProvider Services, string? SourceKey = null);

public sealed partial class FnNasPasswordPage : Page, IShellContent, ISessionScopedPage
{
    private FnNasPasswordRequest? _request;

    public bool IsCurrentSession => ViewModel.IsCurrentSession;
    public string? SourceKey => ViewModel.SourceKey;

    public FnNasPasswordPage()
    {
        InitializeComponent();
        Loaded += (_, _) => HomeMotion.Reveal(PageLayout);
        Unloaded += (_, _) => Release();
    }

    internal FnNasPasswordViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not FnNasPasswordRequest request) return;
        _request = request;
        ViewModel.Attach(request.Services.GetRequiredService<EmbySession>(), request.SourceKey);
        _ = ViewModel.ReloadAsync();
    }

    public void Release()
    {
        ViewModel.Cancel();
        HomeMotion.Stop(PageLayout);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Release();
        base.OnNavigatedFrom(e);
    }
}
