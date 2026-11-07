using Microsoft.UI.Xaml;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.Shell.Views;

// Event delivery is a terminal presentation boundary; the production handler and Cancel execute.
internal sealed class CardItem(EmbyItem item)
{
    internal EmbyItem Item { get; } = item;
    internal bool Watched => Item.IsWatched;
    internal bool IsFavorite => Item.UserData?.IsFavorite == true;
    internal event PropertyChangedEventHandler? PropertyChanged;
    internal int Subscribers => PropertyChanged?.GetInvocationList().Length ?? 0;
    internal void Change(string property) => PropertyChanged?.Invoke(this, new(property));
}
internal interface IShellActions
{
    void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational);
    ItemMenuPermissions MenuPermissionsFor(EmbySessionScope scope);
}
internal static partial class ItemCommands
{
    private const string Category = "ui";
    internal static Task FixtureSubtitles(EmbySessionScope scope, IShellActions actions,
        FrameworkElement owner, EmbyItem item, Action changed) =>
        SubtitlesAsync(scope, actions, owner, new CardItem(item), changed);
}
