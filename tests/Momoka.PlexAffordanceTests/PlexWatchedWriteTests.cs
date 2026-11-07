using System.Net;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static partial class PlexAffordanceTests
{
    private static void RegisterWatchedWrites()
    {
        foreach (var played in new[] { true, false })
            foreach (var status in new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.Forbidden })
            {
                Case($"R05-002 真Plex卡片确认{played}后补读{(int)status}保留状态", async () =>
                {
                    using var f = New();
                    var card = f.Card(f.Item("plex", played: !played));
                    f.Transport.FailDetailRead = true;
                    f.Transport.DetailFailureStatus = status;
                    ItemCommands.Run(played ? ItemCommand.MarkPlayed : ItemCommand.MarkUnplayed,
                        f.Session, f.SourceActions, new FrameworkElement(), card);
                    await AffordanceContext.Until(() => PlexAffordanceFixture.Pending(card) == 0);
                    Assert.Equal(played, card.Watched);
                    Assert.Equal(1, f.Transport.Writes);
                    Assert.Equal(played ? 1 : 0, f.Actions.WatchedReports.Count);
                    Assert.Contains("已保存", f.Actions.Messages.Single());
                    Assert.Contains("刷新失败", f.Actions.Messages.Single());
                    Assert.Equal(status == HttpStatusCode.ServiceUnavailable ? "Warning" : "Error", f.Actions.Severities.Single());
                });
                Case($"R05-002 真Plex详情确认{played}后补读{(int)status}保留状态", async () =>
                {
                    using var f = New();
                    var actions = _shell.Actions();
                    var vm = await Detail(f, "plex", EmbyItemType.Movie, actions);
                    try
                    {
                        // Establish the opposite initial state through the actual successful command.
                        if (!played) await (Task)ShellAssembly.Invoke(vm, "ToggleWatchedAsync")!;
                        Assert.True(ShellAssembly.Get<ICommand>(vm, "ToggleWatchedCommand").CanExecute(null));
                        var before = f.Transport.Writes;
                        actions.Messages.Clear();
                        actions.Severities.Clear();
                        f.Transport.FailDetailRead = true;
                        f.Transport.DetailFailureStatus = status;
                        await (Task)ShellAssembly.Invoke(vm, "ToggleWatchedAsync")!;
                        Assert.Equal(played, ShellAssembly.Get<bool>(vm, "Watched"));
                        Assert.Equal(before + 1, f.Transport.Writes);
                        Assert.Contains("已保存", actions.Messages.Single());
                        Assert.Contains("刷新详情失败", actions.Messages.Single());
                        Assert.Equal(status == HttpStatusCode.ServiceUnavailable ? "Warning" : "Error", actions.Severities.Single());
                    }
                    finally { ((IDisposable)vm).Dispose(); }
                });
            }
        Case("R05-002 真Plex卡片写失败没有确认或补读", async () =>
        {
            using var f = New();
            var card = f.Card(f.Item("plex"));
            f.Transport.FailWrites = true;
            ItemCommands.ToggleWatched(f.Session, f.SourceActions, card);
            await AffordanceContext.Until(() => PlexAffordanceFixture.Pending(card) == 0);
            Assert.False(card.Watched);
            Assert.Equal(1, f.Transport.Writes);
            Assert.Equal(0, f.Actions.WatchedReports.Count);
            Assert.Equal(0, f.Transport.Requests.Count(request => request.Path == "/library/metadata/item"));
            Assert.Equal("Error", f.Actions.Severities.Single());
        });
        foreach (var played in new[] { true, false })
            Case($"R05-002 真Plex卡片独立补读过期位不能推翻{played}确认", async () =>
            {
                using var f = New();
                var card = f.Card(f.Item("plex", played: !played));
                f.Transport.StaleDetailRead = true;
                ItemCommands.ToggleWatched(f.Session, f.SourceActions, card);
                await AffordanceContext.Until(() => PlexAffordanceFixture.Pending(card) == 0);
                Assert.Equal(played, card.Watched);
                if (!played) Assert.Equal(0L, card.Item.UserData!.PlaybackPositionTicks);
                Assert.Equal(1, f.Transport.Writes);
                Assert.Equal(0, f.Actions.Messages.Count);
            });
        Case("R05-002 真Plex卡片确认后来源撤销拒绝补读", async () =>
        {
            using var f = New();
            var card = f.Card(f.Item("plex"));
            f.Transport.StaleDetailRead = true;
            f.Transport.BeforeDetailRead = () => f.Revoke("plex");
            ItemCommands.ToggleWatched(f.Session, f.SourceActions, card);
            await AffordanceContext.Until(() => PlexAffordanceFixture.Pending(card) == 0);
            Assert.True(card.Watched);
            Assert.Equal(1, f.Transport.Writes);
            Assert.Equal(0, f.Actions.Messages.Count);
        });
        Case("R05-002 真Plex详情补读期间取消并重入不回填旧位或提示", async () =>
        {
            using var f = New();
            var actions = _shell.Actions();
            var vm = await Detail(f, "plex", EmbyItemType.Movie, actions);
            try
            {
                f.Transport.BeforeDetailRead = () => ShellAssembly.Invoke(vm, "Cancel");
                f.Transport.FailDetailRead = true;
                await (Task)ShellAssembly.Invoke(vm, "ToggleWatchedAsync")!;
                f.Transport.FailDetailRead = false;
                vm = await Detail(f, "emby", EmbyItemType.Movie, actions, vm);
                Assert.Equal(1, f.Transport.Writes);
                Assert.Equal(0, actions.Messages.Count);
                Assert.False(ShellAssembly.Get<bool>(vm, "Watched"));
                Assert.True(ShellAssembly.Get<ICommand>(vm, "ToggleWatchedCommand").CanExecute(null), "新访问主体已独立读取，旧补读不能改它");
            }
            finally { ((IDisposable)vm).Dispose(); }
        });
    }
}
