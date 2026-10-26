using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

internal sealed class CardItem(EmbyItem item) { internal EmbyItem Item { get; } = item; }
internal interface IShellActions { void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational); }
internal static partial class ItemCommands
{
    private const string Category = "ui";
    internal static Task FixtureSubtitles(EmbySessionScope scope, IShellActions actions,
        FrameworkElement owner, EmbyItem item, Action changed) =>
        SubtitlesAsync(scope, actions, owner, new CardItem(item), changed);
}
