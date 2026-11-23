using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SiteDialogFlowTests
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Test("站点对话流程：搜索确认Primary保存一次、关闭后发一次事件", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(search: true);
            var searches = 0;
            picker.SearchRequested += (_, _) => searches++;
            var result = picker.OpenAsync();
            Assert.True(ReferenceEquals(result, picker.OpenAsync()), "重复打开应复用在飞任务");
            var dialog = picker.CurrentDialog!;
            Assert.Equal("搜索", dialog.ConfirmText);
            Assert.Equal(HorizontalAlignment.Right, dialog.Primary.HorizontalAlignment);
            Assert.Equal(2, dialog.ViewModel.Sites.Count);
            dialog.ViewModel.Sites[1].Selected = true;
            Assert.Equal(0, fixture.Saves);
            dialog.PressPrimary();
            Assert.Equal(ContentDialogResult.Primary, Completed(result));
            Assert.Equal(1, searches);
            Assert.Equal(1, fixture.Saves);
            Assert.Equal("2", picker.ViewModel.Selection!.QueryValue());
            Assert.True(dialog.ViewModel.IsDraftOwnerCurrent, "自己的同步保存通知不应撤销合法确认");
            Assert.True(picker.CurrentDialog is null);
        });

        Test("站点对话流程：每次打开都重读目录，保留本机勾选并同步站点变化", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(search: true);
            var result = picker.OpenAsync();
            var first = picker.CurrentDialog!;
            first.ViewModel.Sites[1].Selected = true;
            first.PressPrimary();
            Assert.Equal(ContentDialogResult.Primary, Completed(result));
            Assert.Equal(1, fixture.Saves);
            Assert.Equal(1, fixture.Transport.Reads);
            fixture.Transport.Catalogue = "[{\"id\":1,\"name\":\"更新后的甲站\",\"is_active\":true}]";
            result = picker.OpenAsync();
            var second = picker.CurrentDialog!;
            Assert.Equal(2, fixture.Transport.Reads, "重开不复用宿主的旧目录");
            Assert.Equal(1, second.ViewModel.Sites.Count);
            Assert.Equal("更新后的甲站", second.ViewModel.Sites[0].Name);
            Assert.Equal(0, second.ViewModel.SelectedCount);
            Assert.False(second.CanConfirm, "本机显式勾选的站点暂缺时不能扩大搜索");
            second.PressX();
            Assert.Equal(ContentDialogResult.None, Completed(result));
            fixture.Transport.Catalogue = Transport.FullCatalogue;
            result = picker.OpenAsync();
            var restored = picker.CurrentDialog!;
            Assert.Equal(3, fixture.Transport.Reads);
            Assert.Equal("2", restored.ViewModel.Selection!.QueryValue());
            restored.PressX();
            Assert.Equal(ContentDialogResult.None, Completed(result));
            Assert.Equal(1, fixture.Saves);
        });

        Test("站点对话流程：目录失败提示重新打开，关闭后重开自动恢复", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.FailCatalogue = true;
            var picker = fixture.Picker(search: true);
            var searches = 0;
            picker.SearchRequested += (_, _) => searches++;
            var result = picker.OpenAsync();
            var failed = picker.CurrentDialog!;
            Assert.True(failed.ViewModel.NoticeOpen);
            Assert.Contains("重新打开", failed.ViewModel.NoticeTitle!);
            failed.PressX();
            Assert.Equal(ContentDialogResult.None, Completed(result));
            fixture.Transport.FailCatalogue = false;
            result = picker.OpenAsync();
            var reopened = picker.CurrentDialog!;
            Assert.Equal(2, fixture.Transport.Reads);
            Assert.Equal(2, reopened.ViewModel.Sites.Count);
            Assert.False(reopened.ViewModel.NoticeOpen);
            reopened.PressX();
            Assert.Equal(ContentDialogResult.None, Completed(result));
            Assert.Equal(0, fixture.Saves);
            Assert.Equal(0, searches);
        });

        foreach (var dismissal in new[] { "X", "Esc", "遮罩" })
            Test("站点对话流程：" + dismissal + "取消None不保存不搜索且可重开", () =>
            {
                using var fixture = new Fixture();
                var picker = fixture.Picker(search: true);
                var searches = 0;
                picker.SearchRequested += (_, _) => searches++;
                var result = picker.OpenAsync();
                var dialog = picker.CurrentDialog!;
                dialog.ViewModel.SelectAllCommand.Execute(null);
                if (dismissal == "X") dialog.PressX();
                else dialog.Hide(); // Esc/遮罩的 WinUI 端点均以 Hide/None 结束；真实输入由父探针验证。
                Assert.Equal(ContentDialogResult.None, Completed(result));
                Assert.Equal(0, searches);
                Assert.Equal(0, fixture.Saves);
                Assert.True(picker.ViewModel.UseDefault);
                result = picker.OpenAsync();
                Assert.True(picker.CurrentDialog!.ViewModel.UseDefault);
                picker.ClosePopup();
                Assert.Equal(ContentDialogResult.None, Completed(result));
            });

        Test("站点对话流程：设置默认完成仅保存，清空可以完成不能搜索", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker();
            Assert.False(picker.SearchOnConfirm);
            var searches = 0;
            picker.SearchRequested += (_, _) => searches++;
            var result = picker.OpenAsync();
            var dialog = picker.CurrentDialog!;
            Assert.Equal("完成", dialog.ConfirmText);
            dialog.ViewModel.ClearSelectionCommand.Execute(null);
            Assert.True(dialog.CanConfirm);
            dialog.PressPrimary();
            Assert.Equal(ContentDialogResult.Primary, Completed(result));
            Assert.Equal(0, searches);
            Assert.Equal(1, fixture.Saves);
            Assert.False(picker.ViewModel.UseDefault || picker.ViewModel.HasSelection);
            picker.SearchOnConfirm = true;
            result = picker.OpenAsync();
            dialog = picker.CurrentDialog!;
            Assert.False(dialog.IsPrimaryButtonEnabled);
            dialog.PressPrimary();
            Assert.False(result.IsCompleted);
            picker.ClosePopup();
            Completed(result);
        });

        Test("站点对话流程：搜索页「完成」只保存、「立即搜索」保存并当场搜索", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(searchNow: true);
            var searches = 0;
            picker.SearchRequested += (_, _) => searches++;
            var result = picker.OpenAsync();
            var dialog = picker.CurrentDialog!;
            // 这一份不画 ContentDialog 的主键：底栏自己给「立即搜索」「完成」两颗，完成在右。
            Assert.Equal("", dialog.ConfirmText);
            Assert.Equal(Visibility.Visible, dialog.SearchNowVisibility);
            dialog.ViewModel.ClearSelectionCommand.Execute(null);
            Assert.True(dialog.CanConfirm);
            dialog.PressComplete();
            Assert.Equal(ContentDialogResult.None, Completed(result));
            Assert.Equal(0, searches);
            Assert.Equal(1, fixture.Saves);

            result = picker.OpenAsync();
            dialog = picker.CurrentDialog!;
            dialog.ViewModel.Sites[1].Selected = true;
            dialog.PressSearchNow();
            Assert.Equal(ContentDialogResult.None, Completed(result));
            Assert.Equal(1, searches);
            Assert.Equal(2, fixture.Saves);
            Assert.Equal("2", picker.ViewModel.Selection!.QueryValue());
            Assert.True(picker.CurrentDialog is null);
        });

        Test("站点对话流程：搜索页底栏两颗键跟着草稿可确认状态一起亮暗", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(searchNow: true);
            var result = picker.OpenAsync();
            var dialog = picker.CurrentDialog!;
            Assert.True(dialog.SearchNowButton.IsEnabled && dialog.CompleteButton.IsEnabled);
            dialog.ViewModel.ClearSelectionCommand.Execute(null);
            Assert.True(dialog.SearchNowButton.IsEnabled, "清空后仍可完成保存");
            fixture.Settings.MoviePilot.Username = "another-fixture";
            picker.ClosePopup();
            Completed(result);
        });

        Test("站点对话流程：保存失败不关闭/发搜索，重试后Primary", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(search: true);
            var searches = 0;
            picker.SearchRequested += (_, _) => searches++;
            var result = picker.OpenAsync();
            picker.CurrentDialog!.ViewModel.Sites[0].Selected = true;
            fixture.FailSave = true;
            picker.CurrentDialog.PressPrimary();
            Assert.False(result.IsCompleted);
            Assert.Equal(0, searches);
            Assert.True(picker.CurrentDialog.ViewModel.NoticeOpen);
            fixture.FailSave = false;
            picker.CurrentDialog.PressPrimary();
            Assert.Equal(ContentDialogResult.Primary, Completed(result));
            Assert.Equal(1, searches);
        });

        Test("R08-005 草稿保存失败后取消，原默认与缺键继承保持", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(search: true);
            var result = picker.OpenAsync();
            picker.CurrentDialog!.ViewModel.Sites[0].Selected = true;
            fixture.FailSave = true;
            picker.CurrentDialog.PressPrimary();
            Assert.False(result.IsCompleted);
            Assert.True(picker.CurrentDialog.ViewModel.NoticeOpen);
            picker.ClosePopup();
            Assert.Equal(ContentDialogResult.None, Completed(result));
            Assert.True(picker.ViewModel.UseDefault);
            Assert.True(fixture.Service.SearchSitePreference().UseDefault);
            Assert.Equal(0, fixture.Settings.MoviePilot.SearchSitePreferences.Count);
        });

        Test("站点对话流程：外部更改偏好含A→B→A不被旧草稿覆盖", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(search: true);
            var searches = 0;
            picker.SearchRequested += (_, _) => searches++;
            var result = picker.OpenAsync();
            picker.CurrentDialog!.ViewModel.Sites[1].Selected = true;
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [1]);
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, true, []);
            Assert.False(picker.CurrentDialog.IsPrimaryButtonEnabled, "外部更改应当场禁用旧确认");
            picker.CurrentDialog.PressPrimary();
            Assert.False(result.IsCompleted);
            Assert.Equal(0, searches);
            Assert.Equal(2, fixture.Saves);
            picker.ClosePopup();
            Completed(result);
        });

        Test("站点对话流程：自己的保存广播中外部再写不丢守卫、不发搜索", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(search: true);
            var searches = 0;
            picker.SearchRequested += (_, _) => searches++;
            var result = picker.OpenAsync();
            picker.CurrentDialog!.ViewModel.Sites[1].Selected = true;
            var reentered = false;
            fixture.Service.SearchSitesChanged += () =>
            {
                if (reentered) return;
                reentered = true;
                fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [1]);
            };
            picker.CurrentDialog.PressPrimary();
            Assert.False(result.IsCompleted);
            Assert.Equal(0, searches);
            Assert.Equal("1", string.Join(',', fixture.Service.SearchSitePreference().SiteIds));
            picker.ClosePopup();
            Completed(result);
        });

        Test("站点对话流程：连接更改或宿主离页拒绝旧确认", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(search: true);
            var result = picker.OpenAsync();
            picker.CurrentDialog!.ViewModel.Sites[0].Selected = true;
            fixture.Settings.MoviePilot.Username = "another-fixture";
            picker.CurrentDialog.PressPrimary();
            Assert.False(result.IsCompleted);
            Assert.Equal(0, fixture.Saves);
            picker.ClosePopup();
            Assert.Equal(ContentDialogResult.None, Completed(result));
            fixture.Settings.MoviePilot.Username = "anonymous-fixture";
            result = picker.OpenAsync();
            picker.ViewModel.Cancel();
            picker.CurrentDialog!.PressPrimary();
            Assert.False(result.IsCompleted);
            picker.ClosePopup();
            Completed(result);
        });

        Test("站点对话流程：挂起目录取消与迟到回复不复活、不搜索", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.Pending = new();
            var picker = fixture.Picker(search: true);
            var searches = 0;
            picker.SearchRequested += (_, _) => searches++;
            var result = picker.OpenAsync();
            var draft = picker.CurrentDialog!.ViewModel;
            var catalogue = draft.EnsureLoadedAsync();
            Assert.True(draft.Busy);
            picker.ClosePopup();
            Assert.Equal(ContentDialogResult.None, Completed(result));
            fixture.Transport.Pending.SetResult(Transport.Response());
            using var completion = new ManualResetEventSlim();
            _ = catalogue.ContinueWith(_ => completion.Set(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Assert.True(completion.Wait(TimeSpan.FromSeconds(5)), "被取消目录的迟到完成没有收尾");
            Assert.True(catalogue.IsCompletedSuccessfully);
            Assert.Equal(0, draft.Sites.Count);
            Assert.Equal(0, fixture.Saves);
            Assert.Equal(0, searches);
        });

        Test("站点对话流程：Cancel后同服务Attach恢复新弹窗、不复活旧草稿", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker(search: true);
            var result = picker.OpenAsync();
            var old = picker.CurrentDialog!.ViewModel;
            picker.ClosePopup();
            Completed(result);
            picker.ViewModel.Cancel();
            picker.ViewModel.Attach(fixture.Service);
            result = picker.OpenAsync();
            Assert.True(picker.CurrentDialog!.CanConfirm);
            Assert.False(old.CommitSelection());
            picker.CurrentDialog.PressPrimary();
            Assert.Equal(ContentDialogResult.Primary, Completed(result));
            Assert.Equal(1, fixture.Saves);
        });

        Test("站点对话流程：换Picker模型关闭旧表、原生根尺寸路径宽窄自适应", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker();
            var result = picker.OpenAsync();
            var dialog = picker.CurrentDialog!;
            Assert.Equal(580d, dialog.SiteDialogBody.Width);
            picker.XamlRoot!.Resize(360, 600);
            Assert.Equal(264d, dialog.SiteDialogBody.Width);
            Assert.Equal(264d, dialog.SiteDialogHeader.Width);
            dialog.SetGridWidth(100);
            Assert.Equal(100d, dialog.SiteGridLayout.MinItemWidth);
            var other = new MoviePilotSitePickerViewModel();
            other.Attach(fixture.Service);
            picker.ViewModel = other;
            Assert.Equal(ContentDialogResult.None, Completed(result));
            Assert.True(picker.CurrentDialog is null);
            picker.XamlRoot.Resize(1000, 800);
            Assert.Equal(264d, dialog.SiteDialogBody.Width, "关闭后应解除根尺寸订阅");
        });

        Test("站点对话流程：未挂根或连接不可用零网络零弹窗", () =>
        {
            using var fixture = new Fixture();
            var picker = fixture.Picker();
            picker.XamlRoot = null;
            Assert.Equal(ContentDialogResult.None, Completed(picker.OpenAsync()));
            Assert.True(picker.CurrentDialog is null);
            picker.XamlRoot = new();
            fixture.Settings.MoviePilot.Enabled = false;
            Assert.Equal(ContentDialogResult.None, Completed(picker.OpenAsync()));
            Assert.Equal(0, fixture.Transport.Reads);
        });
        return Run();
    }

    private static T Completed<T>(Task<T> task)
    {
        Assert.True(task.IsCompletedSuccessfully, "当前确认/取消的实际Task尚未成功结束");
        return task.Result;
    }

    private sealed class Fixture : IDisposable
    {
        internal AppSettings Settings { get; } = new();
        internal Transport Transport { get; } = new();
        private readonly MoviePilotClient _client;
        internal MoviePilotService Service { get; }
        internal int Saves { get; private set; }
        internal bool FailSave { get; set; }

        internal Fixture()
        {
            Settings.MoviePilot.Enabled = true;
            Settings.MoviePilot.Url = "http://192.0.2.10:3001";
            Settings.MoviePilot.Username = "anonymous-fixture";
            Settings.MoviePilot.ProtectedPassword = "placeholder-password";
            _client = new(Transport);
            Service = new(_client, new MoviePilotCredentials(new NoSecrets()), Settings, () =>
            {
                Saves++;
                if (FailSave) throw new IOException("anonymous save failure");
            });
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", flags)!.Invoke(Service, null)!;
            var cached = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
            var session = Activator.CreateInstance(cached, flags | BindingFlags.Public, null,
                [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "placeholder-token", "anonymous-fixture", true)], null);
            typeof(MoviePilotService).GetField("_session", flags)!.SetValue(Service, session);
            typeof(MoviePilotService).GetField("_availableIdentity", flags)!.SetValue(Service, identity);
        }
        internal MoviePilotSitePicker Picker(bool search = false, bool searchNow = false)
        {
            var model = new MoviePilotSitePickerViewModel();
            model.Attach(Service);
            return new() { XamlRoot = new(), ViewModel = model, SearchOnConfirm = search, SearchNow = searchNow };
        }
        public void Dispose() { Service.Dispose(); _client.Dispose(); }
    }

    private sealed class Transport : HttpMessageHandler
    {
        internal const string FullCatalogue = "[{\"id\":1,\"name\":\"甲\",\"is_active\":true},{\"id\":2,\"name\":\"乙\",\"is_active\":true}]";
        internal TaskCompletionSource<HttpResponseMessage>? Pending { get; set; }
        internal int Reads { get; private set; }
        internal string Catalogue { get; set; } = FullCatalogue;
        internal bool FailCatalogue { get; set; }
        internal static HttpResponseMessage Response() => new(HttpStatusCode.OK)
        {
            Content = new StringContent(FullCatalogue, Encoding.UTF8, "application/json")
        };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.True(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v1/site/",
                "站点流程夹具拒绝登录、搜索和所有真实写请求");
            Reads++;
            if (Pending is { } pending) return pending.Task;
            if (FailCatalogue) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Catalogue, Encoding.UTF8, "application/json")
            });
        }
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string value) => throw new AssertionException("不保护真实凭据");
        public string Unprotect(string value) => throw new AssertionException("不解密或登录");
    }
}
