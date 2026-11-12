using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.Views;

internal sealed class CardItem(EmbyItem item)
{
    internal EmbyItem Item { get; } = item;
    // Matches production CardItem's managed-only online-scope check.
    internal bool IsCurrent => Item.Source?.IsAvailable == true;
}

internal interface IShellActions
{
    ItemMenuPermissions MenuPermissionsFor(EmbySessionScope scope);
    void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational);
}

internal sealed class CallerActions(Func<ItemMenuPermissions> permissions) : IShellActions
{
    public ItemMenuPermissions MenuPermissionsFor(EmbySessionScope scope) => permissions();
    public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) { }
}

internal static partial class ItemCommands
{
    internal static Task FixtureFnNasSubtitles(EmbySessionScope scope, IShellActions shell, FrameworkElement owner,
        CardItem card, Action? changed = null) => FnNasSubtitlesAsync(scope, shell, owner, card, changed);
}
