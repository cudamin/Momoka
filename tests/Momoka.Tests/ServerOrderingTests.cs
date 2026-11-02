using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>服务器页排序：服务器组与账号共用同一套纯规则。</summary>
internal static class ServerOrderingTests
{
    private sealed record Item(string Name, int Rank);

    public static void Register()
    {
        Test("服务器排序：字符串往返，认不出的值落回手动", () =>
        {
            foreach (var mode in Enum.GetValues<ServerSortMode>())
                Assert.Equal(mode, ServerListOrder.Parse(ServerListOrder.Serialize(mode)));
            Assert.Equal(ServerSortMode.Manual, ServerListOrder.Parse("name-ish"));
            Assert.Equal(ServerSortMode.Manual, ServerListOrder.Parse(null));
            Assert.Equal("manual", ServerListOrder.Serialize(ServerSortMode.Manual));
            Assert.Equal("name", ServerListOrder.Serialize(ServerSortMode.Name));
            Assert.Equal("status", ServerListOrder.Serialize(ServerSortMode.Status));
        });

        Test("服务器排序：手动模式保留调用方顺序，升降序都不动", () =>
        {
            Item[] items = [new("B", 2), new("A", 0), new("C", 1)];
            foreach (var descending in new[] { false, true })
            {
                var ordered = ServerListOrder.Apply(items, ServerSortMode.Manual, descending,
                    item => item.Name, item => item.Rank);
                Assert.Equal("B,A,C", string.Join(',', ordered.Select(item => item.Name)));
            }
        });

        Test("服务器排序：名称升序/降序，同优先级保持原顺序", () =>
        {
            Item[] items = [new("beta", 0), new("Alpha", 1), new("alpha", 2), new("Gamma", 3)];

            var ascending = ServerListOrder.Apply(items, ServerSortMode.Name, false,
                item => item.Name, item => item.Rank, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("Alpha,alpha,beta,Gamma", string.Join(',', ascending.Select(item => item.Name)));

            var descending = ServerListOrder.Apply(items, ServerSortMode.Name, true,
                item => item.Name, item => item.Rank, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("Gamma,beta,Alpha,alpha", string.Join(',', descending.Select(item => item.Name)));
        });

        Test("服务器排序：状态分组在线在前，同组按名称；降序翻组序", () =>
        {
            Item[] items = [new("离线乙", 1), new("在线甲", 0), new("离线甲", 1), new("在线乙", 0)];

            var ascending = ServerListOrder.Apply(items, ServerSortMode.Status, false,
                item => item.Name, item => item.Rank, StringComparer.Ordinal);
            Assert.Equal("在线乙,在线甲,离线乙,离线甲", string.Join(',', ascending.Select(item => item.Name)));

            var descending = ServerListOrder.Apply(items, ServerSortMode.Status, true,
                item => item.Name, item => item.Rank, StringComparer.Ordinal);
            Assert.Equal("离线乙,离线甲,在线乙,在线甲", string.Join(',', descending.Select(item => item.Name)));
        });

        Test("服务器排序：不修改输入集合", () =>
        {
            Item[] items = [new("B", 1), new("A", 0)];
            _ = ServerListOrder.Apply(items, ServerSortMode.Name, false,
                item => item.Name, item => item.Rank, StringComparer.Ordinal);
            Assert.Equal("B,A", string.Join(',', items.Select(item => item.Name)));
        });
    }
}
