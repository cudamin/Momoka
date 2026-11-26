using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace Momoka.Shell.Views;

/// <summary>What <see cref="ServersPage"/> needs; see <see cref="LibraryRequest"/> for why it is passed.</summary>
internal sealed record ServerRequest(IServiceProvider Services);

/// <summary>
/// Management view for saved Emby servers and their accounts.
/// <para>
/// What is left here is the three things XAML cannot do for itself: a navigation parameter into an
/// <c>Attach</c>, a list click into a view-model call, and the <see cref="ConfirmDialog"/> the delete
/// commands ask through — which needs this page's <c>XamlRoot</c>, so this is the only place that can hand
/// one out. The nine <c>Click</c> handlers that used to pattern-match a <c>Tag</c> back into a row are
/// commands on <see cref="ServersViewModel"/> now.
/// </para>
/// </summary>
public sealed partial class ServersPage : Page, IShellContent
{
    private const string Category = "服务器";

    private ServerRequest? _request;
    private EmbySession? _session;
    private ISettingsService? _settings;
    private SettingsPriorityMotion? _serverMotion;
    private readonly Dictionary<ListView, SettingsPriorityMotion> _accountMotions = [];

    public ServersPage()
    {
        InitializeComponent();
        Loaded += (_, _) => HomeMotion.Reveal(PageLayout);
        Unloaded += (_, _) => HomeMotion.Stop(PageLayout);

        // Wired here rather than on navigation because the dialog captures nothing but this page: the
        // XamlRoot to host it in is read when a question is asked, which is long after it is null.
        ViewModel.UseConfirm(ConfirmDialog.For(this));
    }

    /// <inheritdoc cref="HomePage.ViewModel" />
    internal ServersViewModel ViewModel { get; } = new();

    /// <summary>True once the rows are built; read by the self-check.</summary>
    internal bool IsReady => ViewModel.IsReady;

    /// <summary>How many saved servers the page drew.</summary>
    internal (int Servers, int Accounts) Summary => (
        ViewModel.Servers.Count,
        ViewModel.Servers.Sum(server => server.Accounts.Count));

    /// <summary>
    /// How many server groups and independent account cards the lists actually built, counted off the visual tree rather
    /// than off the bound collections.
    /// <para>
    /// That difference is the whole point of the probe. A <c>DataTemplate</c> is markup the build never
    /// runs, so a resource key that does not resolve or a property that is not there only fails when a
    /// row is realised — which, before this, first happened in front of a user. It is how the 「当前」
    /// badge on this page stayed broken: an <c>InfoBadge</c> whose <c>Value</c> is an <c>int</c>, handed
    /// the string 「当前」, compiled and shipped.
    /// </para>
    /// </summary>
    internal (int Servers, int Accounts) Realised
    {
        get
        {
            var servers = 0;
            var accounts = 0;
            Count(this, ref servers, ref accounts);
            return (servers, accounts);
        }
    }

    /// <inheritdoc />
    public object? NavigationRequest => _request;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not ServerRequest request)
        {
            Log.Warn(Category, "导航参数缺失");
            return;
        }

        _request = request;
        Tag = "servers";

        // The one place this page resolves anything. Deliberately a single block: it is the line that goes
        // when the shell stops handing a container around.
        var services = request.Services;
        if (_session is not null) _session.SourcesChanged -= OnSourcesChanged;
        _session = services.GetRequiredService<EmbySession>();
        _session.SourcesChanged += OnSourcesChanged;
        if (_settings is not null) _settings.SaveStateChanged -= OnSettingsSaved;
        _settings = services.GetRequiredService<ISettingsService>();
        _settings.SaveStateChanged += OnSettingsSaved;

        ViewModel.Attach(
            services.GetRequiredService<IShellActions>(),
            _settings,
            _session);

        _ = ViewModel.ReloadAsync();
    }

    public void Release()
    {
        if (_settings is not null) _settings.SaveStateChanged -= OnSettingsSaved;
        _settings = null;
        if (_session is not null) _session.SourcesChanged -= OnSourcesChanged;
        _session = null;
        DetachMotions();
        ViewModel.Cancel();
    }

    // ---- 排序与拖拽 ------------------------------------------------------------

    /// <summary>排序菜单的条目；探针读它，浮层也用它。</summary>
    internal IReadOnlyList<string> BuildServerSortMenu()
    {
        var entries = ViewModel.SortEntries();
        ServerSortList.ItemsSource = entries;
        return [.. entries.Select(entry => entry.Label)];
    }

    private void OnServerSortMenuOpening(object sender, object e) => BuildServerSortMenu();

    private void OnServerSortItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ServerSortEntry entry) return;

        ViewModel.ChooseSort(entry.Mode);
        ServerSortMenu.Hide();
    }

    private void OnServerSortDirectionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ServerSortMode mode }) return;

        ViewModel.FlipSort(mode);
        BuildServerSortMenu();
    }

    private void OnServerListLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView list || _serverMotion is not null) return;

        _serverMotion = new SettingsPriorityMotion(list, ViewModel.Servers,
            () => ViewModel.Servers.Cast<object>(), ViewModel.BeginServerReorder, ViewModel.EndServerReorder,
            element => element.Tag is ServerRow, item => item is ServerRow);
    }

    private void OnServerListUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView list || list.IsLoaded || _serverMotion is null) return;

        _serverMotion.Detach();
        _serverMotion = null;
    }

    private void OnAccountListLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView list || _accountMotions.ContainsKey(list)) return;

        // DataContext 是首选；Tag 里那份集合是兜底，模板实例刚挂上时 DataContext 还没落定的情况见过。
        var row = list.DataContext as ServerRow
            ?? ViewModel.Servers.FirstOrDefault(server => ReferenceEquals(server.Accounts, list.Tag));
        if (row is null) return;

        _accountMotions[list] = new SettingsPriorityMotion(list, row.Accounts,
            () => row.Accounts.Cast<object>(), row.BeginAccountReorder, row.EndAccountReorder,
            element => element.Tag is AccountRow, item => item is AccountRow);
    }

    private void OnAccountListUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView list || list.IsLoaded) return;
        if (_accountMotions.Remove(list, out var motion)) motion.Detach();
    }

    private void OnServerDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (_serverMotion is not null) _serverMotion.BeginDrag(args);
        else args.Cancel = true;
    }

    private void OnServerDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        _serverMotion?.EndDrag();

    private void OnAccountDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (sender is ListView list && _accountMotions.TryGetValue(list, out var motion)) motion.BeginDrag(args);
        else args.Cancel = true;
    }

    private void OnAccountDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (sender is ListView list && _accountMotions.TryGetValue(list, out var motion)) motion.EndDrag();
    }

    private void OnServerListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TryKeyboardReorder(e, ServersList, item => item as ServerRow,
            (row, offset) => ViewModel.MoveServer(row, offset)))
            e.Handled = true;
    }

    private void OnAccountListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is ListView list
            && TryKeyboardReorder(e, list, item => item as AccountRow,
                (row, offset) => ViewModel.MoveAccount(row, offset)))
            e.Handled = true;
    }

    private bool TryKeyboardReorder<TRow>(KeyRoutedEventArgs e, ListView list,
        Func<object?, TRow?> asRow, Func<TRow, int, bool> move) where TRow : class
    {
        var offset = e.Key switch
        {
            VirtualKey.Up => -1,
            VirtualKey.Down => 1,
            _ => 0
        };
        if (offset == 0 || (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & CoreVirtualKeyStates.Down) == 0)
            return false;

        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        ListViewItem? container = null;
        for (var node = focused; node is not null && !ReferenceEquals(node, list); node = VisualTreeHelper.GetParent(node))
            if (node is ListViewItem item) { container = item; break; }
        if (container is null) return false;
        var row = asRow(list.ItemFromContainer(container));
        if (row is null || !move(row, offset)) return false;

        DispatcherQueue.TryEnqueue(() =>
        {
            list.UpdateLayout();
            if (list.ContainerFromItem(row) is Control target)
                target.Focus(FocusState.Keyboard);
        });
        return true;
    }

    private void OnShiftButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.IsEnabled) return;
        var siblingName = button.Name switch
        {
            "ServerMoveUpButton" => "ServerMoveDownButton",
            "ServerMoveDownButton" => "ServerMoveUpButton",
            "AccountMoveUpButton" => "AccountMoveDownButton",
            "AccountMoveDownButton" => "AccountMoveUpButton",
            _ => null
        };
        if (siblingName is null || VisualTreeHelper.GetParent(button) is not DependencyObject parent) return;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (VisualTreeHelper.GetChild(parent, index) is Button { Name: var name, IsEnabled: true } sibling
                && name == siblingName)
            {
                sibling.Focus(FocusState.Keyboard);
                return;
            }
    }

    private void DetachMotions()
    {
        _serverMotion?.Detach();
        _serverMotion = null;
        foreach (var motion in _accountMotions.Values) motion.Detach();
        _accountMotions.Clear();
    }

    /// <summary>探针：两层列表的换位弹簧都真的挂上了。见 <see cref="SettingsPriorityMotion"/>。</summary>
    internal bool ServerReorderSpringWired => _serverMotion is not null;

    internal bool AccountReorderSpringWired(ServerRow row) => _accountMotions.Keys.Any(list =>
        ReferenceEquals(list.Tag, row.Accounts) || ReferenceEquals(list.DataContext, row));

    private void OnSettingsSaved(object? sender, EventArgs e)
    {
        var settings = _settings;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (settings is not null && ReferenceEquals(settings, _settings) && _session is not null
                && settings.Settings.Servers.Count != ViewModel.Servers.Count) ViewModel.Refresh();
        });
    }

    private void OnSourcesChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_session is not null) ViewModel.Refresh();
    });

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Release();
        base.OnNavigatedFrom(e);
    }

    /// <summary>
    /// 需求 1：picking one of the servers the network scan found. Not a command, because the item in that
    /// list is a Core record with no way back to the view model; see <see cref="ServersViewModel.OpenDiscovered"/>.
    /// </summary>
    private void OnDiscoveredPicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EmbyDiscoveredServer discovered) ViewModel.OpenDiscovered(discovered);
    }

    /// <summary>
    /// 按实际模板根计数：服务器只有分组根，每张独立账号卡只有一个 AccountRow 标记。
    /// 不从 ItemsSource 读取数量，模板没有实现出来时必须如实报告缺失。
    /// </summary>
    private static void Count(DependencyObject node, ref int servers, ref int accounts)
    {
        // Count the actual template roots, not an ItemsControl's implementation-specific wrapper.
        // A missing template must still read zero; bound view-model counts are not evidence of paint.
        if (node is FrameworkElement { Tag: ServerRow }) servers++;
        else if (node is FrameworkElement { Tag: AccountRow }) accounts++;

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++)
            Count(VisualTreeHelper.GetChild(node, index), ref servers, ref accounts);
    }
}
