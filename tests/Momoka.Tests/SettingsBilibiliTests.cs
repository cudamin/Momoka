using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Services;
using Receipt = (string CookieHeader, long UserId, string Name);

namespace Momoka.Tests;

/// <summary>真实设置视图模型配假内存保存和登录回执，不打开 WebView、网络或用户配置。</summary>
internal static class SettingsBilibiliTests
{
    internal static int Run(string shellAssemblyPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var fixture = new Fixture(shellAssemblyPath, sourceRoot);
        TestHarness.Test("B站登录网页：核对期间换号或退出拒绝旧候选，稳定候选才能成功", () =>
        {
            foreach (var current in new[] { "SESSDATA=old", "SESSDATA=new", "" })
            {
                var validation = new TaskCompletionSource<BilibiliAccount>();
                var token = CancellationToken.None;
                var account = fixture.Validate("SESSDATA=old", (_, seen) =>
                {
                    Assert.Equal(token, seen);
                    return validation.Task;
                }, () => Task.FromResult(current), () => true, token);
                validation.SetResult(new(12, "旧账号"));
                Assert.Equal(current == "SESSDATA=old", account.GetAwaiter().GetResult() is not null);
            }
        });
        TestHarness.Test("B站登录网页：第二次Cookie读取在飞时关闭拒绝回执", () =>
        {
            var cookies = new TaskCompletionSource<string>();
            var live = true;
            var account = fixture.Validate("SESSDATA=old", (_, _) => Task.FromResult(new BilibiliAccount(12, "旧账号")),
                () => cookies.Task, () => live, CancellationToken.None);
            live = false;
            cookies.SetResult("SESSDATA=old");
            Assert.True(account.GetAwaiter().GetResult() is null);
        });
        TestHarness.Test("B站设置：未登录保留来源和登录，隐藏无效外观及自动匹配", () =>
        {
            var state = fixture.Create();
            var rows = state.Rows();
            var choices = state.Get<IEnumerable>(rows.Single(row => state.Label(row) == "弹幕来源"), "Choices").Cast<object>().ToArray();
            Assert.Equal(4, choices.Length);
            Assert.True(choices.Any(choice => state.Get<string>(choice, "Label") == "哔哩哔哩"));
            Assert.True(rows.Any(row => state.Label(row) == "登录 B 站"));
            Assert.False(rows.Any(row => state.Label(row) == "显示弹幕"));
            Assert.False(rows.Any(row => state.Label(row) == "自动匹配弹幕"));
            Assert.False(rows.Any(row => state.Label(row) == "退出 B 站"));
        });
        TestHarness.Test("B站设置：登录后原地恢复外观和自动匹配，不显示发送", () =>
        {
            var state = fixture.Create();
            state.Settings.Settings.Danmaku.ProtectedBilibiliCookie = "placeholder-protected";
            state.Settings.Settings.Danmaku.BilibiliUserId = 42;
            state.Settings.Settings.Danmaku.BilibiliUserName = "测试账号";
            state.Rebuild();
            var labels = state.Rows().Select(state.Label).ToArray();
            Assert.True(labels.Contains("B 站账户"));
            Assert.True(labels.Contains("退出 B 站"));
            Assert.True(labels.Contains("显示弹幕"));
            Assert.True(labels.Contains("自动匹配弹幕"));
            Assert.False(labels.Contains("包含关联弹幕"));
            Assert.False(labels.Contains("显示弹幕输入栏"));
        });
        TestHarness.Test("B站登录：保存成功才替换旧账号并广播", () =>
        {
            var state = fixture.Create();
            state.SeedAccount();
            var pushes = state.Listen();
            try
            {
                state.UseLogin(_ => Task.FromResult<Receipt?>(("SESSDATA=placeholder-session; bili_jct=placeholder-csrf; DedeUserID=42", 42, "新账号")));
                state.Login().GetAwaiter().GetResult();
                Assert.Equal(1, state.Settings.Saves);
                Assert.Equal(42L, state.Settings.Settings.Danmaku.BilibiliUserId);
                Assert.Equal("新账号", state.Settings.Settings.Danmaku.BilibiliUserName);
                Assert.Contains("protected:", state.Settings.Settings.Danmaku.ProtectedBilibiliCookie);
                Assert.Equal(1, pushes.Count);
            }
            finally { pushes.Stop(); }
        });
        TestHarness.Test("B站登录：保存失败回滚旧账号且不广播", () =>
        {
            var state = fixture.Create();
            state.SeedAccount();
            state.Settings.SaveSucceeds = false;
            var pushes = state.Listen();
            try
            {
                state.UseLogin(_ => Task.FromResult<Receipt?>(("SESSDATA=placeholder-session; bili_jct=placeholder-csrf; DedeUserID=42", 42, "新账号")));
                state.Login().GetAwaiter().GetResult();
                state.AssertOldAccount();
                Assert.Equal(1, state.Settings.Saves);
                Assert.Equal(0, pushes.Count);
            }
            finally { pushes.Stop(); }
        });
        TestHarness.Test("B站登录：取消关闭和重开后迟到回执不能保存", () =>
        {
            var state = fixture.Create();
            state.SeedAccount();
            var completion = new TaskCompletionSource<Receipt?>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken loginToken = default;
            state.UseLogin(token => { loginToken = token; return completion.Task; });
            var login = state.Login();
            state.Call("Cancel");
            Assert.True(loginToken.IsCancellationRequested);
            state.Call("Activate");
            completion.SetResult(("SESSDATA=placeholder-session; bili_jct=placeholder-csrf; DedeUserID=42", 42, "迟到账号"));
            login.GetAwaiter().GetResult();
            state.AssertOldAccount();
            Assert.Equal(0, state.Settings.Saves);
        });
        TestHarness.Test("B站登录：空回执与重复点击不保存或重复打开", () =>
        {
            var state = fixture.Create();
            var completion = new TaskCompletionSource<Receipt?>();
            var calls = 0;
            state.UseLogin(_ => { calls++; return completion.Task; });
            var first = state.Login();
            state.Login().GetAwaiter().GetResult();
            Assert.Equal(1, calls);
            completion.SetResult(null);
            first.GetAwaiter().GetResult();
            Assert.Equal(0, state.Settings.Saves);
        });
        TestHarness.Test("B站登录：关闭重开可再次登录，旧完成不解除新请求的重复保护", () =>
        {
            var state = fixture.Create();
            var first = new TaskCompletionSource<Receipt?>();
            var second = new TaskCompletionSource<Receipt?>();
            var calls = 0;
            state.UseLogin(_ => ++calls == 1 ? first.Task : second.Task);
            var oldLogin = state.Login();
            state.Call("Cancel");
            state.Call("Activate");
            var newLogin = state.Login();
            Assert.Equal(2, calls);
            first.SetResult(null);
            oldLogin.GetAwaiter().GetResult();
            state.Login().GetAwaiter().GetResult();
            Assert.Equal(2, calls);
            second.SetResult(null);
            newLogin.GetAwaiter().GetResult();
        });
        TestHarness.Test("B站登录：总开关或播放后端失效不能接受迟到账号", () =>
        {
            foreach (var external in new[] { false, true })
            {
                var state = fixture.Create();
                state.SeedAccount();
                var completion = new TaskCompletionSource<Receipt?>();
                state.UseLogin(_ => completion.Task);
                var login = state.Login();
                if (external) state.Settings.Settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
                else state.Settings.Settings.Danmaku.Enabled = false;
                completion.SetResult(("SESSDATA=placeholder-session", 42, "迟到账号"));
                login.GetAwaiter().GetResult();
                state.AssertOldAccount();
                Assert.Equal(0, state.Settings.Saves);
            }
        });
        TestHarness.Test("B站登录：真实来源下拉切走再切回会取消旧登录", () =>
        {
            var state = fixture.Create();
            state.SeedAccount();
            var completion = new TaskCompletionSource<Receipt?>();
            CancellationToken token = default;
            state.UseLogin(value => { token = value; return completion.Task; });
            var login = state.Login();
            var source = state.Rows().Single(row => state.Label(row) == "弹幕来源");
            var choices = state.Get<IEnumerable>(source, "Choices").Cast<object>().ToArray();
            var property = source.GetType().GetProperty("Selected")!;
            property.SetValue(source, choices.Single(choice => state.Get<string>(choice, "Label") == "弹弹play"));
            property.SetValue(source, choices.Single(choice => state.Get<string>(choice, "Label") == "哔哩哔哩"));
            Assert.True(token.IsCancellationRequested);
            var saved = state.Settings.Saves;
            completion.SetResult(("SESSDATA=placeholder-session", 42, "迟到账号"));
            login.GetAwaiter().GetResult();
            state.AssertOldAccount();
            Assert.Equal(saved, state.Settings.Saves);
        });
        TestHarness.Test("B站登录：加密异常保留原账号且反馈不含凭据", () =>
        {
            var state = fixture.Create();
            state.SeedAccount();
            state.UseProtector(new ThrowingProtector());
            state.UseLogin(_ => Task.FromResult<Receipt?>(("SESSDATA=placeholder-session", 42, "新账号")));
            state.Login().GetAwaiter().GetResult();
            state.AssertOldAccount();
            Assert.Equal(0, state.Settings.Saves);
            Assert.False(state.Notice().Contains("placeholder-session", StringComparison.Ordinal));
            Assert.True(state.Notice().Contains("原账号未更改", StringComparison.Ordinal));
        });
        TestHarness.Test("B站退出：本机删除、保存失败回滚、成功保留外观偏好", () =>
        {
            var state = fixture.Create();
            state.SeedAccount();
            state.Settings.Settings.Danmaku.Opacity = 53;
            state.Settings.SaveSucceeds = false;
            state.Call("SignOutBilibili");
            state.AssertOldAccount();
            state.Settings.SaveSucceeds = true;
            state.Call("SignOutBilibili");
            Assert.Equal("", state.Settings.Settings.Danmaku.ProtectedBilibiliCookie);
            Assert.Equal("", state.Settings.Settings.Danmaku.BilibiliUserName);
            Assert.Equal(0L, state.Settings.Settings.Danmaku.BilibiliUserId);
            Assert.Equal(53d, state.Settings.Settings.Danmaku.Opacity);
            Assert.True(state.Rows().Any(row => state.Label(row) == "登录 B 站"));
        });
        return TestHarness.Run();
    }

    private sealed class Fixture
    {
        internal Type Vm { get; }
        internal Type Prefs { get; }
        private readonly MethodInfo _validate;
        internal Fixture(string shellAssemblyPath, string sourceRoot)
        {
            var root = Path.GetFullPath(sourceRoot);
            var path = Path.GetFullPath(shellAssemblyPath);
            var release = Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar;
            if (!File.Exists(Path.Combine(root, "Momoka.sln")) || !path.StartsWith(release, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(path), "Momoka.dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("只加载本树当前 x64 Release Shell 程序集");
            var directory = Path.GetDirectoryName(path)!;
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            };
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            Vm = assembly.GetType("Momoka.Shell.ViewModels.SettingsViewModel", true)!;
            Prefs = assembly.GetType("Momoka.Shell.ShellPrefs", true)!;
            _validate = assembly.GetType("Momoka.Shell.Views.BilibiliLoginDialog", true)!
                .GetMethod("ValidateCandidateAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        }
        internal State Create() => new(this);
        internal Task<BilibiliAccount?> Validate(string candidate,
            Func<string, CancellationToken, Task<BilibiliAccount>> validate, Func<Task<string>> readCookie,
            Func<bool> live, CancellationToken token) =>
            (Task<BilibiliAccount?>)_validate.Invoke(null, [candidate, validate, readCookie, live, token])!;
    }

    private sealed class State
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly Fixture _fixture;
        private readonly object _vm;
        internal FakeSettings Settings { get; } = new();
        internal State(Fixture fixture)
        {
            _fixture = fixture;
            _vm = Activator.CreateInstance(fixture.Vm)!;
            Settings.Settings.Danmaku.Enabled = true;
            Settings.Settings.Danmaku.Source = DanmakuSourceMode.Bilibili;
            fixture.Vm.GetField("_settings", Members)!.SetValue(_vm, Settings);
            fixture.Vm.GetField("_secretProtector", Members)!.SetValue(_vm, new PlaceholderProtector());
            Call("Activate");
            Rebuild();
        }
        internal object? Call(string name, params object?[] args) =>
            (_fixture.Vm.GetMethod(name, Members) ?? throw new AssertionException("缺少入口 " + name)).Invoke(_vm, args);
        internal T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name, Members)!.GetValue(target)!;
        internal string Label(object row) => Get<string>(row, "Label");
        internal object[] Rows() => Get<IEnumerable>(_vm, "Sections").Cast<object>().SelectMany(card => Get<IEnumerable>(card, "Rows").Cast<object>()).ToArray();
        internal void Rebuild()
        {
            var sections = Get<IList>(_vm, "Sections");
            sections.Clear();
            foreach (var card in (IEnumerable)Call("DanmakuSections")!) sections.Add(card);
        }
        internal void UseLogin(Func<CancellationToken, Task<Receipt?>> login) => Call("UseBilibiliLogin", login);
        internal Task Login() => (Task)Call("LoginBilibiliAsync")!;
        internal string Notice() => Get<string>(_vm, "NoticeMessage");
        internal void UseProtector(ISecretProtector protector) => _fixture.Vm.GetField("_secretProtector", Members)!.SetValue(_vm, protector);
        internal void SeedAccount()
        {
            Settings.Settings.Danmaku.ProtectedBilibiliCookie = "old-protected";
            Settings.Settings.Danmaku.BilibiliUserId = 12;
            Settings.Settings.Danmaku.BilibiliUserName = "旧账号";
        }
        internal void AssertOldAccount()
        {
            Assert.Equal("old-protected", Settings.Settings.Danmaku.ProtectedBilibiliCookie);
            Assert.Equal(12L, Settings.Settings.Danmaku.BilibiliUserId);
            Assert.Equal("旧账号", Settings.Settings.Danmaku.BilibiliUserName);
        }
        internal Listener Listen() => new(_fixture.Prefs.GetEvent("DanmakuChanged")!);
    }

    private sealed class Listener
    {
        private readonly EventInfo _event;
        private readonly Action _handler;
        internal int Count { get; private set; }
        internal Listener(EventInfo eventInfo)
        {
            _event = eventInfo;
            _handler = () => Count++;
            _event.AddEventHandler(null, _handler);
        }
        internal void Stop() => _event.RemoveEventHandler(null, _handler);
    }

    private sealed class PlaceholderProtector : ISecretProtector
    {
        public string Protect(string plainText) => "protected:" + plainText;
        public string Unprotect(string cipherText) => cipherText["protected:".Length..];
    }

    private sealed class ThrowingProtector : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException(plainText);
        public string Unprotect(string cipherText) => throw new InvalidOperationException(cipherText);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public int PageSize => 50;
        internal int Saves { get; private set; }
        internal bool SaveSucceeds { get; set; } = true;
        public bool HasUnsavedChanges { get; private set; }
        public event EventHandler? SaveStateChanged { add { } remove { } }
        public bool TrySave() { Saves++; HasUnsavedChanges = !SaveSucceeds; return SaveSucceeds; }
        public void Save() => throw new InvalidOperationException("只允许假 TrySave");
        public ServerProfile ResolveServer(string url) => throw new InvalidOperationException("禁止登录媒体服务器");
        public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new InvalidOperationException("禁止登录媒体服务器");
        public void ExportPreferences(string path) => throw new InvalidOperationException("禁止配置 IO");
        public AppSettings ReadBackup(string path) => throw new InvalidOperationException("禁止配置 IO");
    }
}
