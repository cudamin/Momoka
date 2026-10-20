using System.Net;
using System.Reflection;
using Momoka.FnNas;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FnNasProtocolTests
{
    private static void RegisterTransferBudgets()
    {
        Test("飞牛大传输：普通请求仍 30 秒，字节下载和上传允许跨过 30 秒且保留限额", () =>
        {
            // 三趟假传输同时等待，实际跨过旧 30 秒边界；不读写用户服务器、不变更机器时钟。
            VerifyTransferBudgetsAsync().WaitAsync(TimeSpan.FromSeconds(45)).GetAwaiter().GetResult();
        });
        Test("飞牛大传输：调用方取消仍立即结束下载与上传", () =>
            VerifyTransferCancellationAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        Test("飞牛大传输：独立预算不会取消大小上限或跨来源重定向限制", () =>
        {
            using var transport = new FixtureTransport(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new DeclaredOversizeContent() });
            using var http = new FnNasHttp(transport, disposeHandler: false);
            Assert.Throws<FnNasApiException>(() => http.GetBytesAsync(new Uri("https://nas.invalid/v/media/file"), Secret).GetAwaiter().GetResult());
            using var redirect = new FixtureTransport(_ => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            { Headers = { Location = new Uri("https://other.invalid/v/upload") } });
            using var upload = new FnNasHttp(redirect, disposeHandler: false);
            Assert.Throws<FnNasApiException>(() => upload.UploadAsync(new Uri("https://nas.invalid/v/upload"), [1, 2], "fixture.m3u", Secret, default)
                .GetAwaiter().GetResult());
            Assert.Equal(1, redirect.Requests.Count);
            Assert.Equal(TimeSpan.FromMinutes(5), (TimeSpan)typeof(FnNasHttp).GetField("TransferTimeout", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!);
        });
    }

    private static async Task VerifyTransferBudgetsAsync()
    {
        using var transport = new DelayedTransfer(TimeSpan.FromSeconds(32));
        using var http = new FnNasHttp(transport, disposeHandler: false);
        var json = http.SendAsync(HttpMethod.Get, new Uri("https://nas.invalid/v/api/v1/user/info"), null, Secret);
        var bytes = http.GetBytesAsync(new Uri("https://nas.invalid/v/media/file"), Secret);
        var upload = http.UploadAsync(new Uri("https://nas.invalid/v/upload"), [1, 2, 3], "fixture.m3u", Secret, default);
        var error = await FailureAsync(json);
        Assert.True(error.CanSkipOptionalRead, "普通请求的超时仍明确归类为暂时连不上");
        Assert.Equal("1,2,3", string.Join(',', await bytes));
        Assert.Equal("fixture-temp", (await upload)!["temp_file_id"]!.GetValue<string>());
    }

    private static async Task VerifyTransferCancellationAsync()
    {
        using var transport = new DelayedTransfer(Timeout.InfiniteTimeSpan);
        using var http = new FnNasHttp(transport, disposeHandler: false);
        using var cancellation = new CancellationTokenSource();
        var bytes = http.GetBytesAsync(new Uri("https://nas.invalid/v/media/file"), Secret, cancellation.Token);
        var upload = http.UploadAsync(new Uri("https://nas.invalid/v/upload"), [1], "fixture.m3u", Secret, cancellation.Token);
        cancellation.Cancel();
        foreach (var task in new Task[] { bytes, upload })
        {
            try { await task; throw new AssertionException("调用方取消没有结束传输"); }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task<FnNasApiException> FailureAsync(Task task)
    {
        try { await task; }
        catch (FnNasApiException error) { return error; }
        throw new AssertionException("普通请求应在 30 秒超时");
    }

    private sealed class DelayedTransfer(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("nas.invalid", request.RequestUri!.Host);
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri.AbsolutePath.Contains("/media/", StringComparison.Ordinal)
                    ? new ByteArrayContent([1, 2, 3]) : new StringContent("{\"code\":0,\"data\":{\"temp_file_id\":\"fixture-temp\"}}")
            };
        }
    }

    private sealed class DeclaredOversizeContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 65L * 1024 * 1024; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;
    }
}
