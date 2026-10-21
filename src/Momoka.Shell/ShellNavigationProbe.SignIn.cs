using System.Net;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task SignInAppearanceAsync()
    {
        using var fixture = CreateFixture();
        var page = new SignInPage();
        page.Attach(fixture.Services);
        var vm = page.ViewModel;
        var motion = HomeMotion.PreferenceEnabled;
        HomeMotion.ApplyPreference(false);
        _root.Children.Add(page);
        try
        {
            page.Prepare();
            ResizeProbeClient(1280, 800);
            await LayoutAsync(page);
            Require(vm.Address.Length == 0 && vm.SavedVisibility == Visibility.Collapsed,
                "首次连接不应出现保存服务器入口");

            page.ShowRestoring("离线影院");
            await LayoutAsync(page);
            var progress = Descendants(page).OfType<ProgressBar>().Single();
            Require(progress.IsIndeterminate && progress.Visibility == Visibility.Visible
                && vm.AddressVisibility == Visibility.Collapsed && vm.CredentialVisibility == Visibility.Collapsed,
                "自动登录未显示进度或仍保留可提交的表单");
            await SaveProbeScreenAsync("signin-restoring.png");
            page.Prepare();

            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                await LayoutAsync(page);
                var viewport = Get<ScrollView>(page, "FormScroll");
                var content = Get<StackPanel>(page, "FormContent");
                var origin = content.TransformToVisual(viewport).TransformPoint(new Point());
                Require(Math.Abs(origin.Y + content.ActualHeight / 2 - viewport.ActualHeight / 2) < 1,
                    "宽窗表单未在视口中居中");
                await SaveProbeScreenAsync($"signin-{theme.Id}.png");
            }

            ThemeHost.Apply(_options.Theme ?? "midnight");
            var mark = Get<Image>(page, "BrandMark");
            var wordmark = Get<TextBlock>(page, "BrandWordmark");
            var art = Get<Viewbox>(page, "WelcomeArt");
            foreach (var width in new[] { 999, 1000, 600, 760 })
            {
                ResizeProbeClient(width, 720);
                await LayoutAsync(page);
                Require(art.Visibility == (width >= 1000 ? Visibility.Visible : Visibility.Collapsed),
                    "跨断点后银幕显隐没有恢复");
                if (width < 1000)
                {
                    var icon = mark.TransformToVisual(page).TransformPoint(new Point());
                    var text = wordmark.TransformToVisual(page).TransformPoint(new Point());
                    Require(icon.Y >= 32 && text.X >= icon.X + mark.ActualWidth
                        && text.X - icon.X < 80 && Math.Abs(text.Y - icon.Y) < 24,
                        "紧凑品牌行错位或侵入标题栏");
                }
                await SaveProbeScreenAsync($"signin-address-{width}.png");
            }

            fixture.Settings.Servers.Add(new ServerProfile { Name = "客厅媒体库", Url = "https://cinema.invalid" });
            fixture.Settings.Servers.Add(new ServerProfile { Name = "家庭收藏", Url = "https://collection.invalid" });
            page.Prepare();
            Require(vm.SavedVisibility == Visibility.Visible && page.SavedMenuCount == 2,
                "保存服务器菜单没有跟随设置刷新");
            await LayoutAsync(page);
            var saved = Descendants(page).OfType<DropDownButton>().Single();
            Require(saved.ActualWidth > 0 && saved.ActualHeight > 0, "保存服务器入口尚未布局");
            try
            {
                saved.Flyout.ShowAt(saved);
                await SettleAsync();
                await SaveProbeScreenAsync("signin-saved.png");
            }
            finally { saved.Flyout.Hide(); }

            fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/Users/Public", StringComparison.Ordinal)
                ? Task.FromResult(Json(new[]
                {
                    new EmbyUser { Id = "cinema", Name = "影院用户", HasPassword = true },
                    new EmbyUser { Id = "family", Name = "家人共享的媒体收藏账号", HasPassword = true }
                })) : Task.FromResult(FakeTransport.Standard(request));
            vm.Address = "https://cinema.invalid";
            await vm.ConnectCommand.ExecuteAsync(null);
            await LayoutAsync(page);
            Require(vm.Step == SignInStep.Credential && vm.Users.Count == 2 && !vm.Busy,
                "连接后未呈现账号页");
            var passwordHint = Descendants(Get<PasswordBox>(page, "PasswordInput")).OfType<TextBlock>()
                .Single(text => text.Name == "PlaceholderTextContentPresenter");
            Require(passwordHint.TextAlignment == TextAlignment.Center,
                "首次进入账号页时密码占位符未居中");
            await SaveProbeScreenAsync("signin-credential.png");

            // Reject an artificial password through the same command and error presentation used by
            // the form. The fixture transport never reaches a real server or credential store.
            fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/Users/AuthenticateByName", StringComparison.Ordinal)
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)) : Task.FromResult(FakeTransport.Standard(request));
            vm.Username = "影院用户";
            vm.Password = "fixture-password";
            await vm.SignInCommand.ExecuteAsync(null);
            await LayoutAsync(page);
            Require(Get<InfoBar>(page, "SignInNotice").IsOpen && vm.Step == SignInStep.Credential,
                "登录失败后未保留可重试的账号页");
            Require(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot), Get<PasswordBox>(page, "PasswordInput")),
                "登录失败后焦点未回到密码框");

            ResizeProbeClient(600, 560);
            await LayoutAsync(page);
            var scroll = Get<ScrollView>(page, "FormScroll");
            Require(scroll.ScrollableHeight > 0, "短窗口中的错误及账号表单没有滚动空间");
            await ScrollProbeAsync(scroll, 0);
            await SaveProbeScreenAsync("signin-error-top.png");
            await ScrollProbeAsync(scroll, scroll.ScrollableHeight);
            var back = Descendants(page).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "SignInBackButton");
            var top = back.TransformToVisual(scroll).TransformPoint(new Point());
            var bottom = back.TransformToVisual(scroll).TransformPoint(new Point(0, back.ActualHeight));
            Require(back.ActualHeight > 0 && top.Y >= 0 && bottom.Y <= scroll.ActualHeight + 1,
                $"短窗口滚到底部仍无法换服务器：offset={scroll.VerticalOffset:0.0}/{scroll.ScrollableHeight:0.0}；viewport={scroll.ActualHeight:0.0}；button={top.Y:0.0}–{bottom.Y:0.0}");
            await SaveProbeScreenAsync("signin-error-bottom.png");

            vm.BackToAddressCommand.Execute(null);
            await LayoutAsync(page);
            Require(vm.Step == SignInStep.Address && !vm.NoticeOpen && scroll.VerticalOffset < 1,
                "返回地址页未恢复表单、错误或滚动位置");
            fixture.Settings.Servers.RemoveAt(1);
            page.Prepare();
            Require(vm.SavedVisibility == Visibility.Collapsed && page.SavedMenuCount == 1,
                "只剩一个保存服务器时未收起入口");
        }
        finally
        {
            page.Detach();
            _root.Children.Remove(page);
            vm.Dispose();
            HomeMotion.ApplyPreference(motion);
            ThemeHost.Apply(_options.Theme ?? "midnight");
            ResizeProbeClient(1420, 980);
        }
    }
}
