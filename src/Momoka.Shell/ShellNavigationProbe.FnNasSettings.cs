using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真正进入 A 的定向原生页，默认仍为 B；所有读取与写入只走拒绝真实目标的假传输。</summary>
    private async Task FnNasExplicitSourceAsync()
    {
        using var fixture = CreateFixture();
        fixture.Transport.Reply = FnNasSettingsReply;
        await fixture.SelectAsync("fn.invalid", "fn-user", MediaServerKind.FnNas);
        var source = fixture.Session.Capture();
        var account = fixture.Settings.Servers.Single(server => server.Id == source.ServerProfileId)
            .Accounts.Single(account => account.Id == source.AccountProfileId);
        account.RememberPassword = false;
        await fixture.SelectAsync("b.invalid", "b");
        Require(!source.IsCurrent && source.IsAvailable, "未建立非默认飞牛来源");

        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(FnNasPreferencesPage), new FnNasPreferencesRequest(fixture.Services, source.Key));
            var page = (FnNasPreferencesPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            await LayoutAsync(page);
            Require(page.ViewModel.Loaded && page.ViewModel.CanSave && page.IsCurrentSession,
                "非默认来源的播放偏好没有真正读取");
            Require(page.ViewModel.SubtitleLanguage == "zh" && page.ViewModel.AudioLanguage == "ja",
                "播放偏好不是 A 的内容");
            page.ViewModel.SubtitleLanguage = "en";
            await page.ViewModel.SaveCommand.ExecuteAsync(null);
            Require(fixture.Transport.Requests.Count(request => request == "POST /media/v/api/v1/user/info") == 1,
                "非默认来源保存未执行");
            Require(fixture.Session.Account?.Username == "b", "定向保存改了默认账号");
            await SaveRenderAsync(page, "fnnas-explicit-source-preferences-render.png");

            frame.Navigate(typeof(FnNasPasswordPage), new FnNasPasswordRequest(fixture.Services, source.Key));
            var passwordPage = (FnNasPasswordPage)frame.Content;
            await LayoutAsync(passwordPage);
            passwordPage.ViewModel.NewPassword = passwordPage.ViewModel.ConfirmPassword = "offline-new-password";
            var entered = Pending<bool>();
            var reply = Pending<HttpResponseMessage>();
            fixture.Transport.Reply = (request, ct) =>
            {
                if (!request.RequestUri!.AbsolutePath.EndsWith("/user/passwd", StringComparison.Ordinal))
                    return FnNasSettingsReply(request, ct);
                entered.TrySetResult(true);
                return reply.Task;
            };
            var saving = passwordPage.ViewModel.SaveCommand.ExecuteAsync(null);
            await entered.Task;
            passwordPage.Release();
            reply.SetResult(FnNasEnvelope("offline-new-token"));
            await saving;
            var vault = new CredentialVault(PassthroughSecretProtector.Instance);
            Require(vault.GetAccessToken(account) == "offline-new-token" && !account.RememberPassword,
                "离页使服务端确认的新令牌未保存，或擅自记住密码");
            Require(!passwordPage.ViewModel.NoticeOpen && passwordPage.ViewModel.NewPassword.Length == 0,
                "已离开的改密页被迟到成功重新填回");
            Require(fixture.Session.Account?.Username == "b", "改密确认改了默认账号");

            using var implicitSource = new FnNasPreferencesViewModel();
            fixture.Session.SelectSource(source);
            implicitSource.Attach(fixture.Session, "");
            await implicitSource.ReloadAsync();
            fixture.Session.SelectSource(fixture.Session.Sources.Single(scope => scope.AccountProfileId != source.AccountProfileId));
            var before = fixture.Transport.Requests.Count;
            await implicitSource.ReloadAsync();
            Require(!implicitSource.CanUse && fixture.Transport.Requests.Count == before,
                "未指定来源的旧页仍能操作非默认账号");
            fixture.Session.SignOut(fixture.Settings.Servers.Single(server => server.Id == source.ServerProfileId), account);
            Require(!passwordPage.IsCurrentSession, "已退出的定向来源仍显示可用");
        }
        finally { frame.Content = null; _root.Children.Remove(frame); }
    }

    private static Task<HttpResponseMessage> FnNasSettingsReply(HttpRequestMessage request, CancellationToken _)
    {
        Require(request.RequestUri!.Host.EndsWith(".invalid", StringComparison.Ordinal), "飞牛设置夹具拒绝真实目标");
        if (request.RequestUri.Host != "fn.invalid") return Task.FromResult(FakeTransport.Standard(request));
        var path = request.RequestUri.AbsolutePath;
        if (path.EndsWith("System/Info/Public", StringComparison.Ordinal))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        if (path.EndsWith("/sys/version", StringComparison.Ordinal))
            return Task.FromResult(FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.9.8" }));
        if (path.EndsWith("/sys/config", StringComparison.Ordinal))
            return Task.FromResult(FnNasEnvelope(new { server_guid = "fn-server", server_name = "fn.invalid", supported_languages = new[] { "zh", "ja", "en" } }));
        if (path.EndsWith("/user/info", StringComparison.Ordinal))
            return Task.FromResult(FnNasEnvelope(new { guid = "fn-user", username = "fn-user", is_admin = 1, lan = "zh", audio_lan = "ja" }));
        if (path.EndsWith("/mediadb/list", StringComparison.Ordinal))
            return Task.FromResult(FnNasEnvelope(Array.Empty<object>()));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
