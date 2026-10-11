using Momoka.AniBt;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class AniBtScrapeDialog : ContentDialog
{
    public AniBtScrapeViewModel ViewModel { get; } = new();
    private bool _submitting;

    internal AniBtScrapeDialog(AniBtService service, EmbySessionScope scope, EmbyItem item)
    {
        InitializeComponent();
        Title = $"AniBT 识别 — {item.Name}";
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Opened += async (_, _) => await ViewModel.LoadAsync(service, scope, item);
        Closing += (_, args) => { if (_submitting) args.Cancel = true; };
        Closed += (_, _) => ViewModel.Cancel();
    }

    private async void OnSubmit(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (!ViewModel.CanSubmit || _submitting) return;
        _submitting = true;
        try { await ViewModel.SubmitAsync(); }
        finally { _submitting = false; }
    }
}
