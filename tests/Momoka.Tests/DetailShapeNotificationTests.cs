using System.ComponentModel;
using System.Reflection;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class DetailActionReadinessTests
{
    private static void RegisterShapeTests(Assembly assembly, string root)
    {
        Test("详情两种模式：开启宽窗固定沉浸，窄窗仍紧凑，关闭一律无背景", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season })
            {
                using var f = new Fixture(assembly, root, shape);
                foreach (var item in f.Transport.Catalog.Values) item.BackdropImageTags.Add("fixture");
                Await(f.Load());
                void Set(string name, object value) => f.Vm.GetType().GetProperty(name)!.SetValue(f.Vm, value);
                void Refresh() => f.Vm.GetType().GetMethod("RefreshAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Vm, null);
                f.Ui.EnableImmersive = true;
                Set("PageWidth", 1200d);
                foreach (var height in new[] { 620d, 1120d, 2000d })
                {
                    Set("Viewport", height);
                    Refresh();
                    Assert.True((bool)f.Get("Showcase")!, "宽窗不再存在自动普通模式");
                }
                Set("PageWidth", 760d);
                Assert.True((bool)f.Get("IsCompact")!);
                Assert.False((bool)f.Get("Showcase")!);
                Assert.True((bool)f.Get("BackdropShown")!, "沉浸启用时保留窄窗背景");
                f.Ui.EnableImmersive = false;
                Refresh();
                Assert.False((bool)f.Get("BackdropShown")!);
                Set("PageWidth", 1200d);
                Assert.True((bool)f.Get("HeroCardShown")!);
                Assert.False((bool)f.Get("BackdropShown")!);
                Assert.False((bool)f.Get("Showcase")!);
            }
        });

        Test("沉浸总开关：电影剧季同页关闭成卡面，宽窄无背景，恢复原偏好", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season })
            {
                using var f = new Fixture(assembly, root, shape);
                foreach (var item in f.Transport.Catalog.Values) item.BackdropImageTags.Add("fixture");
                Await(f.Load());
                void Set(string name, object value) => f.Vm.GetType().GetProperty(name)!.SetValue(f.Vm, value);
                void Refresh() => f.Vm.GetType().GetMethod("RefreshAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Vm, null);
                var enabled = f.Ui.GetType().GetProperty("EnableImmersive");
                Assert.NotNull(enabled, "缺少沉浸模式总开关");
                Set("PageWidth", 1200d);
                Set("Viewport", 2000d);
                f.Ui.UseGaussianBackdrop = true;
                Refresh();
                var target = f.Get("PlayTarget");
                var notices = new List<string>();
                ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
                enabled!.SetValue(f.Ui, false);
                Refresh();
                Assert.False((bool)f.Get("BackdropShown")!);
                Assert.False((bool)f.Get("Showcase")!);
                Assert.False((bool)f.Get("UseGaussianBackdrop")!);
                Assert.True((bool)f.Get("HeroCardShown")!);
                Assert.Equal("Top", f.Get("HeroContentAlignment")!.ToString());
                Assert.Equal(0d, (double)f.Get("TailMinHeight")!);
                foreach (var name in new[] { "HeroCardShown", "HeroCardVisibility", "HeroInset", "TailInset", "TailMargin", "HeroLayoutHeight", "SheetPadding", "ColumnVideoVisibility", "ColumnPickersVisibility" })
                    Assert.True(notices.Contains(name), "同页切换没有通知：" + name);
                Assert.True(ReferenceEquals(target, f.Get("PlayTarget")), "外观切换改变播放目标");
                Set("PageWidth", 760d);
                Assert.False((bool)f.Get("BackdropShown")!);
                Assert.True((bool)f.Get("HeroCardShown")!, "简洁模式窄窗继续使用卡面");
                Assert.False((bool)f.Get("IsCompact")!);
                Set("PageWidth", 1200d);
                Assert.True((bool)f.Get("HeroCardShown")!);
                enabled.SetValue(f.Ui, true);
                Refresh();
                Assert.True(f.Ui.UseGaussianBackdrop);
                Assert.True((bool)f.Get("BackdropShown")!);
                Assert.True((bool)f.Get("Showcase")!);
                Assert.False((bool)f.Get("HeroCardShown")!);
            }
        });

        Test("详情封面：四类页面统一集页尺寸并保留原图比例", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season, Shape.Episode })
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
                f.Vm.GetType().GetField("_stillPixels", Members)!.SetValue(f.Vm, (400, 600));
                f.Vm.GetType().GetMethod("RefitArtwork", Members)!.Invoke(f.Vm, null);
                Assert.Equal(250d, (double)f.Get("StillWidth")!);
                Assert.Equal(375d, (double)f.Get("StillHeight")!);
            }
        });

        Test("简洁模式：跨窄窗断点保留封面、双栏操作并同页恢复紧凑模式", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season })
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                f.Vm.GetType().GetField("_stillPixels", Members)!.SetValue(f.Vm, (400, 600));
                void Refresh() => f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                var notices = new List<string>();
                ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
                foreach (var width in new[] { 600d, 760d, 1023d, 1024d, 1200d })
                {
                    f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, width);
                    f.Ui.EnableImmersive = false;
                    notices.Clear();
                    Refresh();
                    Assert.False((bool)f.Get("IsCompact")!);
                    Assert.True((bool)f.Get("HeroCardShown")!);
                    Assert.Equal(250d, (double)f.Get("StillWidth")!);
                    Assert.Equal(375d, (double)f.Get("StillHeight")!);
                    Assert.Equal("Collapsed", f.Get("CompactVisibility")!.ToString());
                    Assert.Equal("Visible", f.Get("ColumnActionsVisibility")!.ToString());
                    foreach (var name in new[] { "IsCompact", "StillVisibility", "CompactVisibility", "CornerVisibility", "ColumnActionsVisibility", "WideProgressVisibility", "PickersVisibility" })
                        Assert.True(notices.Contains(name), "模式切换缺少通知：" + name);
                    f.Ui.EnableImmersive = true;
                    Refresh();
                    Assert.Equal(width < DetailHero.CompactFloor, (bool)f.Get("IsCompact")!);
                    Assert.Equal(width < DetailHero.CompactFloor ? 0d : 375d, (double)f.Get("StillHeight")!);
                }
            }
        });

        Test("简洁模式：主体更正为单集时重新通知紧凑布局并清除封面占位", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            f.Ui.EnableImmersive = false;
            f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 760d);
            Await(f.Load());
            f.Vm.GetType().GetField("_stillPixels", Members)!.SetValue(f.Vm, (400, 600));
            f.Vm.GetType().GetMethod("RefitArtwork", Members)!.Invoke(f.Vm, null);
            var notices = new List<string>();
            ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
            f.Seed.Type = EmbyItemType.Episode;
            Await(f.Load());
            Assert.True((bool)f.Get("IsCompact")!);
            Assert.Equal(0d, (double)f.Get("StillHeight")!);
            foreach (var name in new[] { "IsCompact", "CompactVisibility", "StillVisibility" })
                Assert.True(notices.Contains(name), "类型更正缺少布局通知：" + name);
        });

        Test("沉浸总开关：单集仍宽窗卡面、窄窗背景", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Episode);
            foreach (var item in f.Transport.Catalog.Values) item.BackdropImageTags.Add("fixture");
            Await(f.Load());
            var enabled = f.Ui.GetType().GetProperty("EnableImmersive");
            Assert.NotNull(enabled);
            enabled!.SetValue(f.Ui, false);
            f.Vm.GetType().GetMethod("RefreshAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Vm, null);
            f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
            Assert.True((bool)f.Get("HeroCardShown")!);
            Assert.False((bool)f.Get("BackdropShown")!);
            f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 760d);
            Assert.False((bool)f.Get("HeroCardShown")!);
            Assert.True((bool)f.Get("BackdropShown")!);
        });

        Test("高斯偏好仅在电影剧季宽窗沉浸生效，窄窗无图及单集保留偏好", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season, Shape.Episode })
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                f.Ui.UseGaussianBackdrop = true;
                void Set(string name, object value) => f.Vm.GetType().GetProperty(name)!.SetValue(f.Vm, value);
                Set("PageWidth", 1200d);
                Set("Viewport", 2000d);
                Set("HeroArt", true);
                f.Ui.EnableImmersive = false;
                Assert.False((bool)f.Get("UseGaussianBackdrop")!, "简洁模式不得启用高斯");
                f.Ui.EnableImmersive = true;
                Assert.Equal(shape != Shape.Episode, (bool)f.Get("UseGaussianBackdrop")!);
                Assert.Equal(shape != Shape.Episode, (bool)f.Get("UseGaussianBackdrop")!);
                Set("PageWidth", 760d);
                Assert.False((bool)f.Get("UseGaussianBackdrop")!, "紧凑版式不得启用高斯");
                Set("PageWidth", 1200d);
                Set("HeroArt", false);
                Assert.False((bool)f.Get("UseGaussianBackdrop")!, "无图不得启用高斯");
                Assert.True(f.Ui.UseGaussianBackdrop, "退出适用模式不能清空偏好");
            }
        });
        foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season })
            Test($"详情 {shape} 简洁模式关闭背景并使用卡面，切回恢复沉浸", () =>
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
                f.Vm.GetType().GetProperty("HeroArt")!.SetValue(f.Vm, true);
                f.Ui.EnableImmersive = false;
                f.Vm.GetType().GetMethod("RefreshAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Vm, null);
                Assert.False((bool)f.Get("BackdropShown")!);
                Assert.False((bool)f.Get("Showcase")!);
                Assert.True((bool)f.Get("HeroCardShown")!);
                f.Ui.EnableImmersive = true;
                f.Vm.GetType().GetProperty("HeroArt")!.SetValue(f.Vm, true);
                Assert.True((bool)f.Get("Showcase")!, "开启后普通宽窗也固定沉浸");
            });
        Test("单集唯一音轨隐藏仍保留视频读数", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Episode);
            var source = f.Transport.Catalog["episode"].MediaSources[0];
            source.MediaStreams = [new() { Type = "Audio", Index = 1, Codec = "aac" }];
            source.Container = "mkv";
            Await(f.Load());
            f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
            Assert.Equal("Collapsed", f.Get("AudioVisibility")!.ToString());
            Assert.Equal("Visible", f.Get("ColumnPickersVisibility")!.ToString());
            Assert.Equal("Visible", f.Get("ColumnVideoVisibility")!.ToString());
        });



        foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season })
            Test($"详情 {shape} 唯一音轨显示文字、增加音轨恢复下拉，宽窄选择框在播放前", () =>
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                var source = f.Transport.Catalog[shape == Shape.Movie ? "movie" : "episode"].MediaSources[0];
                source.MediaStreams = [new() { Type = "Audio", Index = 1, Codec = "aac" }];
                Await(f.Load());
                Assert.Equal("Collapsed", f.Get("AudioVisibility")!.ToString());
                Assert.Equal("Visible", f.Get("AudioRowVisibility")!.ToString());
                Assert.Equal("Visible", f.Get("SingleAudioVisibility")!.ToString());
                Assert.True(!string.IsNullOrWhiteSpace((string)f.Get("SingleAudioText")!));
                source.MediaStreams.Add(new() { Type = "Audio", Index = 2, Codec = "aac" });
                Await(f.Load());
                Assert.Equal("Visible", f.Get("AudioVisibility")!.ToString());
                Assert.Equal("Collapsed", f.Get("SingleAudioVisibility")!.ToString());
                f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
                Assert.Equal("Visible", f.Get("ColumnPickersVisibility")!.ToString());
                Assert.Equal("Collapsed", f.Get("PickersVisibility")!.ToString());
                f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 760d);
                Assert.Equal("Collapsed", f.Get("ColumnPickersVisibility")!.ToString());
                Assert.Equal("Visible", f.Get("PickersVisibility")!.ToString());
            });

        foreach (var shape in new[] { Shape.Movie, Shape.Episode, Shape.Series, Shape.Season })
            Test($"P1 {shape}预览与完整主体同类型只通知一次版式", () =>
            {
                using var f = new Fixture(assembly, root, shape);
                f.Transport.HoldDetail = true;
                var notices = Watch(f, "ContinueVisibility");
                var load = f.Load();
                f.Transport.WaitForDetail();
                Assert.Equal(1, notices.Count, "预览必须先确定版式");
                f.Transport.DetailRelease.TrySetResult(true);
                Await(load);
                Assert.Equal(1, notices.Count, "同类型完整主体不应重复宣布版式");
                Assert.True(f.Get("IsReady") is true);
                Assert.True(f.Can("PlayCommand"));
            });

        Test("P1 同类型刷新保留能力通知与内容更新", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            Await(f.Load());
            var shape = Watch(f, "ContinueVisibility");
            var affordance = Watch(f, "CanOpenGenre");
            f.Transport.Catalog["movie"].Name = "updated anonymous movie";
            f.Transport.Catalog["movie"].MediaSources.Clear();
            Await(f.Load());
            Assert.Equal(0, shape.Count, "同类型刷新不重新宣布版式");
            Assert.True(affordance.Count > 0, "能力仍需重新通知");
            Assert.Equal("updated anonymous movie", (string)f.Get("Title")!);
            Assert.Equal(0, ((System.Collections.ICollection)f.Get("Sources")!).Count);
        });

        Test("P1 存根首次主体仍通知版式", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie, stub: true);
            f.Transport.HoldDetail = true;
            var notices = Watch(f, "ContinueVisibility");
            var load = f.Load();
            f.Transport.WaitForDetail();
            Assert.Equal(0, notices.Count, "存根不能冒充预览");
            f.Transport.DetailRelease.TrySetResult(true);
            Await(load);
            Assert.Equal(1, notices.Count);
            Assert.Equal("Collapsed", f.Get("ContinueVisibility")!.ToString());
        });

        Test("P1 同一预览对象被更正类型仍通知新布局", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            f.Transport.HoldDetail = true;
            var notices = Watch(f, "ContinueVisibility");
            var load = f.Load();
            f.Transport.WaitForDetail();
            Assert.Equal(1, notices.Count);
            // Preview 持有的正是这个可变 DTO，比较 Apply 前后的对象属性会漏掉此更正。
            f.Seed.Type = EmbyItemType.Episode;
            f.Transport.DetailRelease.TrySetResult(true);
            Await(load);
            Assert.Equal(2, notices.Count);
            Assert.Equal("Visible", f.Get("ContinueVisibility")!.ToString());
        });

        Test("P1 同类型重新Attach仍重建本访问版式", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            Await(f.Load());
            f.Reattach(f.Seed);
            var notices = Watch(f, "ContinueVisibility");
            Await(f.Load());
            Assert.Equal(1, notices.Count, "旧访问的通知不能代表新访问已就绪");
            Assert.True(f.Can("PlayCommand"));
        });

        Test("P1 版式通知中断后同类型重试必须补齐", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            PropertyChangedEventHandler fail = (_, args) =>
            {
                if (args.PropertyName == "HeroHeight") throw new InvalidOperationException("anonymous notification failure");
            };
            var observable = (INotifyPropertyChanged)f.Vm;
            observable.PropertyChanged += fail;
            Await(f.Load());
            observable.PropertyChanged -= fail;
            Assert.True(f.Get("NoticeOpen") is true);
            var notices = Watch(f, "ContinueVisibility");
            Await(f.Load());
            Assert.Equal(1, notices.Count, "失败的半批通知不能标记为已宣布");
            Assert.True(f.Can("PlayCommand"));
        });

        Test("P1 空类型主体不会命中未初始化的版式记录", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie, stub: true);
            f.Seed.Type = null!;
            var notices = Watch(f, "ContinueVisibility");
            Await(f.Load());
            Assert.Equal(1, notices.Count);
        });

        Test("P1 更正类型通知中断后恢复旧类型也要补齐版式", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            Await(f.Load());
            f.Seed.Type = EmbyItemType.Episode;
            PropertyChangedEventHandler fail = (_, args) =>
            {
                if (args.PropertyName == "HeroHeight") throw new InvalidOperationException("anonymous type correction failure");
            };
            var observable = (INotifyPropertyChanged)f.Vm;
            observable.PropertyChanged += fail;
            Await(f.Load());
            observable.PropertyChanged -= fail;
            Assert.True(f.Get("NoticeOpen") is true);
            f.Seed.Type = EmbyItemType.Movie;
            var notices = Watch(f, "ContinueVisibility");
            Await(f.Load());
            Assert.Equal(1, notices.Count, "中途失败后即使恢复旧类型，也不能复用上一批记录");
            Assert.True(f.Can("PlayCommand"));
        });

        Test("P1 同类型剧集刷新仍通知清空与重填集带", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Series);
            Await(f.Load());
            var shape = Watch(f, "ContinueVisibility");
            var episodes = Watch(f, "EpisodeStripVisibility");
            f.Transport.Episodes.Clear();
            Await(f.Load());
            Assert.Equal(0, shape.Count);
            Assert.True(episodes.Count > 0);
            Assert.Equal("Collapsed", f.Get("EpisodeStripVisibility")!.ToString());
            Assert.False(f.Can("PlayCommand"));
        });
    }

    private static List<string?> Watch(Fixture fixture, string property)
    {
        var notices = new List<string?>();
        ((INotifyPropertyChanged)fixture.Vm).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == property) notices.Add(args.PropertyName);
        };
        return notices;
    }
}
