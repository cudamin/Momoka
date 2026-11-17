using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Momoka.Diagnostics;
using Momoka.Shell.Media;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>
/// 详情页里点开一支 YouTube 预告时用的播放窗：直接加载官方嵌入页，保留 YouTube 自己的播放控件，
/// 登录态与右上角预览、设置页登录共用一份 <see cref="YouTubeSession"/> 配置。
/// <para>
/// 没登录时 YouTube 可能停在登录卡片上；窗口底部给一颗「登录 YouTube」，按它只关掉本窗并让外壳去开设置页，
/// 不在这个播放窗里再套第二个对话框。
/// </para>
/// </summary>
internal static class YouTubePlayerDialog
{
    private const string Category = "YouTube 预告";

    /// <summary>返回用户选的下一步：去设置登录，或交给系统浏览器。</summary>
    internal readonly record struct Result(bool Login, bool Browser);

    internal static async Task<Result> ShowAsync(FrameworkElement owner, YouTubeSession session, string id, string title)
    {
        if (owner.XamlRoot is not { } root) return default;
        if (id.Length != 11 || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return default;

        var width = Math.Min(1000, Math.Max(640, root.Size.Width - 160));
        var height = Math.Min(width * 9 / 16, Math.Max(320, root.Size.Height - 240));
        var web = new WebView2 { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left };
        var hint = new TextBlock
        {
            Text = "如果画面停在登录提示上，可以在这里登录 YouTube；也可以交给系统浏览器打开。",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 16, 0)
        };
        var browser = new Button { Content = "在浏览器打开" };
        var login = new Button { Content = "登录 YouTube" };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(browser);
        actions.Children.Add(login);
        var footer = new Grid { Width = width };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(hint);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        var panel = new StackPanel { Width = width, Spacing = 8 };
        panel.Children.Add(web);
        panel.Children.Add(footer);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light,
            Title = string.IsNullOrWhiteSpace(title) ? "YouTube 预告" : title,
            Content = panel,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            MaxWidth = 1080
        };
        // 同上：模板按资源键取宽，默认 548 会把 16:9 画面裁掉两边。
        dialog.Resources["ContentDialogMaxWidth"] = 1100d;
        dialog.Resources["ContentDialogMinWidth"] = 640d;

        var loginRequested = false;
        var browserRequested = false;
        var lifetime = new CancellationTokenSource();
        try
        {
            var environment = await session.GetEnvironmentAsync(lifetime.Token);
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
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var next)) core.Navigate(next.AbsoluteUri);
            };
            core.DownloadStarting += (_, args) => args.Cancel = true;
            login.Click += (_, _) => { loginRequested = true; dialog.Hide(); };
            browser.Click += (_, _) => { browserRequested = true; dialog.Hide(); };
            // 用 watch 页而不是 /embed：发布方若关闭了「允许嵌入」，嵌入播放器会报 150，
            // 但正常播放页仍然可看；登录态由共享配置带进来。
            core.Navigate($"https://www.youtube.com/watch?v={id}");
            await dialog.ShowAsync();
        }
        catch (Exception error)
        {
            Log.Warn(Category, "无法打开 YouTube 预告窗口", error);
        }
        finally
        {
            lifetime.Cancel();
            try { web.Close(); } catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { }
            lifetime.Dispose();
        }
        return new Result(loginRequested, browserRequested);
    }
}
