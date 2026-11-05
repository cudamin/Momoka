#nullable enable
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
namespace Momoka.Shell.Views;

internal static partial class ItemCommands
{
    private const string Category = "review";
    internal static Task Run(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action changed) => EditAsync(scope, shell, owner, card, changed);
    private static Task FnNasEditAsync(EmbySessionScope scope, IShellActions shell, FrameworkElement owner, CardItem card, Action? changed) => throw new Exception("FnNas must not run");
    private static XamlRoot? Root(FrameworkElement owner) => owner.XamlRoot;

}
