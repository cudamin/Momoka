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

    /// <summary>返回 true 表示用户要去设置页登录。</summary>
    internal static async Task<bool> ShowAsync(FrameworkElement owner, YouTubeSession session, string id, string title)
    {
        if (owner.XamlRoot is not { } root) return false;
        if (id.Length != 11 || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return false;

        var width = Math.Min(1000, Math.Max(640, root.Size.Width - 160));
        var height = Math.Min(width * 9 / 16, Math.Max(320, root.Size.Height - 240));
        var web = new WebView2 { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left };
        var hint = new TextBlock
        {
            Text = "如果画面停在登录提示上，先登录 YouTube；登录后这支预告会直接播放。",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 16, 0)
        };
        var login = new Button { Content = "登录 YouTube", HorizontalAlignment = HorizontalAlignment.Right };
        var footer = new Grid { Width = width };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(hint);
        Grid.SetColumn(login, 1);
        footer.Children.Add(login);
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

        var loginRequested = false;
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
            core.Navigate($"https://www.youtube.com/embed/{id}?autoplay=1&rel=0&playsinline=1");
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
        return loginRequested;
    }
}
