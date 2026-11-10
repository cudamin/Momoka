using System.Reflection;
using System.Security.Cryptography;
using Momoka.Configuration;
using Momoka.Danmaku;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class BilibiliSessionTests
{
    internal static void Register()
    {
        Test("B站登录：只收白名单Cookie，拒绝重复和请求头注入", () =>
        {
            var method = SessionType().GetMethod("CookieHeader")!;
            string Build(params KeyValuePair<string, string>[] cookies) => (string)method.Invoke(null, [cookies])!;
            Assert.Equal("SESSDATA=anonymous%2Ctoken; bili_jct=csrf; DedeUserID=42", Build(
                new("other_site", "must-not-leak"), new("bili_jct", "csrf"), new("SESSDATA", "anonymous%2Ctoken"), new("DedeUserID", "42")));
            Assert.Equal("", Build(new KeyValuePair<string, string>("SESSDATA", "safe\r\nInjected: bad")));
            Assert.Equal("", Build(new("SESSDATA", "one"), new("SESSDATA", "two")));
            Assert.Equal("", Build(new KeyValuePair<string, string>("SESSDATA", "safe; injected=value")));
            Assert.Equal("", Build(new KeyValuePair<string, string>("bili_jct", "csrf")));
        });
        Test("B站登录：凭据保护失败不回显原文，解密损坏返回可操作提示", () =>
        {
            var type = SessionType();
            var protect = type.GetMethod("Protect")!;
            var failure = Assert.Catch<TargetInvocationException>(() => protect.Invoke(null, [new LeakyProtector(), "SESSDATA=anonymous"]));
            Assert.True(failure.InnerException is CryptographicException);
            Assert.False(failure.ToString().Contains("SESSDATA=anonymous"));
            var arguments = new object?[] { new LeakyProtector(), "encrypted-placeholder", "" };
            Assert.Equal("", (string)type.GetMethod("Unprotect")!.Invoke(null, arguments)!);
            Assert.True(((string)arguments[2]!).Contains("重新登录"));
        });
        Test("B站登录：官方导航拒绝相似域、凭据和非HTTPS地址", () =>
        {
            var method = SessionType().GetMethod("IsOfficialNavigation")!;
            bool Accept(string url) => (bool)method.Invoke(null, [url])!;
            Assert.True(Accept("https://passport.bilibili.com/login"));
            Assert.True(Accept("https://www.bilibili.com/"));
            foreach (var url in new[] { "http://www.bilibili.com/", "https://bilibili.com.attacker.invalid/", "https://user@www.bilibili.com/", "https://www.bilibili.com:8443/", "file:///C:/data" })
                Assert.False(Accept(url));
        });
    }

    private static Type SessionType()
    {
        var type = typeof(DanmakuSource).Assembly.GetType("Momoka.Danmaku.BilibiliSession");
        Assert.True(type is not null, "缺少 B站登录凭据边界");
        return type!;
    }

    private sealed class LeakyProtector : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException(plainText);
        public string Unprotect(string cipherText) => throw new InvalidOperationException(cipherText);
    }
}
