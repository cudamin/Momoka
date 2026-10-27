using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>
/// 无 WinUI 测试引用的真实 SettingsViewModel 回归入口。显式测试开关提供本树当前 Shell Release 程序集，
/// 只登记本套件，不运行产品入口，不自动加载其他工作树；实际构建新鲜度由标准先编译流程核验。
/// 缓存重入只执行源码中 built 分支列出的 VM 调用，不构造 SettingsPage，不冒称实际 UI 导航。
/// </summary>
internal static class SettingsPageStateTests
{
    private const string SaveStateMethod = "SyncSaveState";
    private const string OpenDirectoryMethod = "OpenDirectoryAsync";
    private static readonly string[] DirectoryLabels = ["设置文件", "日志目录", "缓存目录", "截图目录"];

    internal static int Run(string shellAssemblyPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("设置页真实 VM／行命令回归（假服务、无窗口；不运行产品或真实目录操作）");
        try
        {
            Register(shellAssemblyPath, sourceRoot);
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    public static void Register(string shellAssemblyPath, string sourceRoot)
    {
        var fixture = new ShellFixture(shellAssemblyPath, sourceRoot);
        TestHarness.Test("R-073 缓存 built 分支接上轻量同步，不重读整页（源码接线）", () =>
        {
            Assert.True(fixture.CachedCalls.Contains(SaveStateMethod, StringComparer.Ordinal), "缓存分支必须同步共享保存状态");
            Assert.True(fixture.CachedCalls.All(name => name is SaveStateMethod or "RefreshAudioDevicesAsync"),
                "缓存分支只读保存 flag 和既有设备列表，不允许 Reload 或 Save");
        });
        TestHarness.Test("R-073 外部 TrySave 失败后重入，真实 VM 提示与重试一起开启", () =>
            CachedSaveTransition(fixture, nextSaveSucceeds: false));
        TestHarness.Test("R-073 外部 TrySave 成功后重入，真实 VM 提示与重试一起关闭", () =>
            CachedSaveTransition(fixture, nextSaveSucceeds: true));
        TestHarness.Test("R-073 未 Attach 的真实 VM 同步无副作用", () =>
        {
            var vm = fixture.NewViewModel();
            fixture.Invoke(vm, SaveStateMethod);
            Assert.False(fixture.Get<bool>(vm, "HasUnsavedChanges"));
            Assert.False(fixture.Command(vm, "RetrySaveCommand").CanExecute(null));
            Assert.False(fixture.Get<bool>(vm, "NoticeOpen"));
            Assert.Equal(0, fixture.List(vm, "Sections").Count);
        });

        // 2026-10-27 用户令「开启隐藏下方说明之后用线条分割每个功能」：说明收起时每行顶上画一条细线，
        // 每张卡的第一行不画。这里量的是真实的 SettingRow/SettingSection 接线（首行身份、行表增删后换人），
        // 屏上那条线由 settings 探针和自检的 ProbeCompactMode 另外量。
        TestHarness.Test("设置行：说明收起后行间画线，首行不画且随行表换人", () =>
        {
            static double Top(object thickness) =>
                (double)thickness.GetType().GetProperty("Top")!.GetValue(thickness)!;

            var first = fixture.NewRow("SettingFactRow", "第一行", "说明", "值", null, null);
            var second = fixture.NewRow("SettingFactRow", "第二行", "说明", "值", null, null);
            var section = fixture.NewSection(first, second);
            Assert.Equal(0, Top(fixture.Get<object>(first, "DividerThickness")));
            Assert.Equal(0, Top(fixture.Get<object>(second, "DividerThickness")));

            fixture.SetStatic("SettingRow", "NotesHidden", true);
            try
            {
                Assert.Equal(0, Top(fixture.Get<object>(first, "DividerThickness")));
                Assert.Equal(1, Top(fixture.Get<object>(second, "DividerThickness")));

                var rows = fixture.List(section, "Rows");
                rows.Remove(first);
                Assert.Equal(0, Top(fixture.Get<object>(second, "DividerThickness")));

                var third = fixture.NewRow("SettingFactRow", "第三行", "说明", "值", null, null);
                rows.Add(third);
                Assert.Equal(1, Top(fixture.Get<object>(third, "DividerThickness")));
                Assert.Equal(0, Top(fixture.Get<object>(second, "DividerThickness")));
            }
            finally
            {
                fixture.SetStatic("SettingRow", "NotesHidden", false);
            }

            Assert.Equal(0, Top(fixture.Get<object>(second, "DividerThickness")));
        });

        // 2026-10-27 用户令：弹幕一页先拆成四张卡，同日又令「删除这个」，最下面那张「应用凭据」整张退役。
        // 整页 Reload 要碰字体、音频设备与预览（native），所以这里只量源码里的建卡接线：三张卡的次序与标题、
        // 凭据行是否彻底离开这份行表、建卡入口是否把三张都加进 Sections。屏上的行由 settings 探针量。
        TestHarness.Test("弹幕设置：三张卡分区，凭据行已退役（源码接线）", () =>
        {
            var source = File.ReadAllText(Path.Combine(fixture.Root, "src", "Momoka.Shell", "ViewModels",
                "SettingsViewModel.Danmaku.cs"));
            var titles = Regex.Matches(source, @"new SettingSection\(DanmakuCategory, ""(?<title>[^""]+)""")
                .Select(match => match.Groups["title"].Value).ToArray();
            Assert.True(titles.SequenceEqual(new[] { "弹幕", "显示与外观", "类型与屏蔽" }),
                "弹幕分类的三张卡次序或标题变了：" + string.Join('、', titles));
            // 退役判据看的是「还有没有建出这张卡和这几行」，不是注释里提没提这件事：源码注释会一直解释
            // 这张卡为什么被删。行的标题在源码里是带引号的字面量，注释里没有引号。
            foreach (var gone in new[] { "应用凭据", "使用自己的应用凭据", "AppId", "AppSecret", "弹弹play 开发者中心" })
                Assert.False(Regex.IsMatch(source, "\"" + Regex.Escape(gone) + "\""),
                    $"{gone} 已经退役，不该再出现在弹幕设置的行表里");
            Assert.True(fixture.ViewModelSource.Contains("AddCards(DanmakuSections());", StringComparison.Ordinal),
                "ReloadAsync 没有把三张弹幕卡都加进 Sections");
        });

        // 2026-10-27 用户令「其他页也拆一下」：主页、播放行为、音频输出、字幕、实时字幕翻译、关于各自拆成多张卡。
        // 和上面那条同一个理由 —— 整页 Reload 要碰字体、音频设备与预览（native），这里只钉住建卡入口里的卡名；
        // 屏上的行由 settings 探针按分类逐个打开量。
        TestHarness.Test("设置分区：六个分类各自拆成多张卡（源码接线）", () =>
        {
            var translation = File.ReadAllText(Path.Combine(fixture.Root, "src", "Momoka.Shell", "ViewModels",
                "SettingsViewModel.Translation.cs"));
            foreach (var (category, title) in new[]
            {
                ("主页", "轮播图"), ("主页", "主页版面"),
                ("播放行为", "播放行为"), ("播放行为", "跳转与步长"), ("播放行为", "音轨"),
                ("音频输出", "输出设备"), ("音频输出", "响度"), ("音频输出", "直通"),
                ("字幕", "字幕"), ("字幕", "字幕外观"), ("字幕", "编码与画面"),
                ("关于", "关于"), ("关于", "更新"), ("关于", "文件与缓存"), ("关于", "备份与恢复")
            })
            {
                Assert.True(fixture.ViewModelSource.Contains($"new SettingSection(\"{category}\", \"{title}\"", StringComparison.Ordinal),
                    $"{category} 分类少了「{title}」那张卡");
            }
            // 主卡用的是 TranslationCardTitle 常量（探针与刷新都按它找），另外两张写的是字面量。
            Assert.True(translation.Contains("new SettingSection(\"字幕\", TranslationCardTitle", StringComparison.Ordinal),
                "实时字幕翻译少了主开关那张卡");
            foreach (var title in new[] { "服务与密钥", "译文外观" })
                Assert.True(translation.Contains($"new SettingSection(\"字幕\", \"{title}\"", StringComparison.Ordinal),
                    $"实时字幕翻译少了「{title}」那张卡");
            Assert.True(fixture.ViewModelSource.Contains("AddCards(HomeSections());", StringComparison.Ordinal)
                && fixture.ViewModelSource.Contains("AddCards(PlaybackSections());", StringComparison.Ordinal)
                && fixture.ViewModelSource.Contains("AddCards(AudioSections());", StringComparison.Ordinal)
                && fixture.ViewModelSource.Contains("AddCards(SubtitleSections());", StringComparison.Ordinal)
                && fixture.ViewModelSource.Contains("AddCards(AboutSections());", StringComparison.Ordinal),
                "ReloadAsync 没有把拆开的分区卡都加进 Sections");
        });

        TestHarness.Test("R-060 关于四目录共用 Task 入口，截图不再自行建目录（源码接线）", () =>
        {
            Assert.Equal(typeof(Task), fixture.Method(OpenDirectoryMethod).ReturnType);
            Assert.Equal(4, Regex.Matches(fixture.AboutSource, @"\bOpenDirectoryAsync\(").Count);
            Assert.False(fixture.AboutSource.Contains("_launcher?.OpenFolder", StringComparison.Ordinal));
            Assert.False(fixture.ViewModelSource.Contains("private void OpenScreenshotDirectory()", StringComparison.Ordinal));
        });
        foreach (var label in DirectoryLabels)
        {
            TestHarness.Test($"R-060 {label}正常打开，真实行命令只调用假 launcher 一次", () =>
                DirectoryCommandAsync(fixture, label, error: null).GetAwaiter().GetResult());
            foreach (var kind in new[] { "权限", "IO", "启动" })
            {
                TestHarness.Test($"R-060 {label}{kind}失败，真实行命令不外溢并通知页面", () =>
                    DirectoryCommandAsync(fixture, label, LauncherError(kind)).GetAwaiter().GetResult());
            }
        }
        TestHarness.Test("R-060 截图点击读取最新配置路径，而非建卡时的旧路径", () =>
            DirectoryCommandAsync(fixture, "截图目录", error: null, changeScreenshot: true).GetAwaiter().GetResult());
        TestHarness.Test("R-060 真实 Task 入口成功可 await，不保存设置", () =>
            DirectoryTaskAsync(fixture, error: null).GetAwaiter().GetResult());
        foreach (var kind in new[] { "权限", "IO", "启动" })
        {
            TestHarness.Test($"R-060 真实 Task 入口承接{kind}异常，任务成功完成且留下错误提示", () =>
                DirectoryTaskAsync(fixture, LauncherError(kind)).GetAwaiter().GetResult());
        }
        TestHarness.Test("R-060 未 Attach、空白目录调用 Task 安全无效", () =>
        {
            var vm = fixture.NewViewModel();
            ((Task)fixture.Invoke(vm, OpenDirectoryMethod, "X:\\假目录|不触盘")!).GetAwaiter().GetResult();
            var settings = new FakeSettings();
            var launcher = fixture.NewLauncher();
            fixture.Attach(vm, settings, launcher);
            foreach (var empty in new[] { "", " ", "\t\r\n" })
                ((Task)fixture.Invoke(vm, OpenDirectoryMethod, empty)!).GetAwaiter().GetResult();
            Assert.Equal(0, launcher.Opened.Count);
            Assert.Equal(0, settings.TrySaveCalls);
            Assert.False(fixture.Get<bool>(vm, "NoticeOpen"));
        });
    }

    private static void CachedSaveTransition(ShellFixture fixture, bool nextSaveSucceeds)
    {
        var settings = new FakeSettings { NextSaveSucceeds = !nextSaveSucceeds };
        settings.TrySave();
        var vm = fixture.NewViewModel();
        fixture.Attach(vm, settings, fixture.NewLauncher());
        // 与原 ReloadAsync 的初次保存 flag 一致；不整页 Reload，避免字体、音频设备和预览触及 native。
        fixture.Set(vm, "HasUnsavedChanges", settings.Unsaved);
        fixture.Set(vm, "IsReady", true);
        fixture.Set(vm, "SelectedCategory", "关于");

        // 真行的构造阶段能持有未提交草稿；真实 TextBox 的半输入/焦点仍需后续 UI 验证。
        var commits = 0;
        var saves = 0;
        const string draft = "  \"C:\\输入还没打完";
        var text = fixture.NewRow("SettingTextRow", "草稿", null, "路径", draft,
            (Func<string, string>)(value => { commits++; return value; }), (Action)(() => saves++));
        var number = fixture.NewRow("SettingNumberRow", "未完成数字", null, 0d, 100d, 17d,
            (Action<double>)(_ => commits++), (Func<double>)(() => 17d), (Action)(() => saves++), null);
        fixture.Set(number, "Value", double.NaN);
        var section = fixture.NewSection(text, number);
        fixture.List(vm, "Sections").Add(section);
        fixture.Set(section, "IsVisible", true);

        var sections = fixture.List(vm, "Sections");
        var categories = fixture.List(vm, "Categories");
        var groups = fixture.Get<object>(vm, "CategoryGroups");
        var selected = fixture.Get<object>(vm, "SelectedEntry");
        var rows = fixture.List(section, "Rows");
        var categoryItems = categories.Cast<object>().ToArray();
        var notifications = new List<string>();
        var collectionChanges = 0;
        var retryChanges = 0;
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, args) => notifications.Add(args.PropertyName ?? "");
        foreach (var collection in new[] { sections, categories, rows })
            ((INotifyCollectionChanged)collection).CollectionChanged += (_, _) => collectionChanges++;
        var retry = fixture.Command(vm, "RetrySaveCommand");
        retry.CanExecuteChanged += (_, _) => retryChanges++;

        settings.NextSaveSucceeds = nextSaveSucceeds;
        settings.Settings.Mpv.ScreenshotDirectory = "外部更新，不重灌草稿";
        settings.TrySave(); // 共享服务在别的页面保存：这里只是假服务，不读写配置。
        var reads = settings.FlagReads;
        fixture.ReenterCached(vm);

        Assert.Equal(!nextSaveSucceeds, fixture.Get<bool>(vm, "HasUnsavedChanges"));
        Assert.Equal(!nextSaveSucceeds, retry.CanExecute(null));
        Assert.Equal(nextSaveSucceeds ? "Collapsed" : "Visible", fixture.Get<object>(vm, "UnsavedVisibility").ToString());
        Assert.Equal(reads + 1, settings.FlagReads, "重入只读取共享保存 flag 一次");
        Assert.Equal(1, retryChanges, "生成命令通知必须随 flag 翻转");
        Assert.Equal(1, notifications.Count(name => name == "HasUnsavedChanges"));
        Assert.Equal(1, notifications.Count(name => name == "UnsavedVisibility"));
        fixture.Invoke(vm, SaveStateMethod);
        fixture.Invoke(vm, SaveStateMethod);
        Assert.Equal(1, retryChanges, "重复同步不制造伪变更");
        Assert.Equal(2, settings.TrySaveCalls, "初始/外部保存之外，重入不能多 Save");
        Assert.Equal(0, commits);
        Assert.Equal(0, saves);
        Assert.Equal(0, collectionChanges, "轻量同步不清空或重建集合");
        Assert.True(ReferenceEquals(sections, fixture.List(vm, "Sections")));
        Assert.True(ReferenceEquals(categories, fixture.List(vm, "Categories")));
        Assert.True(ReferenceEquals(groups, fixture.Get<object>(vm, "CategoryGroups")));
        Assert.True(ReferenceEquals(selected, fixture.Get<object>(vm, "SelectedEntry")));
        Assert.True(categories.Cast<object>().SequenceEqual(categoryItems, ReferenceEqualityComparer.Instance));
        Assert.True(ReferenceEquals(section, sections[0]));
        Assert.True(ReferenceEquals(rows, fixture.List(section, "Rows")));
        Assert.True(ReferenceEquals(text, rows[0]));
        Assert.True(ReferenceEquals(number, rows[1]));
        Assert.Equal(draft, fixture.Get<string>(text, "Text"));
        Assert.True(double.IsNaN(fixture.Get<double>(number, "Value")), "未完成数字不能被共享设置重灌");
        Assert.Equal("关于", fixture.Get<string>(vm, "SelectedCategory"));
        Assert.True(fixture.Get<bool>(vm, "IsReady"));
        Assert.True(fixture.Get<bool>(section, "IsVisible"));
        Assert.False(fixture.Get<bool>(vm, "NoticeOpen"));
    }

    private static async Task DirectoryCommandAsync(ShellFixture fixture, string label, Exception? error, bool changeScreenshot = false)
    {
        var settings = new FakeSettings();
        var launcher = fixture.NewLauncher(error);
        var vm = fixture.NewViewModel();
        fixture.Attach(vm, settings, launcher);
        // 关于 2026-10-27 拆成四张卡：建卡入口返回一份列表，目录行都在「文件与缓存」那张里。
        var aboutCards = ((IEnumerable)fixture.Invoke(vm, "AboutSections")!).Cast<object>().ToArray();
        var row = aboutCards.SelectMany(card => fixture.List(card, "Rows").Cast<object>())
            .Single(row => fixture.Get<string>(row, "Label") == label);
        var expected = fixture.DirectoryFor(label, settings);
        if (changeScreenshot)
        {
            settings.Settings.Mpv.ScreenshotDirectory = $"  \"{fixture.Paths.Root}\\新截图\"  ";
            expected = fixture.DirectoryFor(label, settings);
            Assert.False(expected == fixture.Get<string>(row, "Value"), "点击前先改共享偏好");
        }
        var reported = ObserveError(fixture, vm);
        Assert.True(fixture.Command(row, "RunCommand").CanExecute(null));
        fixture.Command(row, "RunCommand").Execute(null); // 修前这里同步外溢，修后真实 AboutCard 接线进入 Task。
        await (error is null ? launcher.Called.Task : reported.Task).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, launcher.Opened.Count);
        Assert.Equal(expected, launcher.Opened.Single());
        Assert.Equal(0, settings.TrySaveCalls);
        if (error is null) Assert.False(fixture.Get<bool>(vm, "NoticeOpen"));
        else AssertError(fixture, vm, error);
    }

    private static async Task DirectoryTaskAsync(ShellFixture fixture, Exception? error)
    {
        var settings = new FakeSettings();
        var launcher = fixture.NewLauncher(error);
        var vm = fixture.NewViewModel();
        fixture.Attach(vm, settings, launcher);
        var task = (Task)fixture.Invoke(vm, OpenDirectoryMethod, fixture.Paths.LogDirectory)!;
        await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(task.IsCompletedSuccessfully, "异常由任务入口报告，不转成故障任务或 async void 外溢");
        Assert.Equal(1, launcher.Opened.Count);
        Assert.Equal(fixture.Paths.LogDirectory, launcher.Opened.Single());
        Assert.Equal(0, settings.TrySaveCalls);
        if (error is null) Assert.False(fixture.Get<bool>(vm, "NoticeOpen"));
        else AssertError(fixture, vm, error);
    }

    private static TaskCompletionSource ObserveError(ShellFixture fixture, object vm)
    {
        var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "NoticeOpen" && fixture.Get<bool>(vm, "NoticeOpen")) reported.TrySetResult();
        };
        return reported;
    }

    private static void AssertError(ShellFixture fixture, object vm, Exception error)
    {
        Assert.True(fixture.Get<bool>(vm, "NoticeOpen"));
        Assert.Equal("Visible", fixture.Get<object>(vm, "NoticeVisibility").ToString());
        Assert.Equal("Error", fixture.Get<object>(vm, "NoticeSeverity").ToString());
        Assert.Equal("打开目录失败", fixture.Get<string>(vm, "NoticeTitle"));
        Assert.Equal(Failure.Describe(error), fixture.Get<string>(vm, "NoticeMessage"));
    }

    private static Exception LauncherError(string kind) => kind switch
    {
        "权限" => new UnauthorizedAccessException("假目录无权限"),
        "IO" => new IOException("假目录磁盘不可用"),
        _ => new Win32Exception(1155, "假目录启动失败")
    };

    internal sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public int PageSize => 50;
        public bool NextSaveSucceeds { get; set; } = true;
        public bool Unsaved { get; private set; }
        public int TrySaveCalls { get; private set; }
        public int FlagReads { get; private set; }
        public event EventHandler? SaveStateChanged { add { } remove { } }
        public bool HasUnsavedChanges { get { FlagReads++; return Unsaved; } }
        public bool TrySave() { TrySaveCalls++; Unsaved = !NextSaveSucceeds; return NextSaveSucceeds; }
        public void Save() => throw new InvalidOperationException("回归仅允许显式假 TrySave");
        public ServerProfile ResolveServer(string url) => throw new InvalidOperationException("禁止真实账号路径");
        public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new InvalidOperationException("禁止真实账号路径");
        public void ExportPreferences(string path) => throw new InvalidOperationException("禁止配置 IO");
        public AppSettings ReadBackup(string path) => throw new InvalidOperationException("禁止配置 IO");
    }

    internal sealed class ShellFixture
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly Assembly _shell;
        private readonly Type _viewModel;
        public string ViewModelSource { get; }
        public string AboutSource { get; }
        public string[] CachedCalls { get; }
        public Infrastructure.AppPaths Paths { get; }
        /// <summary>当前工作树根：源码级接线检查按它找别的分部文件。</summary>
        public string Root { get; }

        public ShellFixture(string shellAssemblyPath, string sourceRoot)
        {
            var root = Path.GetFullPath(sourceRoot);
            Root = root;
            var shellPath = Path.GetFullPath(shellAssemblyPath);
            var releaseRoot = Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar;
            if (!File.Exists(Path.Combine(root, "Momoka.sln"))
                || !shellPath.StartsWith(releaseRoot, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(shellPath), "Momoka.dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Shell 程序集必须来自指定工作树的当前 x64 Release 输出");
            var directory = Path.GetDirectoryName(shellPath)!;
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            };
            _shell = AssemblyLoadContext.Default.LoadFromAssemblyPath(shellPath);
            _viewModel = _shell.GetType("Momoka.Shell.ViewModels.SettingsViewModel", throwOnError: true)!;
            ViewModelSource = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "ViewModels", "SettingsViewModel.cs"));
            // 2026-10-27 起关于卡拆成四张，建卡入口改名 AboutSections()；取的是这一整段（四张卡都在里面）。
            var aboutStart = ViewModelSource.IndexOf("private IReadOnlyList<SettingSection> AboutSections()", StringComparison.Ordinal);
            var aboutEnd = ViewModelSource.IndexOf("private async Task MeasureImageCacheAsync()", aboutStart, StringComparison.Ordinal);
            AboutSource = ViewModelSource[aboutStart..aboutEnd];
            var page = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "SettingsPage.xaml.cs"));
            var cached = Regex.Match(page, @"if\s*\(built\)\s*(?:\{(?<body>[\s\S]*?)^\s*\}|(?<body>[^\r\n]+))", RegexOptions.Multiline);
            Assert.True(cached.Success, "必须找到真实页面的 cached built 分支");
            var body = Regex.Replace(cached.Groups["body"].Value, @"/\*.*?\*/|//[^\r\n]*", "", RegexOptions.Singleline);
            CachedCalls = Regex.Matches(body, @"ViewModel\.(\w+)\(\)").Select(match => match.Groups[1].Value).ToArray();
            // 故意使用非法目录片段：修前截图入口的遗留 CreateDirectory 也不能在红测时写盘。
            Paths = new Infrastructure.AppPaths(Path.Combine(root, "work", "settings-page-state", "假目录|禁止真实IO"));
        }

        public object NewViewModel() => Activator.CreateInstance(_viewModel)!;
        public MethodInfo Method(string name) => _viewModel.GetMethod(name, Members)
            ?? throw new AssertionException($"真实 SettingsViewModel 缺少入口 {name}");
        public object? Invoke(object target, string name, params object?[] args)
        {
            var method = target.GetType().GetMethod(name, Members)
                ?? throw new AssertionException($"真实 {target.GetType().Name} 缺少入口 {name}");
            return method.Invoke(target, args);
        }
        public T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, Members)!.GetValue(target)!;
        public void Set(object target, string name, object value) => target.GetType().GetProperty(name, Members)!.SetValue(target, value);
        public IList List(object target, string name) => Get<IList>(target, name);
        public ICommand Command(object target, string name) => Get<ICommand>(target, name);

        public SettingsPageStateLauncherProxy NewLauncher(Exception? error = null)
        {
            var contract = _shell.GetType("Momoka.Shell.Platform.ISystemLauncher", throwOnError: true)!;
            var launcher = (SettingsPageStateLauncherProxy)DispatchProxy.Create(contract, typeof(SettingsPageStateLauncherProxy));
            launcher.Error = error;
            return launcher;
        }

        public void Attach(object vm, ISettingsService settings, object launcher)
        {
            var args = Method("Attach").GetParameters().Select(parameter => parameter.Name switch
            {
                "settings" => (object)settings,
                "paths" => Paths,
                "launcher" => launcher,
                _ => null
            }).ToArray();
            // 调用真实 Attach；其余依赖留空，测试不调用字体/native/网络/播放或整页 Reload。
            Invoke(vm, "Attach", args);
        }

        public object NewRow(string type, params object?[] args) =>
            Activator.CreateInstance(_shell.GetType("Momoka.Shell.ViewModels." + type, throwOnError: true)!, Members,
                binder: null, args, culture: null)!;

        /// <summary>页级静态状态（如 <c>SettingRow.NotesHidden</c>）：只在测试里拨，拨完由调用方放回。</summary>
        public void SetStatic(string type, string name, object value) =>
            _shell.GetType("Momoka.Shell.ViewModels." + type, throwOnError: true)!
                .GetProperty(name, Members | BindingFlags.Static)!.SetValue(null, value);

        public object NewSection(params object[] rows)
        {
            var rowType = _shell.GetType("Momoka.Shell.ViewModels.SettingRow", throwOnError: true)!;
            var array = Array.CreateInstance(rowType, rows.Length);
            for (var index = 0; index < rows.Length; index++) array.SetValue(rows[index], index);
            return NewRow("SettingSection", "关于", "草稿卡", "隔离测试", array);
        }

        public void ReenterCached(object vm)
        {
            foreach (var name in CachedCalls)
            {
                Assert.True(name is SaveStateMethod or "RefreshAudioDevicesAsync", "不执行其他页面操作");
                if (Invoke(vm, name) is Task task) task.GetAwaiter().GetResult();
            }
        }

        public string DirectoryFor(string label, FakeSettings settings) => label switch
        {
            "设置文件" => Paths.Root,
            "日志目录" => Paths.LogDirectory,
            "缓存目录" => Path.GetDirectoryName(Paths.ImageCacheDirectory) ?? Paths.Root,
            _ => Infrastructure.AppPaths.ResolveScreenshotDirectory(settings.Settings.Mpv.ScreenshotDirectory, Paths.ScreenshotDirectory)!
        };
    }
}

/// <summary>只记录或抛出假异常，不打开 Explorer、不建目录、不调用真实平台 API。</summary>
public class SettingsPageStateLauncherProxy : DispatchProxy
{
    public Exception? Error { get; set; }
    public ConcurrentQueue<string> Opened { get; } = new();
    public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name != "OpenFolder") throw new InvalidOperationException("仅授权假目录 launcher");
        Opened.Enqueue((string)args![0]!);
        Called.TrySetResult();
        if (Error is { } error) throw error;
        return null;
    }
}
