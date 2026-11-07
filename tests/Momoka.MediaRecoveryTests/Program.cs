using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.MoviePilot;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        AniBtReceiptStateTests.Register();
        AniBtPrepareConcurrencyTests.Register();
        Test("R07-007 飞牛明确权限禁止时菜单与执行判据一致，未知保持", () =>
        {
            var item = new EmbyItem { Id = "fixture", Type = EmbyItemType.Movie, Name = "fixture" };
            var restricted = ItemMenu.For(item, fnNas: true, permissions: new(IsAdministrator: false, CanDeleteContent: false));
            Assert.False(restricted.Any(row => row.Command is ItemCommand.Delete or ItemCommand.ManageVersions or ItemCommand.EditMetadata));
            Assert.True(ItemMenu.Allows(ItemCommand.Delete, default));
            Assert.False(ItemMenu.Allows(ItemCommand.Delete, new(CanDeleteContent: false)));
        });
        foreach (var (input, port) in new[]
        {
            ("http://origin.invalid:80", 80), ("https://origin.invalid:443", 443),
            ("http://[::1]:80/path", 80), ("https://[::1]:443", 443),
            ("origin.invalid:3000", 3000), ("origin.invalid", 3001), ("[::1]", 3001)
        })
            Test($"R08-002 显式端口保持 {input}", () =>
            {
                Assert.True(MoviePilotAddress.TryNormalize(input, out var address, out _));
                Assert.Equal(port, address!.Port);
                var split = MoviePilotAddress.Split(input);
                Assert.Equal(port == 3001 ? "" : port.ToString(), split.Port);
                var joined = MoviePilotAddress.Join(split.Scheme, split.Host, split.Port);
                Assert.True(MoviePilotAddress.TryNormalize(joined, out var reopened, out _));
                Assert.Equal(port, reopened!.Port);
            });

        foreach (var mfa in new[] { false, true })
            Test($"R08-003 登录401准确解释MFA={mfa}", () =>
            {
                using var transport = new LoginFailure(mfa);
                using var client = new MoviePilotClient(transport);
                var failure = Assert.Catch<MoviePilotException>(() => Complete(client.SignInAsync(
                    new Uri("https://login.invalid/"), "anonymous", "placeholder", CancellationToken.None)));
                Assert.Equal(HttpStatusCode.Unauthorized, failure.Status);
                Assert.Contains(mfa ? "二次验证" : "用户名或密码不正确", failure.Message);
                if (mfa) Assert.DoesNotContain("用户名或密码不正确", failure.Message);
                Assert.Equal(1, transport.Writes);
            });

        foreach (var field in new[] { "season", "episode_group", "media_id", "media_source", "type" })
            Test($"R08-004 同订阅ID的{field}变化拒绝混合统计", () =>
            {
                var original = Subscription();
                var current = new Dictionary<string, object?>
                {
                    ["id"] = 12,
                    ["name"] = "fixture",
                    ["type"] = "电视剧",
                    ["season"] = 1,
                    ["media_source"] = "themoviedb",
                    ["media_id"] = "42",
                    ["episode_group"] = "group-a"
                };
                current[field] = field switch
                {
                    "season" => 2,
                    "type" => "电影",
                    "media_source" => "douban",
                    "media_id" => "43",
                    _ => "group-b"
                };
                var data = JsonSerializer.SerializeToElement(new { subscribe = current, episodes = new { } });
                Assert.Throws<MoviePilotException>(() => MoviePilotSubscriptionFiles.Parse(data, original));
            });
        Test("R08-004 非身份字段变化不拒绝统计", () =>
        {
            using var doc = JsonDocument.Parse("""
                {"subscribe":{"id":12,"name":"new title","type":"电视剧","season":1,"media_source":"themoviedb","media_id":"42","episode_group":"group-a","total_episode":1},"episodes":{"1":{"title":"E1","library":"/fixture/S01E01.mkv"}}}
                """);
            Assert.Equal(1, MoviePilotSubscriptionFiles.Parse(doc.RootElement, Subscription()).LibraryCount);
        });
        return Run();
    }

    private static MoviePilotSubscription Subscription() => MoviePilotSubscription.Parse(JsonSerializer.SerializeToElement(new
    {
        id = 12,
        name = "fixture",
        type = "电视剧",
        season = 1,
        media_source = "themoviedb",
        media_id = "42",
        episode_group = "group-a"
    }))!;

    private static T Complete<T>(Task<T> task)
    {
        using var completion = new ManualResetEventSlim();
        _ = task.ContinueWith(_ => completion.Set(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Assert.True(completion.Wait(TimeSpan.FromSeconds(5)), "匿名请求未结束");
        if (task.IsFaulted) throw task.Exception!.InnerException!;
        Assert.True(task.IsCompletedSuccessfully);
        return task.Result;
    }

    private sealed class LoginFailure(bool mfa) : HttpMessageHandler
    {
        internal int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/login/access-token", request.RequestUri!.AbsolutePath);
            Writes++;
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(mfa ? "{\"success\":false,\"data\":{\"mfa_methods\":[\"otp\"]}}" : "{\"detail\":\"bad credentials\"}")
            };
            if (mfa) response.Headers.Add("X-MFA-Required", "true");
            return Task.FromResult(response);
        }
    }
}
