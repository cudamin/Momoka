using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class IdentityRegressionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    public static void Register()
    {
        RegisterPasswords();
        RegisterFnNasPasswordChanges();
        RegisterSessionChanges();
        RegisterScopes();
        RegisterMultipleSources();
        RegisterAggregateMode();
        RegisterCapabilities();
        RegisterMoviePilot();
        RegisterDownloads();
        HttpRedirectTests.Register();
    }

    private static void Case(string name, Func<Task> run) =>
        Test(name, () => run().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());

    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Fails<T>(Task task) where T : Exception
    {
        try
        {
            await task;
        }
        catch (T)
        {
            return;
        }
        throw new AssertionException($"期望 {typeof(T).Name}");
    }

    private static void RegisterPasswords()
    {
        Case("身份回归：同名账号的保存密码只按当前规范化服务器查找", async () =>
        {
            using var fixture = new SessionFixture();
            var a = fixture.A;
            var b = fixture.B;
            a.Account.Username = b.Account.Username = "shared";
            fixture.Settings.Remember(a.Server, a.Account);
            var address = b.Server.Url + "/emby/";
            var saved = EmbySignInIdentity.SavedAccount(fixture.Settings.Servers, address, " SHARED ");
            Assert.True(ReferenceEquals(b.Account, saved));
            var password = fixture.Vault.GetPassword(saved!);
            await fixture.Session.Gateway.AuthenticateAsync(EmbyServerAddress.Normalize(address), "shared", password, None);
            var sent = fixture.Transport.Sent.Single(s => s.IsAuthentication);
            Assert.Equal("b.invalid", sent.Url.Host);
            using var body = JsonDocument.Parse(sent.Body);
            Assert.Equal("password-b", body.RootElement.GetProperty("Pw").GetString());
            Assert.DoesNotContain("password-a", sent.Body);

            b.Server.Accounts.Clear();
            Assert.Null(EmbySignInIdentity.SavedAccount(fixture.Settings.Servers, address, "shared"));
        });

        Case("身份回归：无密码账号的认证正文明确为空，忽略残留密码", async () =>
        {
            using var fixture = new SessionFixture();
            var address = fixture.B.Server.Url;
            var apiBase = EmbyServerAddress.Normalize(address);
            var user = new EmbyUser { Name = "b", HasPassword = false };
            var passwordless = EmbySignInIdentity.IsPasswordless(address, apiBase, user.Name, user);
            Assert.True(passwordless);
            var password = EmbySignInIdentity.PasswordFor(address, "b", "leftover", address, "b", passwordless);
            await fixture.Session.Gateway.AuthenticateAsync(apiBase, user.Name, password, None);
            using var body = JsonDocument.Parse(fixture.Transport.Sent.Single(s => s.IsAuthentication).Body);
            Assert.Equal("", body.RootElement.GetProperty("Pw").GetString());
            Assert.False(EmbySignInIdentity.IsPasswordless(fixture.A.Server.Url, apiBase, "b", user));
            Assert.False(EmbySignInIdentity.IsPasswordless(address, apiBase, "other", user));
        });

        Test("身份回归：地址或账号变更丢弃旧来源密码，等价地址不丢", () =>
        {
            const string address = "https://a.invalid/media";
            Assert.Equal("", EmbySignInIdentity.PasswordFor("https://b.invalid/media", "a", "old", address, "a", false));
            Assert.Equal("", EmbySignInIdentity.PasswordFor(address, "b", "old", address, "a", false));
            Assert.Equal("", EmbySignInIdentity.PasswordFor("", "a", "old", address, "a", false));
            Assert.Equal("old", EmbySignInIdentity.PasswordFor("https://A.invalid:443/media/emby/", " A ", "old", address, "a", false));
            Assert.False(EmbySignInIdentity.SameAddress(address, "https://a.invalid/Media"));
        });
    }

    private static void RegisterFnNasPasswordChanges()
    {
        foreach (var remember in new[] { false, true })
            Case($"飞牛改密：提交后页面取消仍更新 A 令牌并保留记住密码选择 {remember}", async () =>
            {
                using var fixture = new SessionFixture();
                await RestoreFnNasAsync(fixture);
                var source = fixture.Session.Capture();
                await fixture.Restore(fixture.B);
                fixture.Vault.SetPassword(fixture.A.Account, "password-a", remember);
                var entered = Signal<bool>();
                var reply = Signal<HttpResponseMessage>();
                fixture.Transport.Reply = (sent, _) =>
                {
                    if (!sent.Url.AbsolutePath.EndsWith("/user/passwd", StringComparison.Ordinal))
                        return Task.FromResult(FnNasStandard(sent));
                    entered.TrySetResult(true);
                    return reply.Task;
                };
                using var page = new CancellationTokenSource();
                var changing = fixture.Session.ChangeFnNasPasswordAsync(source, "new-password-a", page.Token);
                await entered.Task;
                page.Cancel();
                reply.SetResult(Json("{\"code\":0,\"data\":\"new-token-a\"}"));
                await changing;
                Assert.Equal("new-token-a", fixture.Vault.GetAccessToken(fixture.A.Account));
                Assert.Equal(remember ? "new-password-a" : "", fixture.Vault.GetPassword(fixture.A.Account));
                Assert.Equal(remember, fixture.A.Account.RememberPassword);
                Assert.Equal("new-token-a", (await source.ExecuteAsync((client, _) => Task.FromResult(client.Connection), None)).AccessToken);
                Assert.Equal("token-b", fixture.Vault.GetAccessToken(fixture.B.Account));
                Assert.Equal("password-b", fixture.Vault.GetPassword(fixture.B.Account));
                Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
                Assert.Equal(1, fixture.Transport.Sent.Count(sent => sent.Url.AbsolutePath.EndsWith("/user/passwd", StringComparison.Ordinal)));
            });

        foreach (var change in new[] { "signout", "delete", "signin" })
            Case($"飞牛改密：提交期间 {change} 后迟到成功不复活旧来源或覆盖新凭据", async () =>
            {
                using var fixture = new SessionFixture();
                await RestoreFnNasAsync(fixture);
                var source = fixture.Session.Capture();
                var entered = Signal<bool>();
                var reply = Signal<HttpResponseMessage>();
                fixture.Transport.Reply = (sent, _) =>
                {
                    if (!sent.Url.AbsolutePath.EndsWith("/user/passwd", StringComparison.Ordinal))
                        return Task.FromResult(FnNasStandard(sent));
                    entered.TrySetResult(true);
                    return reply.Task;
                };
                var changing = fixture.Session.ChangeFnNasPasswordAsync(source, "late-password", None);
                await entered.Task;
                if (change == "signin")
                    await fixture.Session.SignInAsync(fixture.A.Server, fixture.A.Account, "explicit-password", "a", true, None);
                else
                {
                    fixture.Session.SignOut(fixture.A.Server, fixture.A.Account);
                    if (change == "delete") fixture.A.Server.Accounts.Remove(fixture.A.Account);
                }
                var savedPassword = fixture.A.Account.ProtectedPassword;
                var savedToken = fixture.A.Account.ProtectedAccessToken;
                reply.SetResult(Json("{\"code\":0,\"data\":\"late-token\"}"));
                await Fails<OperationCanceledException>(changing);
                Assert.Equal(savedPassword, fixture.A.Account.ProtectedPassword);
                Assert.Equal(savedToken, fixture.A.Account.ProtectedAccessToken);
                Assert.Equal(change == "signin", source.IsAvailable);
            });

        Case("飞牛改密：排队／尚未发出时页面取消不提交", async () =>
        {
            using var fixture = new SessionFixture();
            await RestoreFnNasAsync(fixture);
            var source = fixture.Session.Capture();
            await source.IdentityState.PasswordChanges.WaitAsync();
            using var page = new CancellationTokenSource();
            var changing = fixture.Session.ChangeFnNasPasswordAsync(source, "not-sent-password", page.Token);
            page.Cancel();
            await Fails<OperationCanceledException>(changing);
            source.IdentityState.PasswordChanges.Release();
            Assert.False(fixture.Transport.Sent.Any(sent => sent.Url.AbsolutePath.EndsWith("/user/passwd", StringComparison.Ordinal)));
            Assert.Equal("token-a", fixture.Vault.GetAccessToken(fixture.A.Account));
        });

        Case("飞牛改密：服务端拒绝不更新本地凭据", async () =>
        {
            using var fixture = new SessionFixture();
            await RestoreFnNasAsync(fixture);
            var source = fixture.Session.Capture();
            fixture.Transport.Reply = (sent, _) => Task.FromResult(sent.Url.AbsolutePath.EndsWith("/user/passwd", StringComparison.Ordinal)
                ? Json("{\"code\":-15}") : FnNasStandard(sent));
            await Fails<EmbyApiException>(fixture.Session.ChangeFnNasPasswordAsync(source, "rejected-password", None));
            Assert.Equal("token-a", fixture.Vault.GetAccessToken(fixture.A.Account));
            Assert.Equal("password-a", fixture.Vault.GetPassword(fixture.A.Account));
        });
    }

    private static async Task RestoreFnNasAsync(SessionFixture fixture)
    {
        fixture.A.Server.ServerKind = MediaServerKind.FnNas;
        fixture.A.Server.Url = "https://a.invalid/v/";
        fixture.Transport.Reply = (sent, _) => Task.FromResult(FnNasStandard(sent));
        await fixture.Restore(fixture.A);
    }

    private static HttpResponseMessage FnNasStandard(Sent sent)
    {
        if (sent.Url.Host != "a.invalid") return Standard(sent);
        var path = sent.Url.AbsolutePath;
        if (path.EndsWith("System/Info/Public", StringComparison.Ordinal)) return Json("", HttpStatusCode.NotFound);
        if (path.EndsWith("/sys/version", StringComparison.Ordinal)) return Json("{\"code\":0,\"data\":{\"version\":\"0.9.8\",\"mediasrvVersion\":\"0.9.8\"}}");
        if (path.EndsWith("/sys/config", StringComparison.Ordinal)) return Json("{\"code\":0,\"data\":{\"server_guid\":\"fn-a\",\"server_name\":\"a.invalid\"}}");
        if (path.EndsWith("/user/info", StringComparison.Ordinal)) return Json("{\"code\":0,\"data\":{\"guid\":\"a\",\"username\":\"a\",\"is_admin\":1}}");
        if (path.EndsWith("/mediadb/list", StringComparison.Ordinal)) return Json("{\"code\":0,\"data\":[]}");
        if (path.EndsWith("/user/loginByPassword", StringComparison.Ordinal)) return Json("{\"code\":0,\"data\":{\"token\":\"explicit-token\"}}");
        return Json("{\"code\":0,\"data\":{}}");
    }

    private static void RegisterSessionChanges()
    {
        foreach (var fault in new[] { "network", "timeout", "unavailable", "forbidden" })
            Case($"重登故障：{fault} 不注销来源、不清凭据，恢复网络后仍可重试", async () =>
            {
                using var fixture = new SessionFixture();
                await fixture.Restore(fixture.A);
                var source = fixture.Session.Capture();
                fixture.Transport.Reply = (sent, _) =>
                {
                    if (sent.IsWrite) return Task.FromResult(Json("", HttpStatusCode.Unauthorized));
                    if (!sent.IsAuthentication) return Task.FromResult(Standard(sent));
                    return fault switch
                    {
                        "network" => Task.FromException<HttpResponseMessage>(new HttpRequestException("synthetic network")),
                        "timeout" => Task.FromException<HttpResponseMessage>(new TaskCanceledException("synthetic timeout")),
                        "forbidden" => Task.FromResult(Json("", HttpStatusCode.Forbidden)),
                        _ => Task.FromResult(Json("", HttpStatusCode.ServiceUnavailable))
                    };
                };
                await Fails<EmbyApiException>(source.ExecuteAsync((client, ct) => client.MarkPlayedAsync("item", ct), None));
                Assert.True(source.IsAvailable && source.IsCurrent);
                Assert.Equal("token-a", fixture.Vault.GetAccessToken(fixture.A.Account));
                Assert.Equal("password-a", fixture.Vault.GetPassword(fixture.A.Account));
                Assert.Equal(0, fixture.SignedOut);
                fixture.Transport.Reply = (sent, _) => Task.FromResult(sent.IsWrite && sent.Token == "token-a"
                    ? Json("", HttpStatusCode.Unauthorized) : Standard(sent));
                await source.ExecuteAsync((client, ct) => client.MarkPlayedAsync("item", ct), None);
                Assert.Equal("fresh-a", fixture.Vault.GetAccessToken(fixture.A.Account));
                Assert.Equal(0, fixture.SignedOut);
            });

        foreach (var sameServer in new[] { false, true })
            foreach (var outcome in new[] { "success", "rejected", "timeout" })
                Case($"身份回归：A 重登 {outcome} 迟到，直接恢复{(sameServer ? "同服 B 账号" : "B 服务器")}不受影响", async () =>
                {
                    using var fixture = new SessionFixture(sameServer);
                    var entered = Signal<bool>();
                    var reply = Signal<HttpResponseMessage>();
                    fixture.Transport.Reply = (sent, _) =>
                    {
                        if (sent.IsAuthentication && sent.Username == "a")
                        {
                            entered.TrySetResult(true);
                            return reply.Task;
                        }
                        return Task.FromResult(sent.IsWrite ? Json("", HttpStatusCode.Unauthorized) : Standard(sent));
                    };
                    await fixture.Restore(fixture.A);
                    var old = fixture.Session.ExecuteAsync((client, token) => client.MarkPlayedAsync("old-item", token), None);
                    await entered.Task;
                    await fixture.Restore(fixture.B);
                    var bToken = fixture.B.Account.ProtectedAccessToken;
                    if (outcome == "timeout") reply.SetException(new TaskCanceledException("synthetic timeout"));
                    else reply.SetResult(outcome == "success" ? SignedIn("a", "fresh-a") : Json("", HttpStatusCode.Unauthorized));
                    if (outcome == "timeout") await Fails<EmbyUnreachableException>(old);
                    else await Fails<EmbyTokenExpiredException>(old);

                    Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
                    Assert.Equal(bToken, fixture.B.Account.ProtectedAccessToken);
                    Assert.Equal(outcome == "success" ? "fresh-a" : outcome == "rejected" ? "" : "token-a",
                        fixture.Vault.GetAccessToken(fixture.A.Account), "A 的恢复结果只更新 A；仅明确拒绝才移除 A 的令牌");
                    Assert.Equal(fixture.B.Server.Id, fixture.Settings.LastServerId);
                    Assert.Equal(fixture.B.Account.Id, fixture.Settings.LastAccountId);
                    Assert.Equal(0, fixture.SignedOut);
                    Assert.Equal(outcome == "success" ? 2 : 1, fixture.Transport.Sent.Count(s => s.IsWrite));
                    Assert.True(fixture.Transport.Sent.Where(s => s.IsWrite).All(s => s.Url.AbsolutePath.Contains("/Users/a/", StringComparison.Ordinal)));
                });

        foreach (var success in new[] { true, false })
            Case($"身份回归：B 显式登录后 A 迟到{(success ? "成功" : "失败")}不能覆盖或注销", async () =>
            {
                using var fixture = new SessionFixture();
                var entered = Signal<bool>();
                var reply = Signal<HttpResponseMessage>();
                fixture.Transport.Reply = (sent, _) =>
                {
                    if (sent.IsAuthentication && sent.Username == "a")
                    {
                        entered.TrySetResult(true);
                        return reply.Task;
                    }
                    return Task.FromResult(sent.IsWrite ? Json("", HttpStatusCode.Unauthorized) : Standard(sent));
                };
                await fixture.Restore(fixture.A);
                var old = fixture.Session.ExecuteAsync((client, token) => client.MarkPlayedAsync("old", token), None);
                await entered.Task;
                await fixture.Session.SignInAsync(fixture.B.Server, fixture.B.Account, "password-b", "b", true, None);
                reply.SetResult(success ? SignedIn("a", "late-a") : Json("", HttpStatusCode.Unauthorized));
                await Fails<EmbyTokenExpiredException>(old);
                Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
                Assert.Equal(0, fixture.SignedOut);
                Assert.Equal(success ? 2 : 1, fixture.Transport.Sent.Count(s => s.IsWrite));
                Assert.True(fixture.Transport.Sent.Where(s => s.IsWrite).All(s => s.Url.AbsolutePath.Contains("/Users/a/", StringComparison.Ordinal)));
            });

        Case("身份回归：B 恢复尚未完成时，A 的旧恢复失败不能撤销 B 的选择", async () =>
        {
            using var fixture = new SessionFixture();
            var reauthEntered = Signal<bool>();
            var reauthReply = Signal<HttpResponseMessage>();
            var bEntered = Signal<bool>();
            var bReply = Signal<HttpResponseMessage>();
            fixture.Transport.Reply = (sent, _) =>
            {
                if (sent.IsAuthentication)
                {
                    reauthEntered.TrySetResult(true);
                    return reauthReply.Task;
                }
                if (sent.IsViews && sent.Token == "token-b")
                {
                    bEntered.TrySetResult(true);
                    return bReply.Task;
                }
                return Task.FromResult(sent.IsWrite ? Json("", HttpStatusCode.Unauthorized) : Standard(sent));
            };
            await fixture.Restore(fixture.A);
            var old = fixture.Session.ExecuteAsync((client, token) => client.MarkPlayedAsync("old", token), None);
            await reauthEntered.Task;
            var b = fixture.Session.TryRestoreAsync(fixture.B.Server, fixture.B.Account, None);
            await bEntered.Task;
            reauthReply.SetResult(Json("", HttpStatusCode.Unauthorized));
            await Fails<EmbyTokenExpiredException>(old);
            Assert.Equal(0, fixture.SignedOut);
            bReply.SetResult(Views("b"));
            Assert.True(await b);
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
            Assert.Equal(1, fixture.Transport.Sent.Count(s => s.IsWrite));
        });

        foreach (var unauthorized in new[] { false, true })
            Case($"多来源身份：两次恢复逆序返回，A {(unauthorized ? "过期后重登" : "成功")}保留且默认仍是 B", async () =>
            {
                using var fixture = new SessionFixture();
                var entered = Signal<bool>();
                var reply = Signal<HttpResponseMessage>();
                fixture.Transport.Reply = (sent, _) =>
                {
                    if (sent.IsViews && sent.Token == "token-a")
                    {
                        entered.TrySetResult(true);
                        return reply.Task;
                    }
                    return Task.FromResult(Standard(sent));
                };
                var a = fixture.Session.TryRestoreAsync(fixture.A.Server, fixture.A.Account, None);
                await entered.Task;
                await fixture.Restore(fixture.B);
                reply.SetResult(unauthorized ? Json("", HttpStatusCode.Unauthorized) : Views("a"));
                Assert.True(await a);
                Assert.Equal(unauthorized ? "fresh-a" : "token-a", fixture.Vault.GetAccessToken(fixture.A.Account));
                Assert.Equal("b-view", fixture.Session.TakeRestoredViews()![0].Id);
                Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
                Assert.Equal(unauthorized ? 1 : 0, fixture.Transport.Sent.Count(s => s.IsAuthentication));
                Assert.Equal(2, fixture.Session.Sources.Count);
            });

        Case("多来源身份：显式登录逆序完成，两者在线但默认保留最后选择", async () =>
        {
            using var fixture = new SessionFixture();
            var entered = Signal<bool>();
            var reply = Signal<HttpResponseMessage>();
            fixture.Transport.Reply = (sent, _) =>
            {
                if (sent.IsAuthentication && sent.Username == "a")
                {
                    entered.TrySetResult(true);
                    return reply.Task;
                }
                return Task.FromResult(Standard(sent));
            };
            var a = fixture.Session.SignInAsync(fixture.A.Server, fixture.A.Account, "new-a", "a", true, None);
            await entered.Task;
            await fixture.Session.SignInAsync(fixture.B.Server, fixture.B.Account, "new-b", "b", true, None);
            reply.SetResult(SignedIn("a", "late-a"));
            await a;
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
            Assert.Equal("late-a", fixture.Vault.GetAccessToken(fixture.A.Account));
            Assert.Equal("new-a", fixture.Vault.GetPassword(fixture.A.Account));
            Assert.Equal(2, fixture.Session.Sources.Count);
        });

        Case("身份回归：尚无 client 时退出登录也撤销正在登录的结果", async () =>
        {
            using var fixture = new SessionFixture();
            var entered = Signal<bool>();
            var reply = Signal<HttpResponseMessage>();
            fixture.Transport.Reply = (sent, _) =>
            {
                if (!sent.IsAuthentication) return Task.FromResult(Standard(sent));
                entered.TrySetResult(true);
                return reply.Task;
            };
            var signIn = fixture.Session.SignInAsync(fixture.A.Server, fixture.A.Account, "new", "a", true, None);
            await entered.Task;
            fixture.Session.SignOut();
            reply.SetResult(SignedIn("a", "late"));
            await Fails<OperationCanceledException>(signIn);
            Assert.False(fixture.Session.IsSignedIn);
            Assert.Equal("token-a", fixture.Vault.GetAccessToken(fixture.A.Account));
        });

        Case("身份回归：并发同身份 401 合并一次登录，重试都用已核对的 client", async () =>
        {
            using var fixture = new SessionFixture();
            var entered = Signal<bool>();
            var reply = Signal<HttpResponseMessage>();
            fixture.Transport.Reply = (sent, _) =>
            {
                if (sent.IsAuthentication)
                {
                    entered.TrySetResult(true);
                    return reply.Task;
                }
                return Task.FromResult(sent.IsWrite && sent.Token == "token-a"
                    ? Json("", HttpStatusCode.Unauthorized) : Standard(sent));
            };
            await fixture.Restore(fixture.A);
            var first = fixture.Session.ExecuteAsync((client, token) => client.MarkPlayedAsync("one", token), None);
            await entered.Task;
            var second = fixture.Session.ExecuteAsync((client, token) => client.MarkPlayedAsync("two", token), None);
            reply.SetResult(SignedIn("a", "fresh-a"));
            await Task.WhenAll(first, second);
            Assert.Equal(1, fixture.Transport.Sent.Count(s => s.IsAuthentication));
            Assert.Equal(2, fixture.Transport.Sent.Count(s => s.IsWrite && s.Token == "fresh-a"));
            Assert.Equal(0, fixture.SignedOut);
        });
    }

    private static void RegisterScopes()
    {
        foreach (var sameServer in new[] { false, true })
            Case($"推荐来源：{(sameServer ? "同服异账号" : "异服同 ID")}切默认后，图片、详情、已看和收藏仍属于 A", async () =>
            {
                using var fixture = new SessionFixture(sameServer);
                await fixture.Restore(fixture.A);
                var source = fixture.Session.Capture();
                await fixture.Restore(fixture.B);
                fixture.Transport.Reply = (sent, _) => Task.FromResult(
                    sent.Url.AbsolutePath.EndsWith("Movies/Recommendations", StringComparison.Ordinal)
                        ? Json("[{\"Items\":[{\"Id\":\"same-id\",\"ImageTags\":{\"Primary\":\"poster-a\"}}]},{\"Items\":[{\"Id\":\"other-id\"}]}]")
                        : Standard(sent));
                using var http = new EmbyHttp(fixture.Transport);
                var jellyfin = new EmbyClient(http, source.Connection with { ServerKind = MediaServerKind.Jellyfin, ServerVersion = "12.2" });
                var recommendations = await source.ExecuteAsync((_, ct) => jellyfin.GetMovieRecommendationsAsync("library-a", 6, ct), None);
                var item = recommendations[0].Items.Single();
                Assert.True(recommendations.SelectMany(group => group.Items).All(entry => ReferenceEquals(source, entry.Source)));
                Assert.Equal(source.Key, fixture.Session.Capture(item).Key);
                var direct = source.Bind(new MovieRecommendation { Items = [new EmbyItem { Id = "single" }] });
                Assert.Equal(source.Key, direct.Items.Single().Source!.Key);
                await item.Source!.ExecuteAsync((client, ct) => client.GetImageBytesAsync(item.Id, "Primary", "poster-a", 320, ct), None);
                await item.Source.ExecuteAsync((client, ct) => client.GetItemAsync(item.Id, ct), None);
                await item.Source.ExecuteAsync((client, ct) => client.MarkPlayedAsync(item.Id, ct), None);
                await item.Source.ExecuteAsync((client, ct) => client.SetFavoriteAsync(item.Id, true, ct), None);
                var sent = fixture.Transport.Sent.Where(request => request.Url.AbsolutePath.Contains("same-id", StringComparison.Ordinal)).ToArray();
                Assert.Equal(4, sent.Length);
                Assert.True(sent.All(request => request.Url.Host == "a.invalid" && request.Token == "token-a"));
                Assert.True(sent.Where(request => request.Method == "POST")
                    .All(request => request.Url.AbsolutePath.Contains("/Users/a/", StringComparison.Ordinal)));
                Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
            });

        foreach (var sameServer in new[] { false, true })
            Case($"身份 Scope：切换{(sameServer ? "账号" : "服务器")}后仍可给 A 正确收尾", async () =>
            {
                using var fixture = new SessionFixture(sameServer);
                await fixture.Restore(fixture.A);
                var scope = fixture.Session.Capture();
                Assert.True(scope.IsCurrent);
                await fixture.Restore(fixture.B);
                Assert.False(scope.IsCurrent);
                await scope.ExecuteAsync(async (client, token) => { await client.MarkPlayedAsync("old", token); }, None);
                var connection = await scope.ExecuteAsync((client, _) => Task.FromResult(client.Connection), None);
                Assert.True(connection.IsSameIdentityAs(scope.Connection));
                var sent = fixture.Transport.Sent.Single(s => s.IsWrite);
                Assert.Equal("token-a", sent.Token);
                Assert.Contains("/Users/a/", sent.Url.AbsolutePath);
                Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
            });

        Case("身份 Scope：同身份刷新共享给已捕获 scope，快照不变且切 B 后仍用 A 新令牌", async () =>
        {
            using var fixture = new SessionFixture();
            fixture.Transport.Reply = (sent, _) => Task.FromResult(sent.IsWrite && sent.Token == "token-a"
                ? Json("", HttpStatusCode.Unauthorized) : Standard(sent));
            await fixture.Restore(fixture.A);
            var refreshing = fixture.Session.Capture();
            var waiting = fixture.Session.Capture();
            await refreshing.ExecuteAsync((client, token) => client.MarkPlayedAsync("first", token), None);
            Assert.Equal("token-a", waiting.Connection.AccessToken);
            Assert.True(waiting.IsCurrent);
            await fixture.Restore(fixture.B);
            await waiting.ExecuteAsync((client, token) => client.MarkPlayedAsync("last", token), None);
            Assert.Equal("fresh-a", fixture.Transport.Sent.Last(s => s.IsWrite).Token);
            Assert.Equal(1, fixture.Transport.Sent.Count(s => s.IsAuthentication));
        });

        Case("多来源 Scope：默认改为 B 后，A 的 401 仅恢复 A 且不注销 B", async () =>
        {
            using var fixture = new SessionFixture();
            await fixture.Restore(fixture.A);
            var scope = fixture.Session.Capture();
            await fixture.Restore(fixture.B);
            fixture.Transport.Reply = (sent, _) => Task.FromResult(sent.IsWrite
                ? Json("", HttpStatusCode.Unauthorized) : Standard(sent));
            await Fails<EmbyTokenExpiredException>(scope.ExecuteAsync((client, token) => client.MarkPlayedAsync("old", token), None));
            Assert.Equal(1, fixture.Transport.Sent.Count(s => s.IsAuthentication));
            Assert.True(scope.IsAvailable);
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
            Assert.Equal(0, fixture.SignedOut);
        });

        foreach (var callback in new[] { "event", "cancellation" })
            Case($"身份 Scope：退出时 {callback} 回调异常不阻断撤销和其他订阅者", async () =>
            {
                using var fixture = new SessionFixture();
                await fixture.Restore(fixture.A);
                var scope = fixture.Session.Capture();
                using var cleanup = new CancellationTokenSource();
                var entered = Signal<CancellationToken>();
                var active = scope.ExecuteAsync(async (_, token) =>
                {
                    entered.SetResult(token);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }, cleanup.Token);
                var token = await entered.Task;
                var notified = false;
                var cancelledWhenNotified = false;
                if (callback == "event") fixture.Session.SignedOut += (_, _) => throw new InvalidOperationException("fixture subscriber");
                using var registration = callback == "cancellation"
                    ? token.Register(() => throw new InvalidOperationException("fixture cancellation")) : default;
                fixture.Session.SignedOut += (_, _) =>
                {
                    notified = true;
                    cancelledWhenNotified = token.IsCancellationRequested;
                };

                Exception? failure = null;
                try { fixture.Session.SignOut(); }
                catch (Exception error) { failure = error; }
                try
                {
                    Assert.Null(failure, "注销清理不能被订阅者打断");
                    Assert.True(notified);
                    Assert.True(cancelledWhenNotified, "先撤销原会话，再通知外壳");
                    await Fails<OperationCanceledException>(active);
                }
                finally
                {
                    registration.Dispose();
                    cleanup.Cancel();
                    try { await active; }
                    catch (OperationCanceledException) { }
                }
            });

        Case("身份 Scope：释放时取消回调异常仍释放传输层且可以重复释放", async () =>
        {
            using var fixture = new SessionFixture();
            await fixture.Restore(fixture.A);
            var scope = fixture.Session.Capture();
            var entered = Signal<CancellationToken>();
            var active = scope.ExecuteAsync(async (_, token) =>
            {
                entered.SetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }, None);
            var token = await entered.Task;
            using var registration = token.Register(() => throw new InvalidOperationException("fixture cancellation"));
            fixture.Session.Dispose();
            Assert.True(fixture.Transport.Disposed);
            await Fails<OperationCanceledException>(active);
            fixture.Session.Dispose();
        });

        foreach (var dispose in new[] { false, true })
            Case($"身份 Scope：{(dispose ? "Dispose" : "SignOut")}撤销既有 scope 及在飞操作", async () =>
            {
                using var fixture = new SessionFixture();
                await fixture.Restore(fixture.A);
                var scope = fixture.Session.Capture();
                var entered = Signal<bool>();
                var active = scope.ExecuteAsync(async (_, token) =>
                {
                    entered.SetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }, None);
                await entered.Task;
                if (dispose) fixture.Session.Dispose();
                else fixture.Session.SignOut();
                await Fails<OperationCanceledException>(active);
                Assert.False(scope.IsCurrent);
                if (dispose)
                    await Fails<ObjectDisposedException>(scope.ExecuteAsync((_, _) => Task.CompletedTask, None));
                else
                {
                    await fixture.Restore(fixture.B);
                    await Fails<OperationCanceledException>(scope.ExecuteAsync((_, _) => Task.CompletedTask, None));
                }
            });
    }

    private static void RegisterMultipleSources()
    {
        Case("多来源：登录新账号不会退出已有在线来源", async () =>
        {
            using var fixture = new SessionFixture();
            await fixture.Session.RestoreAllAsync(None);
            Assert.Equal(2, fixture.Session.Sources.Count);

            var server = new ServerProfile { Name = "c", Url = "https://c.invalid" };
            var account = new AccountProfile { Username = "c", UserId = "c" };
            server.Accounts.Add(account);
            fixture.Settings.Servers.Add(server);
            await fixture.Session.SignInAsync(server, account, "password-c", "c", true, None);

            Assert.Equal(3, fixture.Session.Sources.Count);
            var accountIds = fixture.Session.Sources.Select(source => source.AccountProfileId).ToArray();
            Assert.Equal(3, accountIds.Distinct().Count());
            Assert.True(accountIds.Contains(fixture.A.Account.Id) && accountIds.Contains(fixture.B.Account.Id)
                && accountIds.Contains(account.Id), "登录第三个账号后前两个来源被挤掉了");
        });

        Case("收藏聚合：分账号筛选只留下选中来源，全部与未知键各归各位", async () =>
        {
            using var fixture = new SessionFixture();
            await fixture.Session.RestoreAllAsync(None);
            var sources = fixture.Session.Sources;
            Assert.Equal(2, FavoriteAccounts.Applicable(sources).Count);

            var selected = FavoriteAccounts.Select(sources, sources[1].Key);
            Assert.True(selected.Count == 1 && selected[0].Key == sources[1].Key, "收藏筛选没有只留下选中账号");
            Assert.Equal(2, FavoriteAccounts.Select(sources, null).Count);
            Assert.Equal(0, FavoriteAccounts.Select(sources, "missing").Count);
        });

        Case("聚合媒体库：不同服务器相同库编号保留两份且来源各自绑定", async () =>
        {
            using var fixture = new SessionFixture();
            fixture.Transport.Reply = (sent, _) => Task.FromResult(sent.IsViews
                ? Json("{\"Items\":[{\"Id\":\"shared-library\",\"Name\":\"电影\",\"Type\":\"CollectionFolder\"}]}")
                : Standard(sent));
            await fixture.Session.RestoreAllAsync(None);
            var result = await SourceLibraries.LoadAsync(fixture.Session, None);
            Assert.Equal(2, result.Items.Count);
            Assert.True(result.Items.All(item => item.Id == "shared-library"));
            Assert.Equal(2, result.Items.Select(item => item.IdentityKey).Distinct().Count());
            Assert.Equal(0, result.FailedSources.Count);
            Assert.True(result.Items.All(item => item.Source?.IsAvailable == true));
        });

        Case("聚合媒体库：一台服务器请求失败仍返回另一台媒体库并注明来源", async () =>
        {
            using var fixture = new SessionFixture();
            await fixture.Session.RestoreAllAsync(None);
            foreach (var source in fixture.Session.Sources) fixture.Session.TakeRestoredViews(source);
            fixture.Transport.Reply = (sent, _) => Task.FromResult(sent.IsViews && sent.Token == "token-a"
                ? Json("", HttpStatusCode.ServiceUnavailable) : Standard(sent));
            var result = await SourceLibraries.LoadAsync(fixture.Session, None);
            Assert.Equal(1, result.Items.Count);
            Assert.Equal(fixture.B.Account.Id, result.Items[0].Source?.AccountProfileId);
            Assert.Equal(1, result.FailedSources.Count);
            Assert.Contains(fixture.A.Account.Username, result.FailedSources[0]);
        });

        Case("聚合媒体库：退出来源后不再查询该账号", async () =>
        {
            using var fixture = new SessionFixture();
            await fixture.Session.RestoreAllAsync(None);
            fixture.Session.SignOut(fixture.A.Server, fixture.A.Account);
            foreach (var source in fixture.Session.Sources) fixture.Session.TakeRestoredViews(source);
            fixture.Transport.Sent.Clear();
            var result = await SourceLibraries.LoadAsync(fixture.Session, None);
            Assert.Equal(1, result.Items.Count);
            Assert.Equal(fixture.B.Account.Id, result.Items[0].Source?.AccountProfileId);
            Assert.True(fixture.Transport.Sent.All(sent => sent.Token != "token-a"));
            Assert.Equal(1, fixture.Transport.Sent.Count(sent => sent.IsViews));
        });

        Case("多来源：启动恢复所有保存账号并保留默认，重复条目号按来源区分", async () =>
        {
            using var fixture = new SessionFixture(true);
            fixture.Settings.Remember(fixture.B.Server, fixture.B.Account);
            Assert.Equal(2, await fixture.Session.RestoreAllAsync(None));
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
            var sources = fixture.Session.Sources;
            var first = sources.Single(source => source.AccountProfileId == fixture.A.Account.Id);
            var second = sources.Single(source => source.AccountProfileId == fixture.B.Account.Id);
            var a = await first.ExecuteAsync((_, _) => Task.FromResult(new EmbyItem { Id = "same" }), None);
            var b = await second.ExecuteAsync((_, _) => Task.FromResult(new ItemsResult { Items = [new EmbyItem { Id = "same" }] }), None);
            Assert.Equal(first.Key, a.Source?.Key);
            Assert.Equal(second.Key, b.Items[0].Source?.Key);
            Assert.False(a.IdentityKey == b.Items[0].IdentityKey);
            Assert.Equal(first.Key, fixture.Session.Capture(a).Key);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(a));
            Assert.False(json.RootElement.TryGetProperty("Source", out _));
            Assert.False(json.RootElement.TryGetProperty("IdentityKey", out _));
        });

        Case("多来源：退出 A 仅撤销 A，保留 B 及 A 保存密码但不自动重登", async () =>
        {
            using var fixture = new SessionFixture();
            await fixture.Session.RestoreAllAsync(None);
            var a = fixture.Session.Sources.Single(source => source.AccountProfileId == fixture.A.Account.Id);
            var b = fixture.Session.Sources.Single(source => source.AccountProfileId == fixture.B.Account.Id);
            var item = a.Bind(new EmbyItem { Id = "same" });
            fixture.Session.SelectSource(b);
            fixture.Session.SignOut(fixture.A.Server, fixture.A.Account);
            Assert.False(a.IsAvailable);
            Assert.True(b.IsAvailable);
            Assert.Equal(1, fixture.Session.Sources.Count);
            Assert.Equal(0, fixture.SignedOut);
            Assert.False(fixture.A.Account.AutoConnect ?? true);
            Assert.True(fixture.A.Account.HasSavedPassword);
            Assert.False(fixture.A.Account.HasSavedToken);
            Assert.Throws<OperationCanceledException>(() => fixture.Session.Capture(item));
            using var restart = new EmbySession(fixture.Settings,
                new SettingsStore(new AppPaths(Path.Combine(fixture.Root, "restart")), PassthroughSecretProtector.Instance),
                fixture.Vault, DeviceIdentity.Create("restart", "1"), new Transport());
            Assert.Equal(1, await restart.RestoreAllAsync(None));
            Assert.Equal(fixture.B.Account.Id, restart.Capture().AccountProfileId);
        });

        Case("多来源：旧设置仅保存密码而没有令牌的账号不在启动时自动登录", async () =>
        {
            using var fixture = new SessionFixture();
            fixture.Vault.ClearAccessToken(fixture.A.Account);
            Assert.Null(fixture.A.Account.AutoConnect);
            Assert.Equal(1, await fixture.Session.RestoreAllAsync(None));
            Assert.Equal(fixture.B.Account.Id, fixture.Session.Capture().AccountProfileId);
            Assert.False(fixture.Transport.Sent.Any(sent => sent.IsAuthentication));
        });

        Case("多来源：A 令牌失效且未保存密码只注销 A，不撤销 B 的 scope", async () =>
        {
            using var fixture = new SessionFixture();
            await fixture.Session.RestoreAllAsync(None);
            var a = fixture.Session.Sources.Single(source => source.AccountProfileId == fixture.A.Account.Id);
            var b = fixture.Session.Sources.Single(source => source.AccountProfileId == fixture.B.Account.Id);
            fixture.Vault.SetPassword(fixture.A.Account, "", false);
            fixture.Transport.Reply = (sent, _) => Task.FromResult(sent.IsWrite && sent.Token == "token-a"
                ? Json("", HttpStatusCode.Unauthorized) : Standard(sent));
            await Fails<EmbyTokenExpiredException>(a.ExecuteAsync((client, token) => client.MarkPlayedAsync("a-item", token), None));
            Assert.False(a.IsAvailable);
            Assert.True(b.IsAvailable);
            Assert.True(fixture.Session.IsSignedIn);
            Assert.Equal(0, fixture.SignedOut);
            await b.ExecuteAsync((client, token) => client.MarkPlayedAsync("b-item", token), None);
            Assert.Equal("token-b", fixture.Transport.Sent.Last(sent => sent.IsWrite).Token);
        });

        Case("多来源：不同账号的令牌恢复互不排队，重试各自使用原用户", async () =>
        {
            using var fixture = new SessionFixture(true);
            await fixture.Session.RestoreAllAsync(None);
            var a = fixture.Session.Sources.Single(source => source.AccountProfileId == fixture.A.Account.Id);
            var b = fixture.Session.Sources.Single(source => source.AccountProfileId == fixture.B.Account.Id);
            var entered = Signal<bool>();
            var reply = Signal<HttpResponseMessage>();
            fixture.Transport.Reply = (sent, _) =>
            {
                if (sent.IsAuthentication && sent.Username == "a")
                {
                    entered.SetResult(true);
                    return reply.Task;
                }
                return Task.FromResult(sent.IsWrite && sent.Token.StartsWith("token-", StringComparison.Ordinal)
                    ? Json("", HttpStatusCode.Unauthorized) : Standard(sent));
            };
            var first = a.ExecuteAsync((client, token) => client.MarkPlayedAsync("a-item", token), None);
            await entered.Task;
            await b.ExecuteAsync((client, token) => client.MarkPlayedAsync("b-item", token), None).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(first.IsCompleted);
            reply.SetResult(SignedIn("a", "fresh-a"));
            await first;
            Assert.Equal(2, fixture.Transport.Sent.Count(sent => sent.IsAuthentication));
            Assert.True(fixture.Transport.Sent.Where(sent => sent.IsWrite && sent.Token == "fresh-a")
                .All(sent => sent.Url.AbsolutePath.Contains("/Users/a/", StringComparison.Ordinal)));
            Assert.True(fixture.Transport.Sent.Where(sent => sent.IsWrite && sent.Token == "fresh-b")
                .All(sent => sent.Url.AbsolutePath.Contains("/Users/b/", StringComparison.Ordinal)));
        });

        Case("多来源：同账号连续登录的旧成功不能覆盖新凭据", async () =>
        {
            using var fixture = new SessionFixture();
            var entered = Signal<bool>();
            var reply = Signal<HttpResponseMessage>();
            var requests = 0;
            fixture.Transport.Reply = (sent, _) =>
            {
                if (sent.IsAuthentication && Interlocked.Increment(ref requests) == 1)
                {
                    entered.SetResult(true);
                    return reply.Task;
                }
                return Task.FromResult(Standard(sent));
            };
            var first = fixture.Session.SignInAsync(fixture.A.Server, fixture.A.Account, "old", "a", true, None);
            await entered.Task;
            await fixture.Session.SignInAsync(fixture.A.Server, fixture.A.Account, "new", "a", true, None);
            reply.SetResult(SignedIn("a", "obsolete"));
            await Fails<OperationCanceledException>(first);
            Assert.Equal("fresh-a", fixture.Vault.GetAccessToken(fixture.A.Account));
            Assert.Equal("new", fixture.Vault.GetPassword(fixture.A.Account));
            Assert.Equal(1, fixture.Session.Sources.Count);
        });

        Case("多来源：其他会话的条目不能借本会话默认账号执行", async () =>
        {
            using var fixture = new SessionFixture();
            using var other = new SessionFixture();
            await fixture.Restore(fixture.A);
            await other.Restore(other.A);
            var item = other.Session.Capture().Bind(new EmbyItem { Id = "foreign" });
            Assert.Throws<OperationCanceledException>(() => fixture.Session.Capture(item));
        });
    }

    private static void RegisterAggregateMode()
    {
        Case("服务器账号选择：B 首账号离线、b2 在线时优先 b2，不使用 A 的全局最近账号", async () =>
        {
            using var fixture = new SessionFixture();
            var offline = new AccountProfile { Username = "offline-b", UserId = "offline-b" };
            fixture.B.Server.Accounts.Insert(0, offline);
            await fixture.Restore(fixture.B);
            await fixture.Restore(fixture.A);
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.PreferredAccount(fixture.B.Server)));
            fixture.Settings.LastAccountId = offline.Id;
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.PreferredAccount(fixture.B.Server)));
            fixture.Session.SignOut(fixture.B.Server, fixture.B.Account);
            Assert.True(ReferenceEquals(offline, fixture.Session.PreferredAccount(fixture.B.Server)));
            Assert.Null(fixture.Session.PreferredAccount(new ServerProfile()));
        });
        Case("服务器账号选择：同服多账号优先当前来源；无当前时最近在线优先于离线", async () =>
        {
            using var fixture = new SessionFixture(sameServer: true);
            await fixture.Restore(fixture.A);
            await fixture.Restore(fixture.B);
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.PreferredAccount(fixture.A.Server)));
            fixture.Session.SelectSource(fixture.Session.Sources.Single(source => source.AccountProfileId == fixture.A.Account.Id));
            Assert.True(ReferenceEquals(fixture.A.Account, fixture.Session.PreferredAccount(fixture.A.Server)));
        });
        Case("聚合模式：来源数不变时仍通知登录动作语义更新", async () =>
        {
            using var fixture = new SessionFixture(aggregateMode: false);
            await fixture.Restore(fixture.A);
            var notifications = 0;
            fixture.Session.SourcesChanged += (_, _) => notifications++;
            fixture.Settings.AggregateMode = true;
            Assert.Equal(0, fixture.Session.ApplyAggregateMode());
            fixture.Settings.AggregateMode = false;
            Assert.Equal(0, fixture.Session.ApplyAggregateMode());
            Assert.Equal(2, notifications);
            Assert.Equal(1, fixture.Session.Sources.Count);
        });

        Case("聚合模式关闭：启动只恢复上次那一个来源，其余账号保留凭据与自动连接", async () =>
        {
            using var fixture = new SessionFixture(aggregateMode: false);
            fixture.Settings.Remember(fixture.B.Server, fixture.B.Account);

            Assert.Equal(1, await fixture.Session.RestoreAllAsync(None));
            Assert.Equal(1, fixture.Session.Sources.Count);
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
            Assert.True(fixture.A.Account.HasSavedToken && fixture.A.Account.HasSavedPassword,
                "没被恢复的账号不应丢掉令牌或密码");
            Assert.True(fixture.A.Account.AutoConnect != false);
        });

        Case("聚合模式关闭：登录另一个账号替换当前来源，旧账号凭据保留", async () =>
        {
            using var fixture = new SessionFixture(aggregateMode: false);
            await fixture.Restore(fixture.A);

            await fixture.Session.SignInAsync(fixture.B.Server, fixture.B.Account, "password-b", "b", true, None);

            Assert.Equal(1, fixture.Session.Sources.Count);
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
            Assert.True(fixture.A.Account.HasSavedToken && fixture.A.Account.HasSavedPassword,
                "替换来源不是删除账号");
            Assert.True(fixture.A.Account.AutoConnect != false);
            Assert.Equal(0, fixture.SignedOut, "替换来源不是用户主动注销");
        });

        Case("聚合模式关闭：切换回旧账号后对方凭据仍保留，来回都可直接恢复", async () =>
        {
            using var fixture = new SessionFixture(aggregateMode: false);
            await fixture.Restore(fixture.A);
            await fixture.Restore(fixture.B);

            Assert.Equal(1, fixture.Session.Sources.Count);
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account));
            Assert.True(fixture.A.Account.HasSavedToken);

            await fixture.Restore(fixture.A);
            Assert.Equal(1, fixture.Session.Sources.Count);
            Assert.True(ReferenceEquals(fixture.A.Account, fixture.Session.Account));
            Assert.True(fixture.B.Account.HasSavedToken && fixture.B.Account.HasSavedPassword);
        });

        Case("聚合模式：打开后把已保存的账号都接回来，关掉时只留当前一个来源", async () =>
        {
            using var fixture = new SessionFixture(aggregateMode: false);
            await fixture.Restore(fixture.A);
            Assert.Equal(1, fixture.Session.Sources.Count);

            // 打开聚合模式：会话层照旧只做收口（这里没有要收的），真正的「都接回来」是 RestoreAllAsync ——
            // 服务器页拨开关时调的就是它。以前开关只改设置，用户还得回列表一台台点「登录账号」。
            fixture.Settings.AggregateMode = true;
            Assert.Equal(0, fixture.Session.ApplyAggregateMode());
            Assert.Equal(2, fixture.Session.RestorableAccounts);
            Assert.Equal(2, await fixture.Session.RestoreAllAsync(None));
            Assert.Equal(2, fixture.Session.Sources.Count);

            await fixture.Session.SignInAsync(fixture.B.Server, fixture.B.Account, "password-b", "b", true, None);
            Assert.Equal(2, fixture.Session.Sources.Count);

            fixture.Settings.AggregateMode = false;
            Assert.Equal(1, fixture.Session.ApplyAggregateMode());
            Assert.Equal(1, fixture.Session.Sources.Count);
            Assert.True(ReferenceEquals(fixture.B.Account, fixture.Session.Account),
                "收口应留下当前来源");
            Assert.True(fixture.A.Account.HasSavedToken && fixture.A.Account.HasSavedPassword);
            Assert.True(fixture.A.Account.AutoConnect != false);
            Assert.Equal(0, fixture.SignedOut);
        });

        Case("聚合模式：下线只摘掉在线来源，令牌、密码与自动连接都留着", async () =>
        {
            using var fixture = new SessionFixture();
            await fixture.Restore(fixture.A);
            await fixture.Restore(fixture.B);
            Assert.Equal(2, fixture.Session.Sources.Count);

            Assert.True(fixture.Session.TakeOffline(fixture.A.Server, fixture.A.Account));
            Assert.Equal(1, fixture.Session.Sources.Count);
            Assert.Equal(0, fixture.SignedOut, "下线不是退出登录");
            Assert.True(fixture.A.Account.HasSavedToken && fixture.A.Account.HasSavedPassword,
                "下线不该动已保存的凭据");
            Assert.True(fixture.A.Account.AutoConnect != false, "下线不该关掉自动连接");

            // 点「登录」走的就是这条路：令牌还在，不需要再输密码。
            Assert.True(await fixture.Session.TryRestoreAsync(fixture.A.Server, fixture.A.Account, None));
            Assert.Equal(2, fixture.Session.Sources.Count);

            // 已经不在线的账号没有「下线」可言，也不该重复通知。
            Assert.False(fixture.Session.TakeOffline(new ServerProfile { Name = "没登录过的" }, fixture.A.Account));
        });
    }

    private static void RegisterCapabilities()
    {
        Case("服务器能力：A 的迟到版本不能覆盖 B，退出后不再提供旧版本", async () =>
        {
            using var fixture = new SessionFixture();
            var entered = Signal<bool>();
            var reply = Signal<HttpResponseMessage>();
            fixture.Transport.Reply = (sent, _) =>
            {
                if (!sent.Url.AbsolutePath.EndsWith("/System/Info", StringComparison.Ordinal))
                    return Task.FromResult(Standard(sent));
                if (sent.Token == "token-a")
                {
                    entered.TrySetResult(true);
                    return reply.Task;
                }
                return Task.FromResult(Json("{\"Version\":\"4.9.5\"}"));
            };
            var capabilities = new ServerCapabilities(fixture.Session);
            await fixture.Restore(fixture.A);
            var a = capabilities.ProbeAsync();
            await entered.Task;
            await fixture.Restore(fixture.B);
            await capabilities.ProbeAsync();
            reply.SetResult(Json("{\"Version\":\"4.6.0\"}"));
            await a;

            Assert.Equal(new Version(4, 9, 5), capabilities.ServerVersion);
            fixture.Session.SignOut();
            Assert.Null(capabilities.ServerVersion);
        });

        Case("服务器能力：切服后新探测尚未成功也不能沿用旧版本", async () =>
        {
            using var fixture = new SessionFixture();
            fixture.Transport.Reply = (sent, _) => Task.FromResult(
                sent.Url.AbsolutePath.EndsWith("/System/Info", StringComparison.Ordinal)
                    ? Json("{\"Version\":\"4.6.0\"}") : Standard(sent));
            var capabilities = new ServerCapabilities(fixture.Session);
            await fixture.Restore(fixture.A);
            await capabilities.ProbeAsync();
            Assert.Equal(new Version(4, 6, 0), capabilities.ServerVersion);

            await fixture.Restore(fixture.B);
            Assert.Null(capabilities.ServerVersion);
            fixture.Transport.Reply = (sent, _) => Task.FromResult(
                sent.Url.AbsolutePath.EndsWith("/System/Info", StringComparison.Ordinal)
                    ? Json("{}") : Standard(sent));
            await capabilities.ProbeAsync();
            Assert.Null(capabilities.ServerVersion);
        });
    }

    private static void RegisterMoviePilot()
    {
        Case("MoviePilot 身份：同地址切账号与改密文凭据都使 JWT 失效，等价地址继续复用", async () =>
        {
            using var fixture = new MoviePilotFixture();
            await fixture.Service.SearchAsync("first", None);
            fixture.Settings.MoviePilot.Url = "http://MP.invalid:80/";
            await fixture.Service.SearchAsync("same", None);
            Assert.Equal(1, fixture.LoginCount);
            fixture.Settings.MoviePilot.Username = "b";
            fixture.Credentials.SetPassword(fixture.Settings.MoviePilot, "password-b");
            await fixture.Service.SearchAsync("second", None);
            Assert.Equal("Bearer jwt-b-2", fixture.Transport.Sent.Last().Authorization);
            fixture.Credentials.SetPassword(fixture.Settings.MoviePilot, "changed");
            await fixture.Service.SearchAsync("changed", None);
            Assert.Equal("Bearer jwt-b-3", fixture.Transport.Sent.Last().Authorization);
            Assert.Equal(3, fixture.LoginCount);
        });

        Case("MoviePilot 身份：清空保存密码后不能继续使用缓存 JWT", async () =>
        {
            using var fixture = new MoviePilotFixture();
            await fixture.Service.SearchAsync("first", None);
            fixture.Credentials.ClearPassword(fixture.Settings.MoviePilot);
            var count = fixture.Transport.Sent.Count;
            await Fails<MoviePilotException>(fixture.Service.SearchAsync("second", None));
            Assert.Equal(count, fixture.Transport.Sent.Count);
        });

        foreach (var change in new[] { "username", "password", "address" })
            Case($"MoviePilot 身份：登录期间修改 {change}，旧结果不缓存、不执行原操作", async () =>
            {
                using var fixture = new MoviePilotFixture();
                var entered = Signal<bool>();
                var reply = Signal<HttpResponseMessage>();
                var first = true;
                fixture.Transport.Reply = (sent, _) =>
                {
                    if (sent.IsMoviePilotLogin && first)
                    {
                        first = false;
                        entered.SetResult(true);
                        return reply.Task;
                    }
                    return Task.FromResult(fixture.Answer(sent));
                };
                var old = fixture.Service.SearchAsync("old", None);
                await entered.Task;
                if (change == "username") fixture.Settings.MoviePilot.Username = "b";
                else if (change == "password") fixture.Credentials.SetPassword(fixture.Settings.MoviePilot, "new-password");
                else fixture.Settings.MoviePilot.Url = "http://new-mp.invalid";
                var fresh = fixture.Service.SearchAsync("new", None);
                reply.SetResult(Json("{\"access_token\":\"old-jwt\",\"user_name\":\"a\"}"));
                await Fails<OperationCanceledException>(old);
                await fresh;
                Assert.Equal(1, fixture.Transport.Sent.Count(s => s.Url.AbsolutePath.Contains("media/search", StringComparison.Ordinal)));
                Assert.False(fixture.Transport.Sent.Any(s => s.Authorization == "Bearer old-jwt"));
            });

        Case("MoviePilot 身份：A 迟到 401 不清除 B 缓存，也不把 A 订阅重放到 B", async () =>
        {
            using var fixture = new MoviePilotFixture();
            var entered = Signal<bool>();
            var reply = Signal<HttpResponseMessage>();
            fixture.Transport.Reply = (sent, _) =>
            {
                if (sent.Url.AbsolutePath.Contains("subscribe", StringComparison.Ordinal))
                {
                    entered.SetResult(true);
                    return reply.Task;
                }
                if (sent.Url.AbsolutePath.Contains("media/search", StringComparison.Ordinal))
                    return Task.FromResult(Json("""[{"title":"synthetic","type":"电影","media_id":"1","media_source":"themoviedb"}]"""));
                return Task.FromResult(fixture.Answer(sent));
            };
            var media = (await fixture.Service.SearchAsync("synthetic", None)).Single();
            var old = fixture.Service.SubscribeAsync(media, None);
            await entered.Task;
            fixture.Settings.MoviePilot.Username = "b";
            fixture.Credentials.SetPassword(fixture.Settings.MoviePilot, "password-b");
            await fixture.Service.SearchAsync("b", None);
            reply.SetResult(Json("", HttpStatusCode.Unauthorized));
            await Fails<OperationCanceledException>(old);
            await fixture.Service.SearchAsync("b-again", None);
            Assert.Equal(2, fixture.LoginCount);
            Assert.Equal(1, fixture.Transport.Sent.Count(s => s.Url.AbsolutePath.Contains("subscribe", StringComparison.Ordinal)));
            Assert.Equal("Bearer jwt-b-2", fixture.Transport.Sent.Last().Authorization);
        });

        Case("MoviePilot 身份：同身份迟到 401 不清除已经刷新过的 JWT", async () =>
        {
            using var fixture = new MoviePilotFixture();
            await fixture.Service.SearchAsync("warm", None);
            var firstEntered = Signal<bool>();
            var secondEntered = Signal<bool>();
            var firstReply = Signal<HttpResponseMessage>();
            var secondReply = Signal<HttpResponseMessage>();
            fixture.Transport.Reply = (sent, _) =>
            {
                if (sent.Authorization == "Bearer jwt-a-1")
                {
                    if (sent.Url.Query.Contains("first", StringComparison.Ordinal))
                    {
                        firstEntered.SetResult(true);
                        return firstReply.Task;
                    }
                    secondEntered.SetResult(true);
                    return secondReply.Task;
                }
                return Task.FromResult(fixture.Answer(sent));
            };
            var first = fixture.Service.SearchAsync("first", None);
            var second = fixture.Service.SearchAsync("second", None);
            await Task.WhenAll(firstEntered.Task, secondEntered.Task);
            firstReply.SetResult(Json("", HttpStatusCode.Unauthorized));
            await first;
            secondReply.SetResult(Json("", HttpStatusCode.Unauthorized));
            await second;
            Assert.Equal(2, fixture.LoginCount);
            Assert.Equal("Bearer jwt-a-2", fixture.Transport.Sent.Last().Authorization);
        });
    }

    private static void RegisterDownloads()
    {
        Case("下载身份：队首挂起时切 B，旧队列不请求 B、也不覆盖已有目标", async () =>
        {
            using var fixture = new SessionFixture();
            using var gate = new SemaphoreSlim(1, 1);
            var entered = Signal<bool>();
            var reply = Signal<HttpResponseMessage>();
            fixture.Transport.Reply = (sent, _) =>
            {
                if (sent.Url.AbsolutePath.Contains("/Download", StringComparison.Ordinal))
                {
                    entered.TrySetResult(true);
                    return reply.Task;
                }
                return Task.FromResult(Standard(sent));
            };
            await fixture.Restore(fixture.A);
            var existing = Path.Combine(fixture.Root, "second.mkv");
            await File.WriteAllTextAsync(existing, "existing-file");
            var first = QueuedDownload(fixture.Session.Capture(), gate, "first", Path.Combine(fixture.Root, "first.mkv"));
            await entered.Task;
            var second = QueuedDownload(fixture.Session.Capture(), gate, "second", existing);
            await fixture.Restore(fixture.B);
            reply.SetResult(Json("synthetic-file"));
            await first;
            await Fails<OperationCanceledException>(second);
            Assert.Equal("existing-file", await File.ReadAllTextAsync(existing));
            Assert.Equal(1, fixture.Transport.Sent.Count(s => s.Url.AbsolutePath.Contains("/Download", StringComparison.Ordinal)));
            Assert.Equal("a.invalid", fixture.Transport.Sent.Last(s => s.Url.AbsolutePath.Contains("/Download", StringComparison.Ordinal)).Url.Host);
        });

        Case("下载身份：普通导航无身份变化，同一 scope 可顺序下载多个文件", async () =>
        {
            using var fixture = new SessionFixture();
            using var gate = new SemaphoreSlim(1, 1);
            await fixture.Restore(fixture.A);
            var scope = fixture.Session.Capture();
            using (var navigation = new LoadGeneration())
            {
                navigation.Begin();
                navigation.Cancel();
            }
            await QueuedDownload(scope, gate, "one", Path.Combine(fixture.Root, "one.mkv"));
            await QueuedDownload(scope, gate, "two", Path.Combine(fixture.Root, "two.mkv"));
            Assert.True(scope.IsCurrent);
            Assert.Equal(2, fixture.Transport.Sent.Count(s => s.Url.AbsolutePath.Contains("/Download", StringComparison.Ordinal)));
        });
    }

    private static async Task QueuedDownload(EmbySessionScope scope, SemaphoreSlim gate, string itemId, string path)
    {
        await gate.WaitAsync();
        try
        {
            scope.ThrowIfNotCurrent();
            await scope.ExecuteAsync((client, token) => client.DownloadToFileAsync(itemId, path, null, token), None);
        }
        finally
        {
            gate.Release();
        }
    }

    private static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage SignedIn(string user, string token) =>
        Json(JsonSerializer.Serialize(new { AccessToken = token, User = new { Id = user, Name = user } }));

    private static HttpResponseMessage Views(string user) =>
        Json(JsonSerializer.Serialize(new { Items = new[] { new { Id = user + "-view", Name = user } } }));

    private static HttpResponseMessage Standard(Sent sent)
    {
        if (sent.IsAuthentication) return SignedIn(sent.Username, "fresh-" + sent.Username);
        if (sent.IsViews) return Views(sent.Token.EndsWith('b') ? "b" : "a");
        if (sent.Url.AbsolutePath.EndsWith("System/Info/Public", StringComparison.Ordinal)) return Json("{\"ServerName\":\"synthetic\"}");
        return Json("{}");
    }

    private sealed record Sent(Uri Url, string Method, string Body, string Token, string Authorization)
    {
        public bool IsViews => Url.AbsolutePath.EndsWith("/Views", StringComparison.Ordinal);
        public bool IsWrite => Url.AbsolutePath.Contains("/PlayedItems/", StringComparison.Ordinal);
        public bool IsAuthentication => Url.AbsolutePath.EndsWith("/Users/AuthenticateByName", StringComparison.Ordinal);
        public bool IsMoviePilotLogin => Url.AbsolutePath.EndsWith("login/access-token", StringComparison.Ordinal);
        public string Username
        {
            get
            {
                if (IsMoviePilotLogin)
                    return Uri.UnescapeDataString(Body.Split('&').Single(pair => pair.StartsWith("username=", StringComparison.Ordinal))[9..]);
                using var document = JsonDocument.Parse(Body);
                return document.RootElement.GetProperty("UserName").GetString()!;
            }
        }
    }

    private sealed class Transport : HttpMessageHandler
    {
        public ConcurrentQueue<Sent> Sent { get; } = new();
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
        public Func<Sent, CancellationToken, Task<HttpResponseMessage>> Reply { get; set; } =
            (sent, _) => Task.FromResult(Standard(sent));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var sent = new Sent(request.RequestUri!, request.Method.Method, body,
                request.Headers.TryGetValues("X-Emby-Token", out var token) ? token.Single() : "",
                request.Headers.TryGetValues("Authorization", out var authorization) ? authorization.Single() : "");
            Sent.Enqueue(sent);
            return await Reply(sent, cancellationToken);
        }
    }

    private sealed record Profile(ServerProfile Server, AccountProfile Account);

    private sealed class SessionFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "identity-fixtures", Guid.NewGuid().ToString("N"));
        public Transport Transport { get; } = new();
        public AppSettings Settings { get; } = new();
        public CredentialVault Vault { get; } = new(PassthroughSecretProtector.Instance);
        public EmbySession Session { get; }
        public Profile A { get; }
        public Profile B { get; }
        public int SignedOut { get; private set; }

        public SessionFixture(bool sameServer = false, bool aggregateMode = true)
        {
            Directory.CreateDirectory(Root);
            Settings.AggregateMode = aggregateMode;
            A = Add("a", "https://a.invalid");
            B = Add("b", sameServer ? A.Server.Url : "https://b.invalid", sameServer ? A.Server : null);
            Session = new EmbySession(Settings, new SettingsStore(new AppPaths(Root), PassthroughSecretProtector.Instance),
                Vault, DeviceIdentity.Create("identity-fixture", "1"), Transport);
            Session.SignedOut += (_, _) => SignedOut++;
        }

        private Profile Add(string user, string address, ServerProfile? server = null)
        {
            if (server is null)
            {
                server = new ServerProfile { Name = user, Url = address };
                Settings.Servers.Add(server);
            }
            var account = new AccountProfile { Username = user, UserId = user };
            Vault.SetAccessToken(account, "token-" + user);
            Vault.SetPassword(account, "password-" + user, true);
            server.Accounts.Add(account);
            return new Profile(server, account);
        }

        public async Task Restore(Profile profile) =>
            Assert.True(await Session.TryRestoreAsync(profile.Server, profile.Account, None));

        public void Dispose()
        {
            Session.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private sealed class MoviePilotFixture : IDisposable
    {
        public Transport Transport { get; } = new();
        public AppSettings Settings { get; } = new();
        public MoviePilotCredentials Credentials { get; } = new(PassthroughSecretProtector.Instance);
        public MoviePilotService Service { get; }
        private readonly MoviePilotClient _client;
        public int LoginCount { get; private set; }

        public MoviePilotFixture()
        {
            Settings.MoviePilot.Enabled = true;
            Settings.MoviePilot.Url = "http://mp.invalid";
            Settings.MoviePilot.Username = "a";
            Credentials.SetPassword(Settings.MoviePilot, "password-a");
            Transport.Reply = (sent, _) => Task.FromResult(Answer(sent));
            _client = new MoviePilotClient(Transport);
            Service = new MoviePilotService(_client, Credentials, Settings);
        }

        public HttpResponseMessage Answer(Sent sent)
        {
            if (!sent.IsMoviePilotLogin) return Json("[]");
            LoginCount++;
            return Json(JsonSerializer.Serialize(new { access_token = $"jwt-{sent.Username}-{LoginCount}", user_name = sent.Username }));
        }

        public void Dispose() => _client.Dispose();
    }
}
