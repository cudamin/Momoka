using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.AniBt;
using Momoka.Emby;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class AniBtScrapeDialog : ContentDialog
{
    public AniBtScrapeViewModel ViewModel { get; } = new();
    private bool _submitting;
    private bool _operating;

    internal AniBtScrapeDialog(AniBtService service, EmbySessionScope scope, EmbyItem item)
    {
        InitializeComponent();
        Title = "AniBT 重新整理";
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Opened += async (_, _) => await ViewModel.LoadAsync(service, scope, item);
        Closing += (_, args) => { if (_submitting) args.Cancel = true; };
        Closed += (_, _) => ViewModel.Cancel();
    }

    private async void OnSubmit(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_operating) return;
        if (!ViewModel.CanAct) { ViewModel.RefreshActions(); return; }
        _operating = true;
        _submitting = true;
        try { await ViewModel.SubmitAsync(); }
        finally { _submitting = false; _operating = false; }
    }

    private async void OnRetryPrepare(object sender, RoutedEventArgs args)
    {
        if (_operating) return;
        _operating = true;
        try { await ViewModel.PreviewAsync(); }
        finally { _operating = false; }
    }
}
