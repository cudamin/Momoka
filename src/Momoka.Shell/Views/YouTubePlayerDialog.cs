using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Momoka.Diagnostics;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>
/// 详情页里点开一支 YouTube 预告时用的播放窗：加载正常 watch 页，保留 YouTube 自己的播放控件。
/// 发布方关闭「允许嵌入」时嵌入页会报 150，但 watch 页通常仍可看；窗口底部可以把链接交给系统浏览器。
/// 不做 YouTube 登录。
/// </summary>
internal static class YouTubePlayerDialog
{
    private const string Category = "YouTube 预告";

    /// <summary>返回 true 表示用户要去系统浏览器打开。</summary>
    internal static async Task<bool> ShowAsync(FrameworkElement owner, string id, string title)
    {
        if (owner.XamlRoot is not { } root) return false;
        if (id.Length != 11 || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return false;

        var width = Math.Min(1000, Math.Max(640, root.Size.Width - 160));
        var height = Math.Min(width * 9 / 16, Math.Max(320, root.Size.Height - 240));
        var web = new WebView2 { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left };
        var hint = new TextBlock
        {
            Text = "如果画面没有开始播放，可以交给系统浏览器打开。",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 16, 0)
        };
        var browser = new Button { Content = "在浏览器打开" };
        var footer = new Grid { Width = width };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(hint);
        Grid.SetColumn(browser, 1);
        footer.Children.Add(browser);
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
        // 模板按资源键取宽，默认 548 会把 16:9 画面裁掉两边。
        dialog.Resources["ContentDialogMaxWidth"] = 1100d;
        dialog.Resources["ContentDialogMinWidth"] = 640d;

        var browserRequested = false;
        var lifetime = new CancellationTokenSource();
        try
        {
            // 一次性的匿名配置：不登录、不残留账号 Cookie。
            var profile = Path.Combine(Path.GetTempPath(), "Momoka", "youtube-player", Guid.NewGuid().ToString("N"));
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync("", profile,
                new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" });
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
            browser.Click += (_, _) => { browserRequested = true; dialog.Hide(); };
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
        return browserRequested;
    }
}
