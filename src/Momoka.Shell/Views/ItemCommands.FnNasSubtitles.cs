using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.FnNas;

namespace Momoka.Shell.Views;

internal static partial class ItemCommands
{
    private static async Task FnNasSubtitlesAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner,
        CardItem card, Action? changed)
    {
        if (Root(owner) is not { } root || !card.IsCurrent) return;
        var original = card.Item;
        var id = original.Id;
        var revision = scope.PermissionRevision;
        var revoked = 0;
        var dispatcher = owner.DispatcherQueue;
        FnNasSubtitleDialog? dialog = null;
        // Core invokes this predicate after ConfigureAwait(false). Keep it entirely managed;
        // XAML ownership is sampled only by UI events and the UI-facing predicate below.
        bool Current() => Volatile.Read(ref revoked) == 0 && scope.IsAvailable && card.IsCurrent
            && ReferenceEquals(original, card.Item) && card.Item.Id == id;
        bool CurrentUi()
        {
            if (Root(owner) != root) Interlocked.Exchange(ref revoked, 1);
            return Current();
        }
        // Opening a ContentDialog reparents its visual; an item container can also be recycled.
        // A real page departure detaches the owning Page, which is the visit this operation captures.
        FrameworkElement visit = owner;
        for (var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(owner); parent is not null;
             parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
            if (parent is Page page) { visit = page; break; }
        void EndVisit(object? sender, RoutedEventArgs args)
        {
            Interlocked.Exchange(ref revoked, 1);
            dispatcher.TryEnqueue(() => dialog?.RefreshCapabilities());
        }
        void SourcesChanged(object? sender, EventArgs args)
        {
            if (!scope.IsAvailable) Interlocked.Exchange(ref revoked, 1);
            dispatcher.TryEnqueue(() => dialog?.RefreshCapabilities());
        }
        void PermissionsChanged(object? sender, EmbySessionScope changedScope)
        {
            if (changedScope.UserStateOwner != scope.UserStateOwner) return;
            dispatcher.TryEnqueue(() => dialog?.RefreshCapabilities());
        }
        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            _ = CurrentUi();
            dialog?.RefreshCapabilities();
        }
        visit.Unloaded += EndVisit;
        root.Changed += RootChanged;
        scope.Session.SourcesChanged += SourcesChanged;
        scope.Session.PermissionsChanged += PermissionsChanged;
        try
        {
            RequirePermission(shell, scope, ItemCommand.Subtitles);
            var full = await AskAsync(scope, (client, token) => client.GetItemAsync(id, token, EmbyFields.Files)).ConfigureAwait(true);
            if (!CurrentUi() || scope.PermissionRevision != revision) return;
            if (full.Id != id) throw new InvalidOperationException("服务器返回了不同媒体条目，请重新打开详情");
            var sources = full.MediaSources.Where(source => !string.IsNullOrWhiteSpace(source.Id) && !string.IsNullOrWhiteSpace(source.Path))
                .Where(source => full.MediaSources.Count(other => other.Id == source.Id) == 1).ToArray();
            if (sources.Length == 0)
            {
                shell.Notify("服务器没有返回完整的媒体版本，无法管理字幕", InfoBarSeverity.Warning);
                return;
            }
            var services = sources.Select(source => new FnNasSubtitles(scope, full, source,
                () => shell.MenuPermissionsFor(scope), Current)).ToArray();
            dialog = new FnNasSubtitleDialog(full.Name, services, CurrentUi) { XamlRoot = root };
            await dialog.ShowAsync();
            // Debt belongs to the entire visit, including a previous selected version and an unknown receipt.
            // The page callback is also captured by the existing caller's source/subject/visit guards.
            if ((dialog.Touched || dialog.NeedsRefresh) && CurrentUi()) changed?.Invoke();
        }
        finally
        {
            visit.Unloaded -= EndVisit;
            root.Changed -= RootChanged;
            scope.Session.SourcesChanged -= SourcesChanged;
            scope.Session.PermissionsChanged -= PermissionsChanged;
        }
    }
}
