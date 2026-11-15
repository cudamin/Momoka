using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell;
using Momoka.Theming;
using Windows.UI;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        var palette = new ResourceDictionary { Source = new Uri("ms-appx:///Theme/Palette.xaml") };
        foreach (var name in new[] { "Dark", "Light", "HighContrast" })
            palette.ThemeDictionaries[name] = new ResourceDictionary
            {
                ["EgWindowBrush"] = new SolidColorBrush(Color.FromArgb(255, 1, 2, 3)),
                ["EgFrostBrush"] = new AcrylicBrush()
            };
        Application.Current.Resources.MergedDictionaries.Add(palette);
        Test("系统主题：启动读取当前模式，稳定ID同步所有根与两份角色，高对比度不覆盖", () =>
        {
            Reset(false);
            var first = new FrameworkElement();
            var second = new FrameworkElement();
            ThemeHost.Register(first);
            ThemeHost.Register(second);
            var darkBrush = ThemeHost.ColorOf("HighContrast", "EgWindowBrush");
            ThemeHost.Apply("SYSTEM");
            Assert.Equal(UiThemes.SystemId, ThemeHost.Current.Id);
            Assert.False(ThemeHost.Current.IsDark);
            Assert.Equal(ElementTheme.Light, first.RequestedTheme);
            Assert.Equal(ElementTheme.Light, second.RequestedTheme);
            Assert.Equal(ThemeHost.ToColor(UiThemes.System(false).Colors.Window), ThemeHost.ColorOf("Dark", "EgWindowBrush"));
            Assert.Equal(ThemeHost.ColorOf("Dark", "EgWindowBrush"), ThemeHost.ColorOf("Light", "EgWindowBrush"));
            Assert.Equal(darkBrush, ThemeHost.ColorOf("HighContrast", "EgWindowBrush"));
            var late = new FrameworkElement();
            ThemeHost.Register(late);
            Assert.Equal(ElementTheme.Light, late.RequestedTheme);
        });
        Test("系统主题：后台突发通知只排一次，UI执行时读取最新模式并同步云母通知", () =>
        {
            Reset(true);
            ThemeHost.Apply(UiThemes.SystemId);
            var changed = 0;
            void OnChanged(UiTheme theme) => changed++;
            ThemeHost.Changed += OnChanged;
            try
            {
                var reads = UISettings.Reads;
                var worker = new Thread(() => { SetDark(false); UISettings.Notify(); UISettings.Notify(); });
                worker.Start();
                worker.Join();
                Assert.True(ThemeHost.Current.IsDark);
                Assert.Equal(reads, UISettings.Reads);
                Assert.Equal(1, DispatcherQueue.Current.Pending);
                DispatcherQueue.Current.Drain();
                Assert.False(ThemeHost.Current.IsDark);
                Assert.Equal(1, changed);
                UISettings.Notify();
                DispatcherQueue.Current.Drain();
                Assert.Equal(1, changed);
                SetDark(true);
                UISettings.Notify();
                SetDark(false);
                DispatcherQueue.Current.Drain();
                Assert.False(ThemeHost.Current.IsDark);
                Assert.Equal(1, changed);
            }
            finally { ThemeHost.Changed -= OnChanged; }
        });
        Test("系统主题：切回固定主题撤销观察及旧队列，重选系统读取最新值", () =>
        {
            Reset(true);
            ThemeHost.Apply(UiThemes.SystemId);
            SetDark(false);
            UISettings.Notify();
            ThemeHost.Apply("midnight");
            DispatcherQueue.Current.Drain();
            Assert.Equal("midnight", ThemeHost.Current.Id);
            Assert.Equal(0, UISettings.Instances.Sum(instance => instance.Subscribers));
            UISettings.Notify();
            Assert.Equal(0, DispatcherQueue.Current.Pending);
            ThemeHost.Apply(UiThemes.SystemId);
            Assert.False(ThemeHost.Current.IsDark);
            Assert.Equal(1, UISettings.Instances.Sum(instance => instance.Subscribers));
        });
        Test("系统主题：拒绝或抛错的队列可在下次通知重试，停止后旧回调无效", () =>
        {
            Reset(true);
            ThemeHost.Apply(UiThemes.SystemId);
            SetDark(false);
            DispatcherQueue.Current.Reject = true;
            UISettings.Notify();
            DispatcherQueue.Current.Reject = false;
            DispatcherQueue.Current.Throw = true;
            UISettings.Notify();
            DispatcherQueue.Current.Throw = false;
            UISettings.Notify();
            Assert.Equal(1, DispatcherQueue.Current.Pending);
            DispatcherQueue.Current.Drain();
            Assert.False(ThemeHost.Current.IsDark);
            SetDark(true);
            UISettings.Notify();
            ThemeHost.StopSystemWatch();
            ThemeHost.StopSystemWatch();
            DispatcherQueue.Current.Drain();
            Assert.False(ThemeHost.Current.IsDark);
            Assert.Equal(0, UISettings.Instances.Sum(instance => instance.Subscribers));
        });
        Test("系统主题：读取失败保留最近配色，恢复读取可继续切换，订阅失败不阻塞选择", () =>
        {
            Reset(false);
            ThemeHost.Apply(UiThemes.SystemId);
            UISettings.FailRead = true;
            SetDark(true);
            UISettings.Notify();
            DispatcherQueue.Current.Drain();
            Assert.False(ThemeHost.Current.IsDark);
            UISettings.FailRead = false;
            UISettings.Notify();
            DispatcherQueue.Current.Drain();
            Assert.True(ThemeHost.Current.IsDark);
            Reset(false);
            UISettings.FailSubscribe = true;
            ThemeHost.Apply(UiThemes.SystemId);
            Assert.False(ThemeHost.Current.IsDark);
        });
        Test("系统主题：弹窗附加注册随系统同步，播放信息可局部退出", () =>
        {
            Reset(true);
            var dialog = new FrameworkElement();
            ThemeHost.SetFollowChanges(dialog, true);
            ThemeHost.Apply(UiThemes.SystemId);
            SetDark(false);
            UISettings.Notify();
            DispatcherQueue.Current.Drain();
            Assert.Equal(ElementTheme.Light, dialog.RequestedTheme);
            ThemeHost.KeepDark(dialog);
            ThemeHost.Apply(UiThemes.SystemId);
            Assert.Equal(ElementTheme.Dark, dialog.RequestedTheme);
            var local = (ResourceDictionary)dialog.Resources.ThemeDictionaries["Dark"];
            Assert.Equal(ThemeHost.ToColor(UiThemes.Default.Colors.Surface), ((SolidColorBrush)local["EgSurfaceBrush"]).Color);
            Assert.Equal(ThemeHost.ToColor(UiThemes.Default.Colors.Text), ((SolidColorBrush)local["EgTextBrush"]).Color);
            Assert.False(dialog.Resources.ThemeDictionaries.ContainsKey("HighContrast"));
            ThemeHost.Apply("midnight");
            var midnight = new FrameworkElement();
            ThemeHost.KeepDark(midnight);
            var midnightRoles = (ResourceDictionary)midnight.Resources.ThemeDictionaries["Dark"];
            Assert.Equal(ThemeHost.ToColor(UiThemes.Resolve("midnight").Colors.Surface),
                ((SolidColorBrush)midnightRoles["EgSurfaceBrush"]).Color);
        });
        Test("系统主题：普通及独立下载弹窗样式登记主题，播放信息显式保留局部深色", () =>
        {
            var styles = System.Xml.Linq.XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "Styles.xaml"));
            var x = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var dialogStyles = styles.Descendants().Where(element => element.Name.LocalName == "Style"
                && ((string?)element.Attribute(x + "Key") == "EgDialogSurfaceStyle"
                    || (string?)element.Attribute("TargetType") == "local:MoviePilotDownloadDialog")).ToArray();
            Assert.Equal(2, dialogStyles.Length);
            Assert.Equal(1, dialogStyles.Count(style => (string?)style.Attribute(x + "Key") == "EgDialogSurfaceStyle"));
            Assert.Equal(1, dialogStyles.Count(style => (string?)style.Attribute("TargetType") == "local:MoviePilotDownloadDialog"));
            foreach (var style in dialogStyles)
                Assert.True(style.Elements().Any(setter => (string?)setter.Attribute("Property") == "theme:ThemeHost.FollowChanges"
                    && (string?)setter.Attribute("Value") == "True"));
            var source = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "MediaInfoDialog.cs"));
            Assert.Contains("ThemeHost.KeepDark(dialog)", source);
        });
        var result = Run();
        Reset(true);
        return result;
    }

    private static void SetDark(bool dark) => UISettings.Foreground = Color.FromArgb(255, dark ? (byte)255 : (byte)0,
        dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0);

    private static void Reset(bool dark)
    {
        ThemeHost.Apply(UiThemes.DefaultId);
        DispatcherQueue.Current.Reject = false;
        DispatcherQueue.Current.Throw = false;
        DispatcherQueue.Current.Drain();
        UISettings.Instances.Clear();
        UISettings.FailRead = false;
        UISettings.FailSubscribe = false;
        SetDark(dark);
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("未找到当前工作树");
    }
}
