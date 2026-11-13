using System.Net;
using System.Text.Json;
using Momoka.Emby;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("从搜索继续仅供手动：播种身份与前六步明确单列，后半仍须真实输入", () =>
        {
            KeyboardJourneyProbeRules.RequireResumeMode(false, false);
            KeyboardJourneyProbeRules.RequireResumeMode(true, false);
            KeyboardJourneyProbeRules.RequireResumeMode(true, true);
            Assert.Throws<InvalidOperationException>(() => KeyboardJourneyProbeRules.RequireResumeMode(false, true));
            Assert.Contains("前六步上轮已验证", KeyboardJourneyProbeRules.ManualInstruction("search-ready", true));
            Assert.Contains("本轮账号为匿名播种", KeyboardJourneyProbeRules.ManualInstruction("search-ready", true));
            Assert.Contains("第7步", KeyboardJourneyProbeRules.ManualInstruction("search-ready", true));
            Assert.False(KeyboardJourneyProbeRules.ManualInstruction("search-ready", false).Contains("匿名播种", StringComparison.Ordinal));
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "src/Momoka.Shell/ShellNavigationProbe.KeyboardJourney.cs"));
            Assert.Contains("KeyboardJourneyProbeRules.RequireResumeMode(manual, resumeFromAnonymousSeed);", source);
            RequireSeedReceiptFields(source);
            Assert.Contains("Read(\"anonymous-first-six-seeded\"", source);
            Assert.Contains("await fixture.Session.SignInAsync", source);
            Assert.Contains("await Stage(\"search-ready\"", source);
            Assert.Contains("KeyboardJourneyProbeRules.RequireFinal", source);
            Assert.Contains("if (minimumKeys > 0) KeyboardJourneyProbeRules.RequireKeyProgress(before, keys);", source);
        });
        Test("从结果继续仅播种匿名查询，真实后半按键与回执明确范围", () =>
        {
            Assert.Contains("本轮从结果继续", KeyboardJourneyProbeRules.ManualInstruction("result-ready", false, true));
            Assert.Contains("搜索结果为匿名播种", KeyboardJourneyProbeRules.ManualInstruction("result-ready", false, true));
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "src/Momoka.Shell/ShellNavigationProbe.KeyboardJourney.cs"));
            Assert.Contains("await searchPage.SubmitSearchAsync(KeyboardJourneyProbeServer.Keyword);", source);
            Assert.Contains("Read(\"anonymous-search-seeded\"", source);
            Assert.Contains("resumedFromResult = resumeFromResult", source);
            Assert.Contains("seededSearch = resumeFromResult", source);
            Assert.Contains("await Stage(\"result-ready\"", source);
            Assert.Contains("KeyboardJourneyProbeRules.RequireFinal", source);
        });
        Test("从搜索继续回执接线忽略格式换行但拒绝字段或值漂移", () =>
        {
            var expressions = "resumedFromSearch = resumeFromSearch, resumedFromResult = resumeFromResult, seededIdentity = resumeFromAnonymousSeed, seededSearch = resumeFromResult";
            RequireSeedReceiptFields(expressions + "\n" + expressions);
            RequireSeedReceiptFields(expressions.Replace(", ", ",\n                ") + "\n" + expressions.Replace(", ", ",\n                "));
            foreach (var invalid in new[] { expressions, (expressions + "\n" + expressions).Replace("seededIdentity", "otherIdentity", StringComparison.Ordinal),
                (expressions + "\n" + expressions).Replace("= resumeFromSearch", "= false", StringComparison.Ordinal),
                (expressions + "\n" + expressions).Replace("= resumeFromAnonymousSeed", "= resumeFromSearch", StringComparison.Ordinal) })
                Assert.Throws<AssertionException>(() => RequireSeedReceiptFields(invalid));
        });
        Test("手动取消单独记录且重新抛出，不能冒称完整通过", () =>
        {
            Assert.Contains("取消不是通过", KeyboardJourneyProbeRules.ManualInstruction("cancelled"));
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "src/Momoka.Shell/ShellNavigationProbe.KeyboardJourney.cs"));
            var start = source.IndexOf("catch (OperationCanceledException) when (cancelled.IsCancellationRequested)", StringComparison.Ordinal);
            var end = source.IndexOf("        finally", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            Assert.Contains("WriteStage(\"cancelled\"", source[start..end]);
            Assert.Contains("throw;", source[start..end]);
        });
        Test("原生搜索名称：媒体库与设置外层有本地化名并保留原生模板传给输入层", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var root = directory!.FullName;
            System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var resources = System.Xml.Linq.XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Strings/zh-Hans/Resources.resw"));
            foreach (var (file, name, uid, label) in new[] { ("LibraryPage.xaml", "SearchInput", "LibrarySearchBox", "搜索媒体库"),
                ("SettingsPage.xaml", "SettingsSearchBox", "SettingsSearchBox", "搜索设置") })
            {
                var page = System.Xml.Linq.XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views", file));
                var control = page.Descendants().Single(node => (string?)node.Attribute(x + "Name") == name);
                Assert.Equal("AutoSuggestBox", control.Name.LocalName);
                Assert.Equal(uid, (string?)control.Attribute(x + "Uid"));
                Assert.Equal(label, (string?)control.Attribute("AutomationProperties.Name"));
                Assert.False(control.Attributes().Any(attribute => attribute.Name.LocalName == "Template"));
                var key = uid + ".[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name";
                Assert.Equal(label, resources.Descendants("data").Single(data => (string?)data.Attribute("name") == key).Element("value")!.Value);
            }
            var probe = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellNavigationProbe.KeyboardJourney.cs"));
            Assert.Contains("KeyboardJourneyProbeRules.RequireName(inputPeer.GetName(), peer.GetName());", probe);
            Assert.Contains("box.Name == \"TextBox\"", probe);
        });
        Test("手动键盘提示：12步中文动作与5分钟时限，普通模式仍60秒", () =>
        {
            Assert.Equal(300_000, KeyboardJourneyProbeRules.ManualStageMilliseconds);
            Assert.Equal(60_000, KeyboardJourneyProbeRules.AutomaticStageMilliseconds);
            var stages = new[] { "address-ready", "username-ready", "password-ready", "account-ready", "source-ready", "search-entry-ready",
                "search-ready", "result-ready", "settings-entry-ready", "settings-ready", "settings-close-ready", "back-ready" };
            foreach (var stage in stages)
            {
                var text = KeyboardJourneyProbeRules.ManualInstruction(stage);
                Assert.Contains("第", text);
                Assert.Contains("步", text);
                Assert.True(text.Any(ch => ch is >= '\u4e00' and <= '\u9fff'));
            }
            Assert.Contains(KeyboardJourneyProbeServer.Address, KeyboardJourneyProbeRules.ManualInstruction("address-ready"));
            Assert.Contains(KeyboardJourneyProbeServer.Username, KeyboardJourneyProbeRules.ManualInstruction("username-ready"));
            Assert.Contains(KeyboardJourneyProbeServer.Password, KeyboardJourneyProbeRules.ManualInstruction("password-ready"));
            Assert.Contains(KeyboardJourneyProbeServer.Keyword, KeyboardJourneyProbeRules.ManualInstruction("search-ready"));
        });
        Test("手动键盘提示：独立提示行/取消入口/状态播报不覆盖产品按钮", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var root = directory!.FullName;
            var prompt = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellNavigationProbe.KeyboardManualPrompt.cs"));
            var journey = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellNavigationProbe.KeyboardJourney.cs"));
            var inspect = File.ReadAllText(Path.Combine(root, "src/Momoka.Shell/ShellNavigationProbe.Inspect.cs"));
            Assert.Contains("KeyboardManualInstruction", prompt);
            Assert.Contains("AutomationLiveSetting.Polite", prompt);
            Assert.Contains("KeyboardManualCancel", prompt);
            Assert.Contains("cancel();", prompt);
            Assert.Contains("Grid.SetRow(shell, 1);", journey);
            Assert.Contains("cancelled.Token.ThrowIfCancellationRequested();", journey);
            Assert.Contains("Task.Delay(50, cancelled.Token)", journey);
            Assert.Contains("settingsWitnessHost?.RemoveHandler", journey);
            Assert.Contains("InspectKeyboardJourneyManual", inspect);
            Assert.Contains("KeyboardJourneyAsync(manual: true)", inspect);
            Assert.Contains("inspectKeyboardStarted.TrySetResult();", inspect);
            Assert.Contains("Task.Delay(TimeSpan.FromMinutes(20))", inspect);
        });
        Test("键盘流程菜单按键独立挂Flyout分支并在finally解绑，来源仍要求按键增量", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "src/Momoka.Shell/ShellNavigationProbe.KeyboardJourney.cs"));
            Assert.Contains("accountMenuBody.AddHandler(UIElement.PreviewKeyDownEvent, witness, handledEventsToo: true);", source);
            Assert.Contains("accountMenuBody.RemoveHandler(UIElement.PreviewKeyDownEvent, witness);", source);
            var start = source.IndexOf("await Stage(\"source-ready\"", StringComparison.Ordinal);
            var end = source.IndexOf("Require(!accountMenu.IsOpen", start, StringComparison.Ordinal);
            Assert.False(source[start..end].Contains("minimumKeys: 0", StringComparison.Ordinal));
        });
        Test("键盘来源阶段等待自己的账号菜单关闭，不把别的提示气泡算成账号浮层", () =>
        {
            Assert.True(KeyboardJourneyProbeRules.SourceReady(true, true, false, false));
            Assert.False(KeyboardJourneyProbeRules.SourceReady(true, true, true, false));
            Assert.False(KeyboardJourneyProbeRules.SourceReady(true, true, false, true));
            Assert.False(KeyboardJourneyProbeRules.SourceReady(false, true, false, false));
            Assert.False(KeyboardJourneyProbeRules.SourceReady(true, false, false, false));
        });
        InfoTableProbeTests.Register();
        Test("键盘结果定位按子卡片身份，不依赖原生容器DataContext", () =>
        {
            var correct = new object();
            var wrong = new object();
            var cards = new Dictionary<object, string?> { [correct] = KeyboardJourneyProbeServer.MovieId, [wrong] = "other" };
            Assert.True(ReferenceEquals(correct, KeyboardJourneyProbeRules.FindResult([wrong, correct], container => cards[container], KeyboardJourneyProbeServer.MovieId)));
            Assert.Null(KeyboardJourneyProbeRules.FindResult([wrong], container => cards[container], KeyboardJourneyProbeServer.MovieId));
            Assert.Null(KeyboardJourneyProbeRules.FindResult([correct], _ => null, KeyboardJourneyProbeServer.MovieId));
        });
        foreach (var host in new[] { "keyboard-a.invalid", "keyboard-b.invalid" })
            Test("键盘匿名源公开信息和目录 " + host, () => TestHarnessRun(async () =>
            {
                var server = new KeyboardJourneyProbeServer();
                using var info = await server.ReplyAsync(new(HttpMethod.Get, "https://" + host + "/emby/System/Info/Public"), default);
                var publicInfo = JsonSerializer.Deserialize<PublicSystemInfo>(await info.Content.ReadAsStringAsync())!;
                Assert.Equal(host, publicInfo.Id);
                Assert.Equal(MediaServerKind.Emby, publicInfo.ServerKind);
                using var views = await server.ReplyAsync(new(HttpMethod.Get, "https://" + host + "/emby/Users/keyboard-user/Views"), default);
                Assert.Equal("keyboard-library", JsonSerializer.Deserialize<ItemsResult>(await views.Content.ReadAsStringAsync())!.Items.Single().Id);
            }));
        Test("键盘匿名源只有正确人工账号密码能够登录", () => TestHarnessRun(async () =>
        {
            var server = new KeyboardJourneyProbeServer();
            using var correct = Authentication(KeyboardJourneyProbeServer.Username, KeyboardJourneyProbeServer.Password);
            using var response = await server.ReplyAsync(correct, default);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var auth = JsonSerializer.Deserialize<AuthenticationResult>(await response.Content.ReadAsStringAsync())!;
            Assert.Equal(KeyboardJourneyProbeServer.Username, auth.User!.Id);
            Assert.False(auth.User.Policy!.IsAdministrator);
            using var wrong = Authentication(KeyboardJourneyProbeServer.Username, "wrong");
            using var rejected = await server.ReplyAsync(wrong, default);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Equal(2, server.AuthenticationPosts);
        }));
        Test("键盘搜索只从第二匿名来源返回固定影片，详情不包含媒体URL", () => TestHarnessRun(async () =>
        {
            var server = new KeyboardJourneyProbeServer();
            foreach (var (host, count) in new[] { ("keyboard-a.invalid", 0), ("keyboard-b.invalid", 1) })
            {
                using var response = await server.ReplyAsync(new(HttpMethod.Get, "https://" + host + "/emby/Users/keyboard-user/Items?SearchTerm=keyboard-query"), default);
                var page = JsonSerializer.Deserialize<ItemsResult>(await response.Content.ReadAsStringAsync())!;
                Assert.Equal(count, page.TotalRecordCount);
                Assert.Equal(count, page.Items.Count);
            }
            using var detail = await server.ReplyAsync(new(HttpMethod.Get, "https://keyboard-b.invalid/emby/Users/keyboard-user/Items/keyboard-movie"), default);
            var item = JsonSerializer.Deserialize<EmbyItem>(await detail.Content.ReadAsStringAsync())!;
            Assert.Equal(KeyboardJourneyProbeServer.MovieId, item.Id);
            Assert.True(item.MediaSources.All(source => string.IsNullOrEmpty(source.Path)));
        }));
        Test("键盘匿名源拒绝真实目标、未知写入和取消", () => TestHarnessRun(async () =>
        {
            var server = new KeyboardJourneyProbeServer();
            await RejectAsync(() => server.ReplyAsync(new(HttpMethod.Get, "https://example.com/Items"), default));
            await RejectAsync(() => server.ReplyAsync(new(HttpMethod.Post, "https://keyboard-a.invalid/Items/a"), default));
            await RejectAsync(() => server.ReplyAsync(new(HttpMethod.Get, "https://keyboard-a.invalid/unknown"), default));
            Assert.Equal(1, server.RejectedWrites);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try { using var response = await server.ReplyAsync(new(HttpMethod.Get, KeyboardJourneyProbeServer.Address + "/Items"), canceled.Token); throw new AssertionException("取消仍执行"); }
            catch (OperationCanceledException) { }
        }));
        Test("键盘流程读数拒绝虚假输入、缺失焦点/名称/角色和错误字段名", () =>
        {
            KeyboardJourneyProbeRules.RequireFocus("服务器地址", "Edit", true);
            KeyboardJourneyProbeRules.RequireName("服务器地址", "服务器地址");
            KeyboardJourneyProbeRules.RequireKeyProgress(10, 11);
            foreach (var action in new Action[]
            {
                () => KeyboardJourneyProbeRules.RequireFocus("", "Edit", true),
                () => KeyboardJourneyProbeRules.RequireFocus("服务器地址", "", true),
                () => KeyboardJourneyProbeRules.RequireFocus("服务器地址", "Edit", false),
                () => KeyboardJourneyProbeRules.RequireName("错误名称夹具", "服务器地址"),
                () => KeyboardJourneyProbeRules.RequireKeyProgress(10, 10)
            }) Reject(action);
        });
        Test("键盘最终回执拒绝丢查询、错来源、真播放和多登录/写入", () =>
        {
            KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, true, 0, 1, 0);
            foreach (var action in new Action[]
            {
                () => KeyboardJourneyProbeRules.RequireFinal("", true, 0, 1, 0),
                () => KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, false, 0, 1, 0),
                () => KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, true, 1, 1, 0),
                () => KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, true, 0, 2, 0),
                () => KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, true, 0, 1, 1)
            }) Reject(action);
        });
        return Run();
    }

    private static HttpRequestMessage Authentication(string user, string password) => new(HttpMethod.Post, "https://keyboard-a.invalid/emby/Users/AuthenticateByName")
    { Content = new StringContent(JsonSerializer.Serialize(new { UserName = user, Pw = password })) };
    private static void RequireSeedReceiptFields(string source)
    {
        foreach (var (field, value) in new[] { ("resumedFromSearch", "resumeFromSearch"), ("resumedFromResult", "resumeFromResult"),
            ("seededIdentity", "resumeFromAnonymousSeed"), ("seededSearch", "resumeFromResult") })
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, @"\b" + field + @"\s*=\s*" + value + @"\b").Count,
                "阶段与最终读数必须分别保留来源播种字段和原值：" + field);
    }
    private static void TestHarnessRun(Func<Task> action)
    {
        var running = action();
        while (!running.IsCompleted) Thread.Sleep(1);
        if (running.IsFaulted) throw running.Exception!.InnerException!;
        if (running.IsCanceled) throw new OperationCanceledException();
    }
    private static void Reject(Action action)
    {
        try { action(); throw new AssertionException("接受错误读数"); }
        catch (InvalidOperationException) { }
    }
    private static async Task RejectAsync(Func<Task<HttpResponseMessage>> action)
    {
        try { using var response = await action(); throw new AssertionException("接受禁止请求"); }
        catch (InvalidOperationException) { }
    }
}
