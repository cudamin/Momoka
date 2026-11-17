using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Media;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>
/// 设置页的「登录 YouTube」：一扇可交互的 WebView2，走 Google 自己的登录页，登录态落进
/// <see cref="YouTubeSession"/> 那份独立配置。
/// <para>
/// 这里只观察 Cookie 名字判断是否登录，不读取、不记录 Cookie 值；登录、两步验证和验证码都由 Google 页面完成。
/// 关掉窗口不等于退出登录：退出按钮清掉这份配置里的 Cookie，下一次起播才会回到匿名状态。
/// </para>
/// </summary>
internal static class YouTubeLoginDialog
{
    private const string Category = "YouTube 登录";
    private const string StartUrl =
        "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";

    internal static async Task<bool> ShowAsync(FrameworkElement owner, YouTubeSession session)
    {
        if (owner.XamlRoot is not { } root) return false;

        var width = Math.Min(880, Math.Max(520, root.Size.Width - 160));
        var height = Math.Min(520, Math.Max(320, root.Size.Height - 240));
        var web = new WebView2 { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left };
        var status = new TextBlock
        {
            Text = "正在打开 Google 登录…",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 16, 0)
        };
        var signOut = new Button
        {
            Content = "退出 YouTube 登录",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var footer = new Grid { Width = width };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(status);
        Grid.SetColumn(signOut, 1);
        footer.Children.Add(signOut);
        var panel = new StackPanel { Width = width, Spacing = 8 };
        panel.Children.Add(web);
        panel.Children.Add(footer);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            Title = "登录 YouTube",
            Content = panel,
            PrimaryButtonText = "完成",
            CloseButtonText = "取消",
            // 密码框里按回车不能把窗口关掉；关闭按钮只由显式点击触发。
            DefaultButton = ContentDialogButton.Close,
            MaxWidth = 980
        };
        // ContentDialog 模板用的是这两个主题资源而不是上面那个 MaxWidth；不覆盖的话内容会被默认 548 裁掉，
        // Google 登录页的邮箱框和「下一步」正好在裁掉的那一半里。
        dialog.Resources["ContentDialogMaxWidth"] = 1000d;
        dialog.Resources["ContentDialogMinWidth"] = 560d;

        var lifetime = new CancellationTokenSource();
        var signedIn = false;
        try
        {
            CoreWebView2Environment environment;
            try { environment = await session.GetEnvironmentAsync(lifetime.Token); }
            catch (Exception error)
            {
                Log.Warn(Category, "无法创建 YouTube 登录用的 WebView2 配置", error);
                status.Text = "登录页没有打开：" + Failure.Describe(error);
                await dialog.ShowAsync();
                return false;
            }

            await web.EnsureCoreWebView2Async(environment);
            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Profile.PreferredColorScheme = ThemeHost.Current.IsDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
            core.NewWindowRequested += (_, args) =>
            {
                // Google 的两步验证/账号选择偶尔会要求新窗口；同一扇窗口接着走，不让它弹到系统浏览器。
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var next)) core.Navigate(next.AbsoluteUri);
            };
            core.DownloadStarting += (_, args) => args.Cancel = true;
            signOut.Click += (_, _) =>
            {
                try
                {
                    core.CookieManager.DeleteAllCookies();
                    session.Report(false);
                    signedIn = false;
                    status.Text = "已退出 YouTube 登录。";
                }
                catch (Exception error)
                {
                    Log.Warn(Category, "退出 YouTube 登录失败", error);
                    status.Text = "没能退出登录：" + Failure.Describe(error);
                }
            };
            web.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess) { status.Text = "在下面的页面登录 Google 账号；登录状态只保存在本机。"; signOut.Visibility = Visibility.Visible; }
                else status.Text = "登录页加载失败，请检查网络后重试。";
            };
            core.Navigate(StartUrl);
            _ = WatchAsync(core, status, session, lifetime.Token, () => signedIn = true);

            await dialog.ShowAsync();
        }
        catch (Exception error)
        {
            // WinUI 只允许一个对话框：设置页的其它提示正开着时这里会抛，按「没登录」收尾而不是炸穿命令。
            Log.Warn(Category, "无法显示 YouTube 登录窗口", error);
        }
        finally
        {
            lifetime.Cancel();
            try { web.Close(); } catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { }
            lifetime.Dispose();
        }
        return signedIn;
    }

    private static async Task WatchAsync(CoreWebView2 core, TextBlock status, YouTubeSession session,
        CancellationToken token, Action signedIn)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                var cookies = await core.CookieManager.GetCookiesAsync("https://www.youtube.com");
                if (!YouTubeTrailer.HasSession(cookies.Select(cookie => cookie.Name))) continue;
                session.Report(true);
                signedIn();
                status.Text = "已登录 YouTube。之后需要登录的预告会直接在 Momoka 里播放。";
                return;
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException
            or InvalidOperationException)
        { }
    }
}
