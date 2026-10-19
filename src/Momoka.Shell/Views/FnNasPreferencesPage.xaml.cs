using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Momoka.Shell.Views;

internal sealed record FnNasPreferencesRequest(IServiceProvider Services);

public sealed partial class FnNasPreferencesPage : Page, IShellContent, ISessionScopedPage
{
    private FnNasPreferencesRequest? _request;

    public bool IsCurrentSession => ViewModel.IsCurrentSession;

    public FnNasPreferencesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => HomeMotion.Reveal(PageLayout);
        Unloaded += (_, _) => Release();
    }

    internal FnNasPreferencesViewModel ViewModel { get; } = new();
    public object? NavigationRequest => _request;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not FnNasPreferencesRequest request) return;
        _request = request;
        ViewModel.Attach(request.Services.GetRequiredService<EmbySession>());
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
