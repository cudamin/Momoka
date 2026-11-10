using System.Reflection;
using System.Text.Json;
using Momoka.Emby;
using Momoka.FnNas;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FnNasLibraryDisplayTests
{
    internal static void Register()
    {
        Test("飞牛布局：其他/直播库和目录可选列表，电影电视与未知类别只选海报", () =>
        {
            foreach (var collection in new[] { "homevideos", "livetv" })
                Assert.Equal("Poster,Thumb,List", string.Join(',', Views(collection, false)));
            foreach (var collection in new string?[] { "movies", "tvshows", "mixed", null, "unknown" })
            {
                Assert.Equal("Poster,Thumb", string.Join(',', Views(collection, false)));
                Assert.Equal("Poster,List", string.Join(',', Views(collection, true)));
            }
        });
        Test("飞牛布局：旧横竖海报迁移保留，列表往返恢复最后海报形状", () =>
        {
            foreach (var poster in new[] { LibraryView.Poster, LibraryView.Thumb })
            {
                var original = new LibraryDisplay { View = poster, CardSize = LibraryCardSize.Small };
                var list = Choose(original, LibraryView.List, "homevideos", false);
                Assert.Equal(LibraryView.List, list.View);
                Assert.Equal(poster, PosterPreference(list));
                Assert.Equal(poster, original.View);
                var saved = JsonSerializer.Deserialize<LibraryDisplay>(JsonSerializer.Serialize(list))!;
                Assert.Equal(poster, PosterPreference(saved));
                Assert.Equal(poster, Fit(saved, "movies", false).View);
                Assert.Equal(LibraryView.List, Fit(saved, "homevideos", false).View);
                Assert.Equal(LibraryView.List, saved.View);
            }
        });
        Test("飞牛布局：未知视图不暴露通用样式，非法偏好回退且克隆保留有效偏好", () =>
        {
            var invalid = new LibraryDisplay { View = LibraryView.Table };
            Assert.Equal(LibraryView.Poster, Fit(invalid, "homevideos", false).View);
            var list = Choose(new LibraryDisplay { View = LibraryView.Thumb }, LibraryView.List, "homevideos", false);
            Assert.Equal(LibraryView.Thumb, PosterPreference(list.Clone().Normalized()));
            Assert.Equal(LibraryView.List, Choose(list, LibraryView.Table, "homevideos", false).View);
            Assert.Equal(LibraryView.Poster, Choose(new LibraryDisplay(), LibraryView.List, "movies", false).View);
        });
    }

    private static Type Rules()
    {
        var type = typeof(FnNasLibraryQuery).Assembly.GetType("Momoka.FnNas.FnNasLibraryDisplay");
        Assert.True(type is not null, "缺少飞牛布局条件与偏好恢复规则");
        return type!;
    }

    private static IReadOnlyList<LibraryView> Views(string? collection, bool folder) =>
        (IReadOnlyList<LibraryView>)Rules().GetMethod("Views")!.Invoke(null, [collection, folder])!;

    private static LibraryDisplay Fit(LibraryDisplay display, string? collection, bool folder) =>
        (LibraryDisplay)Rules().GetMethod("Fit")!.Invoke(null, [display, collection, folder])!;

    private static LibraryDisplay Choose(LibraryDisplay display, LibraryView view, string? collection, bool folder) =>
        (LibraryDisplay)Rules().GetMethod("Choose")!.Invoke(null, [display, view, collection, folder])!;

    private static LibraryView? PosterPreference(LibraryDisplay display) =>
        (LibraryView?)typeof(LibraryDisplay).GetProperty("FnNasPosterView", BindingFlags.Public | BindingFlags.Instance)!.GetValue(display);
}
