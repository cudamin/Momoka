using System.ComponentModel;
using System.Reflection;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class DetailActionReadinessTests
{
    private static void RegisterCornerLimitTests(Assembly assembly, string root)
    {
        Test("艺术图通知：页宽跨紧凑断点后宽高绑定与头部高度同步", () =>
        {
            using var f = CornerFixture(assembly, root);
            foreach (var width in new[] { 820d, 819d, 1280d })
                CheckCornerChange(f, () => SetCornerValue(f, "PageWidth", width), "页宽 " + width);
        });
        Test("艺术图通知：整窗未测量时内容宽变化同步宽高与头部高度", () =>
        {
            using var f = CornerFixture(assembly, root);
            SetCornerValue(f, "FullWindowWidth", 0d);
            SetCornerValue(f, "WindowWidth", 2560d);
            CheckCornerChange(f, () => SetCornerValue(f, "WindowWidth", 1280d), "窗口宽回退");
            Assert.Equal(360d, (double)f.Get("CornerMaxWidth")!);
        });
        Test("艺术图通知：整窗还原时末次留白变化不会留下过渡窄盒", () =>
        {
            using var f = CornerFixture(assembly, root);
            SetCornerValue(f, "FullWindowWidth", 2560d);
            Assert.True((double)f.Get("CornerMaxHeight")! < 20, "先建立旧整窗留白挤出的过渡小盒");
            CheckCornerChange(f, () => SetCornerValue(f, "FullWindowWidth", 1280d), "整窗还原");
            Assert.Equal(360d, (double)f.Get("CornerMaxWidth")!);
            Assert.Equal(202.5d, (double)f.Get("CornerMaxHeight")!);
        });
        Test("艺术图通知：封面单独变宽同步宽高上限与头部高度", () =>
        {
            using var f = CornerFixture(assembly, root);
            SetCornerValue(f, "PageWidth", 1040d);
            CheckCornerChange(f, () => SetCornerValue(f, "StillWidth", 400d), "封面宽变化");
            Assert.True((double)f.Get("CornerMaxWidth")! < 360);
        });
        Test("艺术图通知：倍率变更同步上限，倍率不变的最大化还原仍同步", () =>
        {
            using var f = CornerFixture(assembly, root);
            void Scale(double width) => f.Vm.GetType().GetMethod("SetCornerViewport", Members)!
                .Invoke(f.Vm, [width, 2560d]);
            CheckCornerChange(f, () => Scale(2560d), "最大倍率");
            Assert.Equal(540d, (double)f.Get("CornerMaxWidth")!);
            foreach (var width in new[] { 2560d, 1280d })
            {
                // 与真实 OnBodySizeChanged 顺序相同；每次 setter 返回后分别核两个绑定缓存。
                foreach (var name in new[] { "PageWidth", "WindowWidth", "FullWindowWidth" })
                    CheckCornerChange(f, () => SetCornerValue(f, name, width), name + " → " + width,
                        requireNotice: (double)f.Get(name)! != width);
                Scale(2560d); // 倍率没变时方法早退，不能替前面窗口宽输入补通知。
                CheckCornerValues(f, (double)f.Get("CornerMaxWidth")!, (double)f.Get("CornerMaxHeight")!, "恒定倍率");
            }
            CheckCornerChange(f, () => Scale(1280d), "还原倍率");
            Assert.Equal(360d, (double)f.Get("CornerMaxWidth")!);
        });
        Test("艺术图通知：背景与商店模式切换仍成对通知并重算高度", () =>
        {
            using var f = CornerFixture(assembly, root);
            SetCornerValue(f, "FullWindowWidth", 1600d);
            foreach (var art in new[] { false, true })
                CheckCornerChange(f, () => SetCornerValue(f, "HeroArt", art), "背景 " + art);
            foreach (var immersive in new[] { false, true })
                CheckCornerChange(f, () =>
                {
                    f.Ui.EnableImmersive = immersive;
                    f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                }, "沉浸 " + immersive);
        });
    }

    private static Fixture CornerFixture(Assembly assembly, string root)
    {
        var fixture = new Fixture(assembly, root, Shape.Episode);
        foreach (var item in fixture.Transport.Catalog.Values) item.BackdropImageTags.Add("fixture");
        Await(fixture.Load());
        foreach (var name in new[] { "PageWidth", "WindowWidth", "FullWindowWidth" })
            SetCornerValue(fixture, name, 1280d);
        fixture.Vm.GetType().GetField("_cornerPixels", Members)!.SetValue(fixture.Vm, (1600, 900));
        fixture.Vm.GetType().GetMethod("FitCorner", Members)!.Invoke(fixture.Vm, null);
        return fixture;
    }

    private static void SetCornerValue(Fixture fixture, string name, object value) =>
        fixture.Vm.GetType().GetProperty(name, Members)!.SetValue(fixture.Vm, value);

    private static void CheckCornerChange(Fixture fixture, Action change, string step, bool requireNotice = true)
    {
        // 模拟两个 OneWay 绑定只在自己的通知到来时取值；只读 getter 会漏掉本次原生探针发现的陈旧上限。
        var width = (double)fixture.Get("CornerMaxWidth")!;
        var height = (double)fixture.Get("CornerMaxHeight")!;
        var notices = new List<string>();
        PropertyChangedEventHandler handler = (_, args) =>
        {
            var name = args.PropertyName ?? "";
            notices.Add(name);
            if (name == "CornerMaxWidth") width = (double)fixture.Get(name)!;
            if (name == "CornerMaxHeight") height = (double)fixture.Get(name)!;
        };
        var observable = (INotifyPropertyChanged)fixture.Vm;
        observable.PropertyChanged += handler;
        try
        {
            change();
            if (requireNotice)
                foreach (var name in new[] { "CornerMaxWidth", "CornerMaxHeight" })
                    Assert.True(notices.Contains(name), step + " 缺少通知 " + name);
            CheckCornerValues(fixture, width, height, step);
        }
        finally { observable.PropertyChanged -= handler; }
    }

    private static void CheckCornerValues(Fixture fixture, double boundWidth, double boundHeight, string step)
    {
        var width = (double)fixture.Get("CornerMaxWidth")!;
        var height = (double)fixture.Get("CornerMaxHeight")!;
        Assert.Equal(width, boundWidth, step + " 宽上限绑定停在旧值");
        Assert.Equal(height, boundHeight, step + " 高上限绑定停在旧值");
        Assert.Equal(width * DetailHero.CornerHeight / DetailHero.CornerWidth, height, step + " 上限比例");
        var expected = DetailHero.StillBox(1600, 900, width, height)?.Height ?? 0;
        Assert.Equal(expected, (double)fixture.Get("CornerHeight")!, step + " 头部仍使用过渡艺术图高度");
    }
}
