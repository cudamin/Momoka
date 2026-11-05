using Momoka.Emby;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FilterValuesTests
{
    internal static void Register()
    {
        Test("筛选全选：只清当前字段、刷新复选且仅通知一次", () =>
        {
            var selection = new ItemFilters();
            selection.Years.AddRange(["2024", "1999"]);
            selection.Genres.Add("动画");
            var owner = new FilterPanelViewModel();
            var changes = 0;
            owner.Changed += (_, _) => changes++;
            owner.Open(selection, "mixed", new Version(4, 10, 1),
                (_, _) => Task.FromResult<IReadOnlyList<string>>(["2026", "2024"]));
            var row = owner.Rows.OfType<FilterValuesRow>().Single(value => value.Field.ListKey == EmbyFilterBy.Keys.Years);
            row.IsOpen = true;
            Assert.Equal("2026,2024,1999", string.Join(',', row.Values.Select(value => value.Label)));
            Assert.Equal(2, row.Values.Count(value => value.IsChecked));
            row.SelectAll();
            Assert.Equal(0, selection.Years.Count);
            Assert.Equal("动画", selection.Genres.Single());
            Assert.True(row.Values.All(value => !value.IsChecked));
            Assert.Equal("全部", row.Summary);
            Assert.Equal(1, changes);
            row.SelectAll();
            Assert.Equal(1, changes, "不限时全选不能重复查询");
            row.Values[0].IsChecked = true;
            Assert.Equal("2026", selection.Years.Single());
            Assert.Equal(2, changes);
        });
        Test("筛选展开：缓存重开保留已选值且不重复读取", () =>
        {
            var selection = new ItemFilters();
            var owner = new FilterPanelViewModel();
            var reads = 0;
            owner.Open(selection, "mixed", new Version(4, 10, 1), (_, _) =>
            {
                reads++;
                return Task.FromResult<IReadOnlyList<string>>(["2026", "2025"]);
            });
            var row = owner.Rows.OfType<FilterValuesRow>().Single(value => value.Field.ListKey == EmbyFilterBy.Keys.Years);
            row.IsOpen = true;
            row.Values[1].IsChecked = true;
            row.IsOpen = false;
            row.IsOpen = true;
            Assert.Equal(1, reads);
            Assert.Equal("2025", selection.Years.Single());
            Assert.True(row.Values[1].IsChecked);
        });
        Test("筛选读取失败：仍能清除旧年份且重开可以重试", () =>
        {
            var selection = new ItemFilters();
            selection.Years.Add("1999");
            var owner = new FilterPanelViewModel();
            var reads = 0;
            owner.Open(selection, "mixed", new Version(4, 10, 1), (_, _) =>
                ++reads == 1 ? Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("离线失败"))
                    : Task.FromResult<IReadOnlyList<string>>(["2026"]));
            var row = owner.Rows.OfType<FilterValuesRow>().Single(value => value.Field.ListKey == EmbyFilterBy.Keys.Years);
            row.IsOpen = true;
            Assert.Equal(0, row.Values.Count);
            Assert.True(row.Note.Length > 0);
            row.SelectAll();
            Assert.Equal(0, selection.Years.Count);
            row.IsOpen = false;
            row.IsOpen = true;
            Assert.Equal(2, reads);
            Assert.Equal("2026", row.Values.Single().Label);
            Assert.Equal("", row.Note);
        });
    }

}
