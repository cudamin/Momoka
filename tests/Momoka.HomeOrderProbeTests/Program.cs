using System.Collections;
using System.Text;
using System.Windows.Input;
using Momoka.Emby;
using Momoka.Shell;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("用法：Momoka.HomeOrderProbeTests <本树当前 Release Momoka.dll> <本树根目录> [legacy-red]");
            return 2;
        }
        try
        {
            _ = new ShellRuntime(args[0], args[1]);
            Console.WriteLine("HomeOrder：真实 HomeSections/Rows/MP 可用性＋源链接页面/共享探针入口；无UI/登录/凭据/网络");
            if (args.Length == 3)
            {
                Assert.Equal("legacy-red", args[2]);
                Test("旧探针直接 WatchMoviePilot 后等待两个 MP 项（应复现 Timeout）", async () =>
                {
                    using var f = new HomeOrderFixture();
                    f.Page.WatchMoviePilot(f.Service);
                    Assert.True(await f.Connect());
                    f.Drain();
                    Console.WriteLine($"  旧夹具证据：Available={f.Service.Available}；Rows={f.Rows.Count}；MP={f.MoviePilotCount}；队列={f.Page.DispatcherQueue.Count}");
                    await ShellNavigationProbe.UntilForFixture(() => f.MoviePilotCount == 2);
                });
            }
            else Register(args[1]);
            var failed = 0;
            foreach (var (name, run) in Cases)
            {
                try { await run(); Console.WriteLine("  通过  " + name); }
                catch (Exception error) { failed++; Console.WriteLine("  失败  " + name + "\n" + error); }
            }
            Console.WriteLine($"共 {Cases.Count} 项，通过 {Cases.Count - failed}，失败 {failed}，跳过 0");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Test(string name, Func<Task> action) => Cases.Add((name, action));
    private static void Test(string name, Action action) => Test(name, () => { action(); return Task.CompletedTask; });

    private static void Register(string root)
    {
        foreach (var release in new[] { "ReleaseHosted", "Release" })
            Test($"HomeOrder {release}取消两卡真实颜色草稿而保留主页表", () =>
            {
                using var f = new HomeOrderFixture();
                var colors = new AppearanceColorFixture(f.Vm);
                if (release == "ReleaseHosted") f.Page.ReleaseHosted();
                else f.Page.Release();
                colors.AssertCancelled();
                f.Page.ReleaseHosted();
                colors.AssertCancelled();
                f.Stable();
            });
        Test("HomeOrder 真实在线来源保留媒体库行，撤销来源只隐藏该行且偏好不丢", () =>
        {
            using var f = new HomeOrderFixture();
            Assert.True(f.Source.IsCurrent && f.Source.IsAvailable);
            Assert.Equal(1, f.Capabilities.Sources.Count);
            Assert.Equal(4, f.Rows.Count);
            Assert.True(f.Keys.Any(key => key.Contains(Uri.EscapeDataString(f.Source.Key), StringComparison.Ordinal)));
            Revoke(f.Source);
            HomeOrderFixture.Runtime.Invoke(f.Vm, "RefreshHomeRows");
            Assert.False(f.Source.IsAvailable);
            Assert.Equal(3, f.Rows.Count);
            f.Stable();
        });
        Test("HomeOrder 原case断言、截图和轮播阈值全部保留（源码契约，非像素）", () => CoverageContract(root));
        Test("HomeOrder 实际CategoryGroups通知在活动期换绑定源，主页表与选择保持", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            var initial = f.Page.CategoryGroupsSource.Source;
            var writes = f.Page.CategoryGroupsSource.Writes;
            Assert.True(ReferenceEquals(initial, HomeOrderFixture.Runtime.Get<object>(f.Vm, "CategoryGroups")));
            // 相同目录现在按设计复用（见 SettingsViewModel.ReplaceCategories）；要验证通知与重绑，必须真的换一档目录：
            // 管理员那档会多出「通知」一行。
            HomeOrderFixture.Runtime.Invoke(f.Vm, "ApplyServerAdministration", true, MediaServerKind.Emby);
            var current = HomeOrderFixture.Runtime.Get<object>(f.Vm, "CategoryGroups");
            Assert.False(ReferenceEquals(initial, current), "必须是实际VM重新构造的分组，不是假通知");
            Assert.True(ReferenceEquals(current, f.Page.CategoryGroupsSource.Source), "必须执行真正UpdateCategoryGroups而非空stub");
            Assert.Equal(writes + 1, f.Page.CategoryGroupsSource.Writes);
            Assert.Equal("主页", HomeOrderFixture.Runtime.Get<string>(f.Vm, "SelectedCategory"));
            f.Page.UpdateCategoryGroupsForFixture();
            Assert.Equal(writes + 1, f.Page.CategoryGroupsSource.Writes, "相同分组源不可重复写绑定");
            f.Stable();
        });
        Test("HomeOrder 未活动新分组通知不能借构造器绑定借权", () =>
        {
            using var f = new HomeOrderFixture();
            var initial = f.Page.CategoryGroupsSource.Source;
            var writes = f.Page.CategoryGroupsSource.Writes;
            // 目录真的变了才会通知；未变化的目录按设计复用。
            HomeOrderFixture.Runtime.Invoke(f.Vm, "ApplyServerAdministration", true, MediaServerKind.Emby);
            Assert.False(ReferenceEquals(initial, HomeOrderFixture.Runtime.Get<object>(f.Vm, "CategoryGroups")));
            Assert.True(ReferenceEquals(initial, f.Page.CategoryGroupsSource.Source));
            Assert.Equal(writes, f.Page.CategoryGroupsSource.Writes);
            f.Stable();
        });
        Test("HomeOrder Release后分组通知失效，新活动期收到新表才重绑", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            // 用管理员档与普通档来回切出真实的目录变化；相同目录按设计复用，不再发通知。
            HomeOrderFixture.Runtime.Invoke(f.Vm, "ApplyServerAdministration", true, MediaServerKind.Emby);
            var active = f.Page.CategoryGroupsSource.Source;
            var writes = f.Page.CategoryGroupsSource.Writes;
            f.Page.Release();
            HomeOrderFixture.Runtime.Invoke(f.Vm, "ApplyServerAdministration", false, MediaServerKind.Emby);
            Assert.True(ReferenceEquals(active, f.Page.CategoryGroupsSource.Source));
            Assert.Equal(writes, f.Page.CategoryGroupsSource.Writes);
            await f.Open();
            HomeOrderFixture.Runtime.Invoke(f.Vm, "ApplyServerAdministration", true, MediaServerKind.Emby);
            Assert.True(ReferenceEquals(HomeOrderFixture.Runtime.Get<object>(f.Vm, "CategoryGroups"), f.Page.CategoryGroupsSource.Source));
            Assert.Equal(writes + 1, f.Page.CategoryGroupsSource.Writes);
            f.Stable();
        });
        Test("HomeOrder 真分组绑定赋值引起选择写回时立即保住原主页选择", () =>
        {
            using var f = new HomeOrderFixture();
            f.Page.CategoryGroupsSource.Source = null;
            var writes = f.Page.CategoryGroupsSource.Writes;
            f.Page.CategoryGroupsSource.SourceAssigned = () => HomeOrderFixture.Runtime.Set(f.Vm, "SelectedCategory", "视频");
            f.Page.UpdateCategoryGroupsForFixture();
            Assert.Equal(writes + 1, f.Page.CategoryGroupsSource.Writes);
            Assert.True(ReferenceEquals(HomeOrderFixture.Runtime.Get<object>(f.Vm, "CategoryGroups"), f.Page.CategoryGroupsSource.Source));
            Assert.Equal("主页", HomeOrderFixture.Runtime.Get<string>(f.Vm, "SelectedCategory"), "不能依靠ReplaceCategories尾部把丢失选择掩盖");
            f.Page.UpdateCategoryGroupsForFixture();
            Assert.Equal(writes + 1, f.Page.CategoryGroupsSource.Writes);
            f.Stable();
        });
        Test("HomeOrder 弹幕事件只在真实活动期订阅，重复Loaded不加倍且离树释放", async () =>
        {
            using var f = new HomeOrderFixture();
            var subscribers = ShellPrefs.DanmakuSubscribers;
            f.Page.RaiseLoaded();
            Assert.Equal(subscribers, ShellPrefs.DanmakuSubscribers);
            await f.Open();
            try
            {
                f.Page.RaiseLoaded();
                f.Page.RaiseLoaded();
                Assert.Equal(subscribers + 1, ShellPrefs.DanmakuSubscribers);
                // Native/full-page card construction is outside this fixture; the actual callback must
                // still be invoked and obey its unready guard, not be replaced by an inert VM method.
                HomeOrderFixture.Runtime.Set(f.Vm, "IsReady", false);
                ShellPrefs.RaiseDanmakuChanged();
                Assert.Equal(1, f.Page.ViewModel.DanmakuPreferenceCalls);
                f.Page.XamlRoot = new();
                f.Page.RaiseUnloaded();
                Assert.Equal(subscribers + 1, ShellPrefs.DanmakuSubscribers, "暂时Unloaded不能当真正离树");
            }
            finally
            {
                f.Page.XamlRoot = null;
                f.Page.RaiseUnloaded();
            }
            Assert.Equal(subscribers, ShellPrefs.DanmakuSubscribers);
            ShellPrefs.RaiseDanmakuChanged();
            Assert.Equal(1, f.Page.ViewModel.DanmakuPreferenceCalls);
        });
        Test("HomeOrder 实际排序行hook验证上下移、两端禁移和单次保存", () =>
        {
            var method = HomeOrderFixture.Runtime.Type("SettingHomeLayoutRow").GetMethod("Probe",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var probe = ((bool Ok, string Detail))method.Invoke(null, null)!;
            Assert.True(probe.Ok, probe.Detail);
            Assert.Contains("写盘 2 次", probe.Detail);
        });
        Test("HomeOrder 未导航直接Watch不能借VM Attach打开页面活动期", async () =>
        {
            using var f = new HomeOrderFixture();
            f.Page.WatchMoviePilot(f.Service);
            Assert.True(await f.Connect());
            f.Drain();
            Assert.True(f.Service.Available);
            Assert.Equal(4, f.Rows.Count);
            Assert.Equal(0, f.MoviePilotCount);
            Assert.Equal(0, f.Page.DispatcherQueue.Count);
            f.Stable();
        });
        Test("HomeOrder 共享探针入口恢复活动期，未连接隐藏而GET成功原表补齐两项", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.Equal("媒体库顺序", HomeOrderFixture.Runtime.Get<string>(f.Table, "Label"));
            Assert.Equal(4, f.Rows.Count);
            f.Settings.Settings.MoviePilot.Enabled = true;
            f.Drain();
            Assert.False(f.Service.Available);
            Assert.Equal(0, f.MoviePilotCount);
            Assert.True(await f.Connect());
            Assert.Equal(4, f.Rows.Count, "后台通知只能排队，不能直接重填UI集合");
            f.Drain();
            await f.WaitRows(6);
            Assert.Equal(2, f.MoviePilotCount);
            Assert.Equal(1, f.Reads.Requests);
            Assert.True(double.IsNaN(HomeOrderFixture.Runtime.Get<double>(f.Table, "ListHeight")),
                "主页表按内容伸缩，要为展开的媒体库子项留出空间");
            f.Stable();
        });
        Test("HomeOrder 断线/连接恢复原地4→6，非默认次序与两个取消勾选不丢失", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.True(await f.Connect());
            f.Drain();
            f.Reads.Offline = true;
            Assert.False(await f.Service.RefreshAvailabilityAsync());
            f.Drain();
            await f.WaitRows(4);
            Assert.True(double.IsNaN(HomeOrderFixture.Runtime.Get<double>(f.Table, "ListHeight")),
                "断线后仍由父表按内容测量，不给隐藏的订阅项保留固定高度");
            Assert.Equal(0, f.MoviePilotCount);
            f.Stable();
            f.Reads.Offline = false;
            Assert.True(await f.Connect());
            f.Drain();
            await f.WaitRows(6);
            // 轮播图开着：固定的「媒体库」占第一位，存档里的非默认次序从第二位接上（这一份把订阅排放在最前）。
            Assert.Equal(HomeLayout.Libraries, f.Keys[0]);
            Assert.Equal(HomeLayout.MoviePilotSeries, f.Keys[1]);
            foreach (var row in f.Rows.Cast<object>().Where(row => HomeLayout.IsMoviePilot(HomeOrderFixture.Runtime.Get<string>(row, "Key"))))
                Assert.False(HomeOrderFixture.Runtime.Get<bool>(row, "Visible"));
            f.Stable();
        });
        Test("HomeOrder 聚合多账号时媒体库顺序表写账号用户名，勾选保存的仍是库名", () =>
        {
            using var f = new HomeOrderFixture(secondSource: true);
            Assert.Equal(2, f.Capabilities.Sources.Count);
            Assert.Equal(5, f.Rows.Count);

            var libraryRows = f.Rows.Cast<object>()
                .Where(row => HomeLayout.LibraryId(HomeOrderFixture.Runtime.Get<string>(row, "Key")) is not null)
                .ToArray();
            Assert.Equal(2, libraryRows.Length);
            Assert.Equal("anonymous-user · 最近添加 · 离线电影库",
                HomeOrderFixture.Runtime.Get<string>(libraryRows[0], "Caption"));
            Assert.Equal("second-user · 最近添加 · 离线电影库",
                HomeOrderFixture.Runtime.Get<string>(libraryRows[1], "Caption"));
            Assert.True(libraryRows.All(row => HomeOrderFixture.Runtime.Get<string>(row, "Title") == "最近添加 · 离线电影库"),
                "存档标题不能带账号用户名");

            // 勾一下会写盘：写出去的仍是干净库名，前缀只在屏上，关掉聚合模式后不会留下或叠一层。
            var writes = f.Settings.Writes;
            HomeOrderFixture.Runtime.Set(libraryRows[0], "Visible", false);
            Assert.Equal(writes + 1, f.Settings.Writes);
            Assert.True(f.Settings.Settings.Ui.HomeRows.Where(row => HomeLayout.LibraryId(row.Key) is not null)
                .All(row => row.Title == "最近添加 · 离线电影库"), "保存不能带上屏上的账号用户名");
        });
        Test("HomeOrder 子表用实际库名，媒体库入口与最近添加排分别保存", () =>
        {
            using var f = new HomeOrderFixture(secondSource: true, liveDirectory: true);
            var runtime = HomeOrderFixture.Runtime;
            var entries = runtime.List(LibraryEntries(f), "Rows");
            var keys = f.LibraryDirectory.Select(HomeLayout.LibraryKey).ToArray();
            Assert.True(keys.SequenceEqual(ChoiceKeys(entries)), "默认照目录次序，包括音乐库和另一来源的同 ID 库");
            Assert.Equal("anonymous-user · 离线电影库", runtime.Get<string>(entries[0]!, "Caption"));
            Assert.Equal("second-user · 离线电影库", runtime.Get<string>(entries[2]!, "Caption"));
            Assert.True(entries.Cast<object>().All(row => runtime.Get<bool>(row, "Visible")));
            Assert.Equal(0, f.Settings.Writes);

            runtime.Set(entries[0]!, "Visible", false);
            Assert.Equal(1, f.Settings.Writes);
            var ui = f.Settings.Settings.Ui;
            Assert.True(HomeLayout.Same(f.Saved, HomeLayout.Plan(ui.HomeRows, null)), "子表不能改最近添加排和父行的显示");
            Assert.True(ui.HomeLibraryEntries.Single(entry => entry.Key == keys[2]).Visible, "同 ID 的另一来源库不受影响");
            Assert.Equal("离线电影库", ui.HomeLibraryEntries.Single(entry => entry.Key == keys[0]).Title);
            Assert.False(ui.HomeLibraryEntries.Single(entry => entry.Key == keys[0]).Visible);

            var saved = ui.HomeLibraryEntries.Select(row => (row.Key, row.Title, row.Visible)).ToArray();
            runtime.Set(Choice(f, HomeLayout.Libraries), "Visible", false);
            runtime.Set(Choice(f, keys[1]), "Visible", false);
            Assert.Equal(3, f.Settings.Writes);
            Assert.True(saved.SequenceEqual(ui.HomeLibraryEntries.Select(row => (row.Key, row.Title, row.Visible))),
                "关闭父行及最近添加排后，重新开启时仍应恢复原子表偏好");
        });
        Test("HomeOrder 媒体库子项原生拖动拆插完成才保存，取消保留全表，箭头也能换位", () =>
        {
            using var f = new HomeOrderFixture(liveDirectory: true);
            var runtime = HomeOrderFixture.Runtime;
            var table = LibraryEntries(f);
            var entries = runtime.List(table, "Rows");
            var keys = ChoiceKeys(entries);
            var first = entries[0]!;
            runtime.Invoke(table, "BeginReorder");
            entries.RemoveAt(0);
            entries.Insert(1, first);
            Assert.Equal(0, f.Settings.Writes);
            CompleteReorder(table);
            Assert.Equal(1, f.Settings.Writes);
            Assert.True(keys.Reverse().SequenceEqual(f.Settings.Settings.Ui.HomeLibraryEntries.Select(row => row.Key)));

            runtime.Invoke(table, "BeginReorder");
            entries.RemoveAt(1);
            entries.Insert(0, first);
            runtime.Invoke(table, "CancelReorder");
            Assert.Equal(1, f.Settings.Writes);
            Assert.True(keys.Reverse().SequenceEqual(ChoiceKeys(entries)), "取消落位后恢复原次序");

            runtime.Invoke(table, "BeginReorder");
            entries.RemoveAt(1);
            CompleteReorder(table);
            Assert.Equal(1, f.Settings.Writes);
            Assert.True(keys.Reverse().SequenceEqual(ChoiceKeys(entries)), "拖动取消不丢选项");

            ((ICommand)runtime.Get<object>(first, "UpCommand")).Execute(null);
            Assert.Equal(2, f.Settings.Writes);
            Assert.True(keys.SequenceEqual(ChoiceKeys(entries)));
            Assert.True(keys.SequenceEqual(f.Settings.Settings.Ui.HomeLibraryEntries.Select(row => row.Key)));
            Assert.True(HomeLayout.Same(f.Saved, HomeLayout.Plan(f.Settings.Settings.Ui.HomeRows, null)));
        });
        Test("HomeOrder 刷新目录时维持子表身份，旧拖动不能恢复离线来源或覆盖偏好", () =>
        {
            using var f = new HomeOrderFixture(secondSource: true, liveDirectory: true);
            var runtime = HomeOrderFixture.Runtime;
            var table = LibraryEntries(f);
            var entries = runtime.List(table, "Rows");
            var keys = ChoiceKeys(entries);
            var leaving = entries[2]!;
            Assert.True(ReferenceEquals(table, runtime.Get<object>(Choice(f, HomeLayout.Libraries), "LibraryEntries")));
            Assert.False(runtime.Get<bool>(table, "IsExpanded"), "媒体库子表默认折叠");
            runtime.Set(table, "IsExpanded", true);
            runtime.Set(leaving, "Visible", false);

            runtime.Invoke(table, "BeginReorder");
            entries.RemoveAt(2);
            Revoke(f.SecondSource!);
            f.LibraryDirectory.RemoveAt(2);
            f.LibraryDirectory.Add(f.Source.Bind(new EmbyItem
            {
                Id = "new", Name = "新增剧集库", Type = EmbyItemType.CollectionFolder, CollectionType = "tvshows"
            }));
            runtime.Invoke(f.Vm, "RefreshHomeRows");
            CompleteReorder(table);
            Assert.True(ReferenceEquals(table, LibraryEntries(f)));
            Assert.True(ReferenceEquals(entries, runtime.List(table, "Rows")));
            Assert.True(ReferenceEquals(table, runtime.Get<object>(Choice(f, HomeLayout.Libraries), "LibraryEntries")));
            Assert.True(runtime.Get<bool>(table, "IsExpanded"), "重填父表后保留展开位置");
            Assert.True(f.LibraryDirectory.Select(HomeLayout.LibraryKey).SequenceEqual(ChoiceKeys(entries)));
            Assert.True(runtime.Get<bool>(entries[2]!, "Visible"), "新库默认显示");
            Assert.Equal(1, f.Settings.Writes, "来源切换或旧拖动结束不能保存");

            runtime.Set(leaving, "Visible", true);
            runtime.Set(entries[2]!, "Visible", false);
            Assert.Equal(2, f.Settings.Writes, "已离开的旧行不能提交，当前新行可以提交");
            Assert.False(f.Settings.Settings.Ui.HomeLibraryEntries.Single(row => row.Key == keys[2]).Visible,
                "编辑在线库时要保留离线库偏好");
        });
        Test("HomeOrder 订阅库随真实可用性进入媒体库子表，断线重连保留排序和勾选", async () =>
        {
            using var f = new HomeOrderFixture(liveDirectory: true);
            await f.Open();
            var runtime = HomeOrderFixture.Runtime;
            var subscriptionKey = HomeLibraryLayout.SubscriptionLibraryKey;

            Assert.False(f.LibraryKeys().Contains(subscriptionKey), "MoviePilot 未连接时不应显示订阅库入口");
            Assert.True(await f.Connect());
            f.Drain();
            await f.WaitLibraryEntries(3);

            var table = LibraryEntries(f);
            var rows = runtime.List(table, "Rows");
            var directoryKeys = f.LibraryDirectory.Select(HomeLayout.LibraryKey).ToArray();
            Assert.Equal(3, rows.Count);
            Assert.True(f.LibraryKeys().Contains(subscriptionKey));
            Assert.True(directoryKeys.All(key => f.LibraryKeys().Contains(key)));
            Assert.True(HomeLayout.Same(f.Saved, HomeLayout.Plan(f.Settings.Settings.Ui.HomeRows, null)),
                "新增订阅库入口不能改写父表最近添加排");

            var subscription = rows.Cast<object>().Single(row => runtime.Get<string>(row, "Key") == subscriptionKey);
            var firstLibrary = rows.Cast<object>().Single(row => runtime.Get<string>(row, "Key") == directoryKeys[0]);
            runtime.Invoke(table, "BeginReorder");
            rows.Remove(subscription);
            rows.Insert(0, subscription);
            CompleteReorder(table);
            runtime.Set(firstLibrary, "Visible", false);
            runtime.Set(subscription, "Visible", false);
            var writes = f.Settings.Writes;
            var saved = f.Settings.Settings.Ui.HomeLibraryEntries
                .Select(row => (row.Key, row.Title, row.Visible)).ToArray();
            Assert.Equal(3, writes);
            Assert.Equal(subscriptionKey, saved[0].Key);
            Assert.False(saved[0].Visible);
            Assert.False(saved.Single(row => row.Key == directoryKeys[0]).Visible);

            f.Reads.Offline = true;
            Assert.False(await f.Service.RefreshAvailabilityAsync());
            f.Drain();
            await f.WaitLibraryEntries(2);
            Assert.False(f.LibraryKeys().Contains(subscriptionKey));
            Assert.True(saved.SequenceEqual(f.Settings.Settings.Ui.HomeLibraryEntries
                .Select(row => (row.Key, row.Title, row.Visible))), "订阅库暂不可用时不能清除排序存档");
            Assert.True(HomeLayout.Same(f.Saved, HomeLayout.Plan(f.Settings.Settings.Ui.HomeRows, null)),
                "订阅库可用性不能改写父表");

            f.Reads.Offline = false;
            Assert.True(await f.Connect());
            f.Drain();
            await f.WaitLibraryEntries(3);
            Assert.True(f.LibraryKeys().SequenceEqual([subscriptionKey, directoryKeys[0], directoryKeys[1]]));
            var restored = runtime.List(LibraryEntries(f), "Rows").Cast<object>().ToArray();
            Assert.False(runtime.Get<bool>(restored[0], "Visible"), "订阅库隐藏偏好未恢复");
            Assert.False(runtime.Get<bool>(restored[1], "Visible"), "媒体库隐藏偏好未恢复");
            Assert.True(HomeLayout.Same(f.Saved, HomeLayout.Plan(f.Settings.Settings.Ui.HomeRows, null)),
                "订阅库恢复不能改变父表顺序或勾选");
        });
        Test("HomeOrder 轮播开着时媒体库固定第一位，关掉放开再开回来", () =>
        {
            using var f = new HomeOrderFixture();
            Assert.True(f.Settings.Settings.Ui.ShowHomeBanner, "夹具默认轮播图开着");
            Assert.Equal(HomeLayout.Libraries, f.Keys[0]);
            var library = Choice(f, HomeLayout.Libraries);
            Assert.True(HomeOrderFixture.Runtime.Get<bool>(library, "Pinned"));
            Assert.False(HomeOrderFixture.Runtime.Get<bool>(library, "CanUp"));
            Assert.False(HomeOrderFixture.Runtime.Get<bool>(library, "CanDown"));

            // 第二排不能挪到固定项前面，按了也不动。
            var second = f.Rows[1]!;
            Assert.False(HomeOrderFixture.Runtime.Get<bool>(second, "CanUp"));
            ((ICommand)HomeOrderFixture.Runtime.Get<object>(second, "UpCommand")).Execute(null);
            Assert.Equal(HomeLayout.Libraries, f.Keys[0]);

            // 轮播关掉：固定放开，次序照存档（这一份把订阅排放在最前，断线时「继续观看」打头）。
            var toggle = ToggleRow(f);
            HomeOrderFixture.Runtime.Set(toggle, "IsOn", false);
            Assert.False(f.Settings.Settings.Ui.ShowHomeBanner);
            Assert.Equal(HomeLayout.Resume, f.Keys[0]);
            Assert.False(HomeOrderFixture.Runtime.Get<bool>(Choice(f, HomeLayout.Libraries), "Pinned"));

            HomeOrderFixture.Runtime.Set(toggle, "IsOn", true);
            Assert.True(f.Settings.Settings.Ui.ShowHomeBanner);
            Assert.Equal(HomeLayout.Libraries, f.Keys[0]);
            Assert.True(HomeOrderFixture.Runtime.Get<bool>(Choice(f, HomeLayout.Libraries), "Pinned"));
        });
        Test("HomeOrder 固定项被拖到后面自己落回第一位，别的排照常换位", () =>
        {
            using var f = new HomeOrderFixture();
            var library = Choice(f, HomeLayout.Libraries);
            var writes = f.Settings.Writes;

            // ListView 换位走 Remove ＋ Add 两下：插回来那一拍把固定项拉回第一位，只写一次盘。
            f.Rows.RemoveAt(0);
            f.Rows.Insert(2, library);
            Assert.Equal(HomeLayout.Libraries, f.Keys[0]);
            Assert.Equal(writes + 1, f.Settings.Writes);

            // 固定项下面照常能挪（那两行不归它管）。
            var next = Choice(f, HomeLayout.Resume);
            Assert.False(HomeOrderFixture.Runtime.Get<bool>(next, "CanUp"));
            Assert.True(HomeOrderFixture.Runtime.Get<bool>(next, "CanDown"));
            ((ICommand)HomeOrderFixture.Runtime.Get<object>(next, "DownCommand")).Execute(null);
            Assert.Equal(HomeLayout.Libraries, f.Keys[0]);
            Assert.Equal(HomeLayout.NextUp, f.Keys[1]);
        });
        Test("HomeOrder Release后直接Watch仍拒绝，共享活动期重入才同步最新可用性", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.True(await f.Connect());
            f.Drain();
            f.Page.ReleaseHosted();
            f.Page.WatchMoviePilot(f.Service);
            f.Reads.Offline = true;
            Assert.False(await f.Service.RefreshAvailabilityAsync());
            Assert.Equal(0, f.Page.DispatcherQueue.Count);
            Assert.Equal(6, f.Rows.Count, "活动期结束不应让离树旧表偷偷更新");
            await f.Open();
            await f.WaitRows(4);
            f.Reads.Offline = false;
            Assert.True(await f.Connect());
            f.Drain();
            await f.WaitRows(6);
            f.Stable();
        });
        Test("HomeOrder MP关闭/恢复期间设置页离树；活动期重入4→6且Rows稳定", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.True(await f.Connect());
            f.Drain();
            f.Page.ReleaseHosted();
            f.Settings.Settings.MoviePilot.Enabled = false;
            Assert.Equal(6, f.Rows.Count);
            await f.Open();
            await f.WaitRows(4);
            f.Page.ReleaseHosted();
            Assert.True(await f.Connect());
            Assert.Equal(4, f.Rows.Count);
            await f.Open();
            await f.WaitRows(6);
            f.Stable();
        });
        Test("HomeOrder 旧活动期已排队连接回调不能穿过Release后重入", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            Assert.True(await f.Connect());
            Assert.True(f.Page.DispatcherQueue.Count > 0);
            f.Page.ReleaseHosted();
            f.Settings.Settings.MoviePilot.Enabled = false;
            await f.Open();
            await f.WaitRows(4);
            f.Drain();
            Assert.Equal(4, f.Rows.Count);
            Assert.False(f.Service.Available);
            f.Stable();
        });
        Test("HomeOrder 非主页分类不误等隐藏表；回主页才同步当前可用性", async () =>
        {
            using var f = new HomeOrderFixture();
            await f.Open();
            HomeOrderFixture.Runtime.Set(f.Vm, "SelectedCategory", "关于");
            Assert.True(await f.Connect());
            f.Drain();
            Assert.Equal(4, f.Rows.Count);
            HomeOrderFixture.Runtime.Set(f.Vm, "SelectedCategory", "主页");
            await f.WaitRows(6);
            f.Stable();
        });
        Test("HomeOrder 原样缓存重入调用先开共享活动期，保留投影后的5项与真实表身份", async () =>
        {
            using var f = new HomeOrderFixture();
            // 与生产探针夹具一致：聚合模式开着，设置表不排「接下来看」（2026-11-03 起的行数口径）。
            f.Settings.Settings.AggregateMode = true;
            await f.Open();
            Assert.True(await f.Connect());
            f.Drain();
            await f.Reopen();
            Assert.Equal(5, f.Rows.Count);
            f.Reads.Offline = true;
            Assert.False(await f.Service.RefreshAvailabilityAsync());
            f.Drain();
            await f.WaitRows(3);
            f.Stable();
        });
        Test("HomeOrder 未开启活动期的错误等待仍实际UntilAsync超时，不延时洗绿", async () =>
        {
            using var f = new HomeOrderFixture();
            f.Page.WatchMoviePilot(f.Service);
            Assert.True(await f.Connect());
            f.Drain();
            try { await f.WaitRows(6); throw new AssertionException("错误夹具必须超时"); }
            catch (TimeoutException error) { Assert.Contains("未等到预期状态", error.Message); }
            Assert.Equal(4, f.Rows.Count);
            f.Stable();
        });
    }

    private static object Choice(HomeOrderFixture fixture, string key) =>
        fixture.Rows.Cast<object>().Single(row => HomeOrderFixture.Runtime.Get<string>(row, "Key") == key);

    private static object LibraryEntries(HomeOrderFixture fixture) =>
        HomeOrderFixture.Runtime.Get<object>(fixture.Table, "LibraryEntries")
            ?? throw new AssertionException("找不到媒体库的子排序表");

    private static string[] ChoiceKeys(IList rows) =>
        rows.Cast<object>().Select(row => HomeOrderFixture.Runtime.Get<string>(row, "Key")).ToArray();

    private static void CompleteReorder(object table) =>
        table.GetType().GetMethod("EndReorder",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null, types: Type.EmptyTypes, modifiers: null)!.Invoke(table, null);

    private static void Revoke(EmbySessionScope source)
    {
        var identity = typeof(EmbySessionScope).GetProperty("IdentityState",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(source)!;
        ((CancellationTokenSource)identity.GetType().GetProperty("Lifetime")!.GetValue(identity)!).Cancel();
    }

    /// <summary>「显示主页轮播大图」那一行的真对象 —— Shell 程序集只在运行时加载，这里按反射取。</summary>
    private static object ToggleRow(HomeOrderFixture fixture) =>
        ((IEnumerable)fixture.Sections).Cast<object>()
            .SelectMany(section => HomeOrderFixture.Runtime.List(section, "Rows").Cast<object>())
            .Single(row => HomeOrderFixture.Runtime.Get<string>(row, "Label") == "显示主页轮播大图");

    private static void CoverageContract(string root)
    {
        var source = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "ShellNavigationProbe.HomeOrder.cs"));
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(source,
            @"BeginSettingsFixtureVisit\(settings\);\s+settings\.WatchMoviePilot\(service\);").Count,
            "初入、缓存重入和MP关/开恢复统一复用共享入口");
        Assert.False(source.Contains("private static void BeginSettingsFixtureVisit", StringComparison.Ordinal), "不能重复定义共享入口");
        Assert.Contains("settings.ReleaseHosted();\n            _root.Children.Remove(settings);", source);
        foreach (var assertion in new[]
        {
            "关闭时排序表仍有 MP 或标题未改", "未连接时排序表显示 MP", "排序表未实现订阅勾选控件",
            "电影订阅勾选未保存", "假断线没有撤销可用性", "断线后排序表没有收起整行或仍有空白",
            "断线后有效列表项仍有订阅控件", "假连接未恢复", "恢复连接丢失原次序或勾选",
            "缓存设置页重入未恢复订阅排序", "主页未采用排序表次序", "混排订阅命中了隐藏的标题或条带",
            "订阅标题与封面之间缺少间距", "订阅混排的轮播折叠阈值漂移", "取消电视剧勾选影响了电影排",
            "关闭后连接未恢复", "关闭后恢复丢失电视剧勾选", "主页排序触发了订阅写操作"
        }) Assert.Contains(assertion, source);
        foreach (var evidence in new[]
        {
            "new[] { 1100, 760 }", "settings-home-order-{width}.png", "settings-home-order-offline.png",
            "settings-home-order-restored.png", "home-subscription-order-{width}.png",
            "home-subscription-order-overlay.png", "home-subscription-order-disabled.png",
            "movieCheck.IsChecked = false", "while (series.CanUp)",
            "choices.Single(row => row.Key == HomeLayout.Resume).DownCommand.Execute(null)",
            "before.HasNext && after.HasNext && Math.Abs(before.Line - after.Line) < 2",
            "home.HomeRows.All(row => row is not MoviePilotHomeRow)", "await UntilAsync(() => choices.Count == 5)",
            "server.Writes.Count == 0"
        }) Assert.Contains(evidence, source);
    }
}
