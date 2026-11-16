using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Services;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

internal sealed record LocalMediaRequest(IServiceProvider Services);

public sealed partial class LocalMediaPage : Page, IShellContent
{
    public LocalMediaViewModel ViewModel { get; } = new();
    private LocalMediaRequest? _request;
    private MoviePilotService? _moviePilot;
    private ContentDialog? _confirmation;
    private bool _released;
    public object? NavigationRequest => _request;

    public LocalMediaPage()
    {
        InitializeComponent();
        ViewModel.Confirm = ConfirmAsync;
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (args.Parameter is LocalMediaRequest request) Attach(request);
    }

    internal void Attach(LocalMediaRequest request)
    {
        _request = request;
        _released = false;
        _moviePilot = request.Services.GetRequiredService<MoviePilotService>();
        ViewModel.Attach(request.Services.GetRequiredService<ISettingsService>(), request.Services.GetRequiredService<ISecretProtector>(), _moviePilot);
        if (request.Services.GetService<Services.LocalLibraryCoordinator>() is { } coordinator) ViewModel.UseLibraries(coordinator);
        _moviePilot.AvailabilityChanged += AvailabilityChanged;
    }

    private void AvailabilityChanged(object? sender, EventArgs args)
    {
        // 立即取消，避免已排队的 UI 回调让失效连接再执行一个文件。
        if (ViewModel.IsMoviePilot) ViewModel.Stop();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_released) return;
            if (ViewModel.IsMoviePilot) _confirmation?.Hide();
            ViewModel.RefreshAvailability();
        });
    }

    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void OnModesSizeChanged(object sender, SizeChangedEventArgs args)
    {
        // 量的是卡片内真正可用的宽度，独立页面和设置窗口的左栏占宽都能正确折行。
        VisualStateManager.GoToState(this, args.NewSize.Width >= 520 ? "LocalMediaWide" : "LocalMediaNarrow", false);
    }
    private async void OnScan(object sender, RoutedEventArgs args) => await ViewModel.ScanAsync();
    private async void OnApply(object sender, RoutedEventArgs args) => await ViewModel.ApplyAsync();
    private async void OnSearch(object sender, RoutedEventArgs args) => await ViewModel.SearchAsync();
    private async void OnUpdatePreview(object sender, RoutedEventArgs args) => await ViewModel.CorrectAsync();
    private void OnNewLibrary(object sender, RoutedEventArgs args) => ViewModel.NewLibrary();
    private async void OnSaveLibrary(object sender, RoutedEventArgs args) => await ViewModel.SaveLibraryAsync();
    private async void OnRemoveLibrary(object sender, RoutedEventArgs args) => await ViewModel.RemoveLibraryAsync();
    private void OnStop(object sender, RoutedEventArgs args) => ViewModel.Stop();
    private void OnSaveToken(object sender, RoutedEventArgs args) { ViewModel.SaveToken(LocalMediaToken.Password); LocalMediaToken.Password = ""; }
    private void OnClearToken(object sender, RoutedEventArgs args) { ViewModel.SaveToken(""); LocalMediaToken.Password = ""; }
    private void OnCorrectFile(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Busy || sender is not FrameworkElement { Tag: LocalMediaRow row } || row.Attempted) return;
        ViewModel.CurrentRow = row;
        LocalMediaCorrection.StartBringIntoView();
    }
    private async void OnPickSource(object sender, RoutedEventArgs args)
    {
        if (await PickFolderAsync("选择源目录") is { } path && !_released) ViewModel.SourceDirectory = path;
    }
    private async void OnPickTarget(object sender, RoutedEventArgs args)
    {
        if (await PickFolderAsync("选择目标目录") is { } path && !_released) ViewModel.TargetDirectory = path;
    }
    private async Task<string?> PickFolderAsync(string title)
    {
        if (ViewModel.Busy || XamlRoot?.ContentIslandEnvironment.AppWindowId is not { Value: not 0 } id) return null;
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(id) { Title = title };
            return (await picker.PickSingleFolderAsync())?.Path;
        }
        catch (Exception) { ViewModel.Notice = "无法打开文件夹选择器，也可以直接填写目录路径（支持本机盘符与 \\\\服务器\\共享）。"; return null; }
    }
    private async Task<bool> ConfirmAsync(string message)
    {
        if (_released || XamlRoot is null || _confirmation is not null) return false;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "确认本机整理",
            Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, MaxWidth = 520 } },
            PrimaryButtonText = "按预览执行",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        AutomationProperties.SetAutomationId(dialog, "LocalMediaConfirm");
        _confirmation = dialog;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary && !_released; }
        finally { _confirmation = null; }
    }
    public void Release()
    {
        _released = true;
        _confirmation?.Hide();
        if (_moviePilot is { } service) service.AvailabilityChanged -= AvailabilityChanged;
        _moviePilot = null;
        ViewModel.Dispose();
        LocalMediaToken.Password = "";
    }
    protected override void OnNavigatedFrom(NavigationEventArgs args) { Release(); base.OnNavigatedFrom(args); }
}
