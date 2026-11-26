using System.Collections.ObjectModel;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        RegisterFilters();
        RegisterSearchLifetime();
        RegisterEmptyAndRecovery();
        RegisterPreviews();
        RegisterXamlContracts();
        return Run();
    }

    private static void RegisterFilters()
    {
        Scenario("图片筛选：初始Toggled在owner附加前安全，默认全部来源与所有语言", async () =>
        {
            var requests = 0;
            var owner = new CoverDialog { Find = (_, _, _) => { requests++; throw new InvalidOperationException("unexpected initial request"); } };
            var result = Results("initial", "FanArt", "TheTVDB", "fanart", " ");
            result.Images.Add(Image("other", "TheMovieDb"));
            var picker = Picker(owner, result);
            try
            {
                Assert.Equal(0, requests);
                Assert.True(picker.AllLanguagesToggle.IsOn);
                Assert.Equal(0, picker.SourcePicker.SelectedIndex);
                Assert.Equal("全部,FanArt,TheTVDB,TheMovieDb", string.Join(',', picker.SourcePicker.Items));
                Assert.Equal(2, Rows(picker).Count);
                Assert.Equal("", picker.PrimaryButtonText);
                Assert.False(picker.IsPrimaryButtonEnabled);
                Assert.Equal("返回", picker.CloseButtonText);
            }
            finally { await Close(picker); }
        });

        Scenario("图片筛选：真实来源与语言事件传递完整搜索，集合属主保持稳定", async () =>
        {
            var searches = new List<(string Type, RemoteImageSearch Search)>();
            var owner = new CoverDialog
            {
                Find = (type, search, _) =>
                {
                    searches.Add((type, search));
                    return Task.FromResult(Results("filtered", "FanArt", "TheTVDB"));
                }
            };
            var picker = Picker(owner, Results("initial", "FanArt", "TheTVDB"));
            var rows = picker.Grid.ItemsSource;
            var providers = picker.SourcePicker.ItemsSource;
            try
            {
                picker.SourcePicker.SelectedIndex = 1;
                await picker.SearchSettledAsync();
                Assert.Equal(1, searches.Count, "更新来源目录不得递归发起搜索");
                Assert.Equal(EmbyImageStore.Thumb, searches[0].Type);
                Assert.Equal(new RemoteImageSearch("FanArt", true), searches[0].Search);
                picker.AllLanguagesToggle.IsOn = false;
                await picker.SearchSettledAsync();
                Assert.Equal(new RemoteImageSearch("FanArt", false), searches[1].Search);
                picker.SourcePicker.SelectedIndex = 0;
                await picker.SearchSettledAsync();
                Assert.Equal(new RemoteImageSearch(null, false), searches[2].Search);
                Assert.True(ReferenceEquals(rows, picker.Grid.ItemsSource));
                Assert.True(ReferenceEquals(providers, picker.SourcePicker.ItemsSource));
            }
            finally { await Close(picker); }
        });

        Scenario("图片筛选：清掉旧选择，等待隐藏使用按钮，结果重选恢复", async () =>
        {
            var pending = new TaskCompletionSource<RemoteImageResult>();
            var picker = Picker(new CoverDialog { Find = (_, _, _) => pending.Task }, Results("initial", "FanArt"));
            var original = Rows(picker)[0];
            try
            {
                picker.Grid.SelectedIndex = 0;
                Assert.Equal("使用这张", picker.PrimaryButtonText);
                Assert.NotNull(picker.Picked);
                picker.AllLanguagesToggle.IsOn = false;
                Assert.Null(picker.Picked);
                Assert.Null(picker.Grid.SelectedItem);
                Assert.Equal(0, Rows(picker).Count);
                Assert.Equal("", picker.PrimaryButtonText);
                Assert.False(picker.IsPrimaryButtonEnabled);
                Assert.Equal(Visibility.Visible, picker.SearchProgress.Visibility);
                pending.SetResult(Results("new", "FanArt"));
                await picker.SearchSettledAsync();
                Assert.Equal(Visibility.Collapsed, picker.SearchProgress.Visibility);
                Assert.Equal("", picker.PrimaryButtonText);
                picker.Grid.SelectedIndex = 0;
                Assert.Equal("使用这张", picker.PrimaryButtonText);
                Assert.True(picker.IsPrimaryButtonEnabled);
                Assert.Contains("new", picker.Picked!.Url);
                picker.Grid.SelectedItem = original;
                Assert.Null(picker.Picked, "旧候选即便被控件暂保留，也不能作为提交答案");
                Assert.Equal("", picker.PrimaryButtonText);
            }
            finally { pending.TrySetResult(new()); await Close(picker); }
        });

        Scenario("图片筛选：来源空结果保留当前来源和所有语言恢复入口", async () =>
        {
            var queries = new List<RemoteImageSearch>();
            var picker = Picker(new CoverDialog
            {
                Find = (_, search, _) => { queries.Add(search); return Task.FromResult(new RemoteImageResult()); }
            }, Results("initial", "FanArt", "TheTVDB"));
            try
            {
                picker.SourcePicker.SelectedIndex = 2;
                await picker.SearchSettledAsync();
                Assert.Equal("TheTVDB", picker.SourcePicker.SelectedItem as string);
                Assert.Equal(Visibility.Visible, picker.SourcePicker.Visibility);
                Assert.Equal(Visibility.Visible, picker.AllLanguagesToggle.Visibility);
                picker.AllLanguagesToggle.IsOn = false;
                await picker.SearchSettledAsync();
                Assert.Equal(new RemoteImageSearch("TheTVDB", false), queries.Last());
                Assert.Equal(Visibility.Visible, picker.UploadArtworkButton.Visibility);
            }
            finally { await Close(picker); }
        });

        Scenario("图片筛选：响应来源大小写变化仍保留已选来源", async () =>
        {
            var queries = new List<RemoteImageSearch>();
            var picker = Picker(new CoverDialog
            {
                Find = (_, search, _) =>
                {
                    queries.Add(search);
                    return Task.FromResult(Results("filtered", "FanArt", "TheTVDB"));
                }
            }, Results("initial", "fanart"));
            try
            {
                picker.SourcePicker.SelectedIndex = 1;
                await picker.SearchSettledAsync();
                Assert.Equal("FanArt", picker.SourcePicker.SelectedItem as string);
                picker.AllLanguagesToggle.IsOn = false;
                await picker.SearchSettledAsync();
                Assert.Equal(new RemoteImageSearch("FanArt", false), queries.Last());
            }
            finally { await Close(picker); }
        });
    }

    private static void RegisterSearchLifetime()
    {
        foreach (var lateFailure in new[] { false, true })
            Scenario($"图片筛选：快速切换取消旧请求，迟到{(lateFailure ? "错误" : "结果")}不覆盖新结果", async () =>
            {
                var requests = new List<SearchRequest>();
                var picker = Picker(new CoverDialog
                {
                    Find = (type, search, token) =>
                    {
                        var request = new SearchRequest(type, search, token);
                        requests.Add(request);
                        return request.Completion.Task;
                    }
                }, Results("initial", "FanArt", "TheTVDB"));
                try
                {
                    picker.SourcePicker.SelectedIndex = 1;
                    picker.SourcePicker.SelectedIndex = 2;
                    Assert.Equal(2, requests.Count);
                    Assert.True(requests[0].Token.IsCancellationRequested);
                    Assert.False(requests[1].Token.IsCancellationRequested);
                    requests[1].Completion.SetResult(Results("latest", "FanArt", "TheTVDB"));
                    await requests[1].Completion.Task;
                    await YieldUi();
                    Assert.Equal("TheTVDB", picker.SourcePicker.SelectedItem as string);
                    Assert.Contains("latest", Rows(picker).Single().Info.Url);
                    if (lateFailure) requests[0].Completion.SetException(new IOException("late failure"));
                    else requests[0].Completion.SetResult(Results("stale", "OldProvider"));
                    await picker.SearchSettledAsync();
                    Assert.Contains("latest", Rows(picker).Single().Info.Url);
                    Assert.Equal("TheTVDB", picker.SourcePicker.SelectedItem as string);
                    Assert.Equal(Visibility.Collapsed, picker.Notice.Visibility);
                    Assert.Equal(Visibility.Collapsed, picker.RetryArtworkSearch.Visibility);
                    Assert.Equal(Visibility.Collapsed, picker.SearchProgress.Visibility);
                }
                finally
                {
                    foreach (var request in requests) request.Completion.TrySetResult(new());
                    await Close(picker);
                }
            });

        Scenario("图片筛选：旧请求先完成仍不清掉新请求进度", async () =>
        {
            var requests = new List<SearchRequest>();
            var picker = Picker(new CoverDialog
            {
                Find = (type, search, token) =>
                {
                    var request = new SearchRequest(type, search, token);
                    requests.Add(request);
                    return request.Completion.Task;
                }
            }, Results("initial", "FanArt"));
            try
            {
                picker.AllLanguagesToggle.IsOn = false;
                picker.AllLanguagesToggle.IsOn = true;
                requests[0].Completion.SetResult(Results("stale", "FanArt"));
                await YieldUi();
                Assert.Equal(0, Rows(picker).Count);
                Assert.Equal(Visibility.Visible, picker.SearchProgress.Visibility);
                requests[1].Completion.SetResult(Results("latest", "FanArt"));
                await picker.SearchSettledAsync();
                Assert.Contains("latest", Rows(picker).Single().Info.Url);
            }
            finally
            {
                foreach (var request in requests) request.Completion.TrySetResult(new());
                await Close(picker);
            }
        });

        Scenario("图片筛选：关闭取消读取，迟到结果及后续事件不复活面板", async () =>
        {
            SearchRequest? request = null;
            var count = 0;
            var picker = Picker(new CoverDialog
            {
                Find = (type, search, token) =>
                {
                    count++;
                    request = new(type, search, token);
                    return request.Completion.Task;
                }
            }, Results("initial", "FanArt"));
            picker.AllLanguagesToggle.IsOn = false;
            Assert.NotNull(request);
            picker.Hide();
            Assert.True(request!.Token.IsCancellationRequested);
            request.Completion.SetResult(Results("closed", "NewProvider"));
            await picker.SearchSettledAsync();
            Assert.Equal(0, Rows(picker).Count);
            picker.AllLanguagesToggle.IsOn = true;
            picker.RetryArtworkSearch.Invoke();
            Assert.Equal(1, count);
            await Close(picker);
        });
    }

    private static void RegisterEmptyAndRecovery()
    {
        foreach (var providers in new[] { Array.Empty<string>(), new[] { "FanArt" } })
            Scenario($"图片空态：来源字段{(providers.Length == 0 ? "缺失" : "存在")}只说明光盘图无结果", async () =>
            {
                var result = new RemoteImageResult { Providers = [.. providers], Images = [new() { Url = " \t", ProviderName = "FanArt" }] };
                var picker = Picker(new CoverDialog(), result, EmbyImageStore.Disc);
                try
                {
                    Assert.Equal(0, Rows(picker).Count);
                    Assert.Equal(Visibility.Collapsed, picker.Grid.Visibility);
                    Assert.Equal(Visibility.Visible, picker.Notice.Visibility);
                    Assert.Contains("没有找到", picker.Notice.Text);
                    Assert.Contains("光盘封面", picker.Notice.Text);
                    Assert.DoesNotContain("刮削", picker.Notice.Text);
                    Assert.DoesNotContain("记录", picker.Notice.Text);
                    Assert.Equal("", picker.PrimaryButtonText);
                    Assert.Equal(Visibility.Visible, picker.UploadArtworkButton.Visibility);
                    Assert.True(picker.UploadArtworkButton.IsEnabled);
                }
                finally { await Close(picker); }
            });

        foreach (var error in new Exception[] { new EmbyUnreachableException("fixture timeout"), new UnauthorizedAccessException(), new IOException("fixture read failed") })
            Scenario($"图片失败：{error.GetType().Name}显示原因并允许重新搜索", async () =>
            {
                var count = 0;
                var picker = Picker(new CoverDialog
                {
                    Find = (_, _, _) => ++count == 1
                        ? Task.FromException<RemoteImageResult>(error) : Task.FromResult(Results("recovered", "FanArt"))
                }, Results("initial", "FanArt"));
                try
                {
                    picker.AllLanguagesToggle.IsOn = false;
                    await picker.SearchSettledAsync();
                    Assert.Contains("搜索图片失败", picker.Notice.Text);
                    Assert.Contains(Momoka.Diagnostics.Failure.Describe(error), picker.Notice.Text);
                    Assert.Equal(Visibility.Visible, picker.RetryArtworkSearch.Visibility);
                    Assert.Equal(Visibility.Visible, picker.UploadArtworkButton.Visibility);
                    Assert.True(picker.UploadArtworkButton.IsEnabled);
                    Assert.Equal("", picker.PrimaryButtonText);
                    picker.RetryArtworkSearch.Invoke();
                    await picker.SearchSettledAsync();
                    Assert.Equal(2, count);
                    Assert.Contains("recovered", Rows(picker).Single().Info.Url);
                    Assert.Equal(Visibility.Collapsed, picker.Notice.Visibility);
                    Assert.Equal(Visibility.Collapsed, picker.RetryArtworkSearch.Visibility);
                }
                finally { await Close(picker); }
            });

        Scenario("图片上传：搜索失败后仍能选择本机文件，候选与上传答案互斥", async () =>
        {
            var expected = new PickedArtwork([1, 2, 3], "fixture.png");
            var picks = 0;
            var picker = Picker(new CoverDialog
            {
                Find = (_, _, _) => Task.FromException<RemoteImageResult>(new IOException("fixture search failed")),
                Pick = () => { picks++; return Task.FromResult<PickedArtwork?>(expected); }
            }, Results("initial", "FanArt"));
            var completed = 0;
            picker.UploadCompleted += () => completed++;
            try
            {
                picker.AllLanguagesToggle.IsOn = false;
                await picker.SearchSettledAsync();
                picker.UploadArtworkButton.Invoke();
                await ((UiQueue)SynchronizationContext.Current!).IdleAsync();
                Assert.Equal(1, picks);
                Assert.Equal(1, completed);
                Assert.Equal(expected, picker.Uploaded!.Value);
                Assert.Null(picker.Picked);
                Assert.Equal("", picker.PrimaryButtonText);
                Assert.True(picker.UploadArtworkButton.IsEnabled);
            }
            finally { await Close(picker); }
        });

        foreach (var fail in new[] { false, true })
            Scenario($"图片上传：选文件{(fail ? "失败" : "取消")}保留面板和候选恢复能力", async () =>
            {
                var picker = Picker(new CoverDialog
                {
                    Pick = () => fail ? Task.FromException<PickedArtwork?>(new IOException("fixture picker failed")) : Task.FromResult<PickedArtwork?>(null)
                }, Results("initial", "FanArt"));
                var completed = 0;
                picker.UploadCompleted += () => completed++;
                try
                {
                    picker.Grid.SelectedIndex = 0;
                    picker.UploadArtworkButton.Invoke();
                    await ((UiQueue)SynchronizationContext.Current!).IdleAsync();
                    Assert.Null(picker.Uploaded);
                    Assert.NotNull(picker.Picked);
                    Assert.Equal(0, completed);
                    Assert.True(picker.UploadArtworkButton.IsEnabled);
                    Assert.True(picker.IsPrimaryButtonEnabled);
                    Assert.Equal("使用这张", picker.PrimaryButtonText);
                    if (fail) Assert.Contains("选择图片失败", picker.Notice.Text);
                }
                finally { await Close(picker); }
            });
    }

    private static void RegisterPreviews()
    {
        Scenario("图片预览：没有安全代理仍保留文字候选、筛选和上传", async () =>
        {
            var reads = 0;
            var picker = Picker(new CoverDialog
            {
                CanPreviewRemoteArtwork = false,
                Fetch = (_, _) => { reads++; throw new InvalidOperationException("unexpected remote fetch"); },
                Find = (_, _, _) => Task.FromResult(Results("filtered", "FanArt"))
            }, Results("initial", "FanArt"));
            try
            {
                Assert.True(double.IsNaN(picker.Grid.Height));
                Assert.Equal(Visibility.Collapsed, Rows(picker).Single().PreviewVisibility);
                Assert.Equal(46d, Rows(picker).Single().CellHeight);
                picker.Grid.SelectedIndex = 0;
                Assert.NotNull(picker.Picked);
                picker.AllLanguagesToggle.IsOn = false;
                await picker.SearchSettledAsync();
                Assert.Equal(1, Rows(picker).Count);
                picker.Grid.SelectedIndex = 0;
                Assert.Equal("使用这张", picker.PrimaryButtonText);
                Assert.Equal(0, reads);
                Assert.Equal(Visibility.Visible, picker.UploadArtworkButton.Visibility);
            }
            finally { await Close(picker); }
        });

        foreach (var pair in new[]
        {
            (Type: EmbyImageStore.Thumb, ItemType: EmbyItemType.Movie, Height: 84.375d),
            (Type: EmbyImageStore.Disc, ItemType: EmbyItemType.Movie, Height: 150d),
            (Type: EmbyImageStore.Primary, ItemType: EmbyItemType.Movie, Height: 225d),
            (Type: EmbyImageStore.Primary, ItemType: EmbyItemType.Episode, Height: 84.375d)
        })
            Scenario($"图片预览：{pair.ItemType}/{pair.Type}使用图种对应的框高", async () =>
            {
                var picker = Picker(new CoverDialog { CanPreviewRemoteArtwork = true }, Results("initial", "FanArt"), pair.Type, pair.ItemType);
                try
                {
                    await picker.PreviewsSettledAsync();
                    Assert.Equal(pair.Height, Rows(picker).Single().PreviewHeight);
                    Assert.Equal(pair.Height + 90, Rows(picker).Single().CellHeight);
                    Assert.NotNull(Rows(picker).Single().Picture);
                }
                finally { await Close(picker); }
            });

        Scenario("图片预览：换筛选后的迟到图片不写回已移除候选", async () =>
        {
            var preview = new TaskCompletionSource<byte[]?>();
            var picker = Picker(new CoverDialog
            {
                CanPreviewRemoteArtwork = true,
                Fetch = (url, _) => url.Contains("initial", StringComparison.Ordinal) ? preview.Task : Task.FromResult<byte[]?>([1]),
                Find = (_, _, _) => Task.FromResult(Results("latest", "FanArt"))
            }, Results("initial", "FanArt"));
            var original = Rows(picker).Single();
            try
            {
                picker.AllLanguagesToggle.IsOn = false;
                await picker.SearchSettledAsync();
                await picker.PreviewsSettledAsync();
                preview.SetResult([1]);
                await YieldUi();
                Assert.Null(original.Picture);
                Assert.Contains("latest", Rows(picker).Single().Info.Url);
                Assert.NotNull(Rows(picker).Single().Picture);
            }
            finally { preview.TrySetResult([1]); await Close(picker); }
        });

        Scenario("图片预览：第一次换筛选也取消首批预览，不让旧图占满并发槽", async () =>
        {
            var preview = new TaskCompletionSource<byte[]?>();
            var tokens = new List<CancellationToken>();
            var initial = Results("initial-0", "FanArt");
            for (var i = 1; i < 4; i++) initial.Images.Add(Image($"initial-{i}", "FanArt"));
            var picker = Picker(new CoverDialog
            {
                CanPreviewRemoteArtwork = true,
                Fetch = async (url, token) =>
                {
                    if (!url.Contains("initial", StringComparison.Ordinal)) return [1];
                    tokens.Add(token);
                    return await preview.Task.WaitAsync(token);
                },
                Find = (_, _, _) => Task.FromResult(Results("latest", "FanArt"))
            }, initial);
            try
            {
                Assert.Equal(4, tokens.Count);
                picker.AllLanguagesToggle.IsOn = false;
                await picker.SearchSettledAsync();
                Assert.True(tokens.All(token => token.IsCancellationRequested), "新筛选必须取消首批4张慢预览");
                await picker.PreviewsSettledAsync();
                Assert.Contains("latest", Rows(picker).Single().Info.Url);
                Assert.NotNull(Rows(picker).Single().Picture);
            }
            finally { preview.TrySetResult([1]); await Close(picker); }
        });

        Scenario("图片预览：失败的代理预览可重试，重试不发图片搜索", async () =>
        {
            var fetches = 0;
            var searches = 0;
            var picker = Picker(new CoverDialog
            {
                CanPreviewRemoteArtwork = true,
                Fetch = (_, _) => ++fetches == 1 ? Task.FromException<byte[]?>(new IOException("fixture preview failed")) : Task.FromResult<byte[]?>([1]),
                Find = (_, _, _) => { searches++; return Task.FromResult(new RemoteImageResult()); }
            }, Results("initial", "FanArt"));
            try
            {
                await picker.PreviewsSettledAsync();
                Assert.True(Rows(picker).Single().PreviewFailed);
                Assert.Equal(Visibility.Visible, picker.RetryArtworkPreviews.Visibility);
                picker.RetryArtworkPreviews.Invoke();
                await ((UiQueue)SynchronizationContext.Current!).IdleAsync();
                Assert.Equal(2, fetches);
                Assert.Equal(0, searches);
                Assert.NotNull(Rows(picker).Single().Picture);
                Assert.False(Rows(picker).Single().PreviewFailed);
                Assert.Equal(Visibility.Collapsed, picker.RetryArtworkPreviews.Visibility);
            }
            finally { await Close(picker); }
        });
    }

    private static void RegisterXamlContracts()
    {
        foreach (var (handle, glyph) in new[] { ("AddRemoteBackdrop", "\uE721"), ("AddBackdropButton", "\uE710") })
        {
            Test($"背景图：{handle} 是独立于图片列表的具名图标按钮", () =>
            {
                var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Production", "CoverDialog.xaml"));
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var button = document.Descendants().Single(element => element.Name.LocalName == "Button"
                    && (element.Attribute(x + "Name")?.Value == handle || element.Attribute("AutomationProperties.AutomationId")?.Value == handle));
                Assert.False(button.Ancestors().Any(element => element.Name.LocalName == "DataTemplate"), "空列表也要能触发");
                var icon = button.Elements().Single();
                Assert.Equal("FontIcon", icon.Name.LocalName);
                Assert.Equal(glyph, icon.Attribute("Glyph")?.Value);
                Assert.True(!string.IsNullOrWhiteSpace(button.Attribute("AutomationProperties.Name")?.Value));
                Assert.True(!string.IsNullOrWhiteSpace(button.Attribute("ToolTipService.ToolTip")?.Value));
                if (handle == "AddBackdropButton") Assert.Equal("OnAddBackdrop", button.Attribute("Click")?.Value);
            });
        }

        Test("图片XAML：筛选默认值、实际事件和自动化名称保持接通", () =>
        {
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Production", "CoverPickerDialog.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var controls = document.Descendants().Where(element => element.Attribute(x + "Name") is not null)
                .ToDictionary(element => element.Attribute(x + "Name")!.Value);
            Assert.Equal("OnSourceChanged", controls["SourcePicker"].Attribute("SelectionChanged")?.Value);
            Assert.Equal("OnLanguagesChanged", controls["AllLanguagesToggle"].Attribute("Toggled")?.Value);
            Assert.Equal("True", controls["AllLanguagesToggle"].Attribute("IsOn")?.Value);
            Assert.Equal("OnPicked", controls["Grid"].Attribute("SelectionChanged")?.Value);
            Assert.Equal("OnRetrySearch", controls["RetryArtworkSearch"].Attribute("Click")?.Value);
            Assert.Equal("OnUpload", controls["UploadArtworkButton"].Attribute("Click")?.Value);
            foreach (var name in new[] { "SourcePicker", "AllLanguagesToggle", "RetryArtworkSearch" })
                Assert.True(!string.IsNullOrWhiteSpace(controls[name].Attribute("AutomationProperties.AutomationId")?.Value));
            Assert.Equal("Collapsed", controls["SearchProgress"].Attribute("Visibility")?.Value);
            Assert.Equal("Collapsed", controls["RetryArtworkSearch"].Attribute("Visibility")?.Value);
        });
    }

    private static void Scenario(string name, Func<Task> action) => Test(name, () =>
    {
        using var queue = new UiQueue();
        queue.Run(action);
    });

    private static CoverPickerDialog Picker(CoverDialog owner, RemoteImageResult result,
        string type = EmbyImageStore.Thumb, string itemType = EmbyItemType.Movie) =>
        new(new EmbyItem { Id = "fixture-item", Name = "离线图片示例", Type = itemType }, ItemArtwork.Panel.Single(kind => kind.ImageType == type), result, owner);

    private static RemoteImageInfo Image(string name, string provider) =>
        new() { Url = $"https://fixture.invalid/{name}.png", ProviderName = provider, Width = 1920, Height = 1080, Language = "zh" };

    private static RemoteImageResult Results(string name, params string[] providers) => new()
    {
        Providers = [.. providers],
        Images = [Image(name, providers.FirstOrDefault() ?? "FanArt")]
    };

    private static ObservableCollection<CoverChoice> Rows(CoverPickerDialog picker) =>
        (ObservableCollection<CoverChoice>)picker.Grid.ItemsSource!;

    private static async Task YieldUi() { await Task.Yield(); await Task.Yield(); }

    private static async Task Close(CoverPickerDialog picker)
    {
        picker.CancelLoads();
        await picker.SearchSettledAsync();
        await picker.PreviewsSettledAsync();
        await YieldUi();
    }

    private sealed record SearchRequest(string Type, RemoteImageSearch Search, CancellationToken Token)
    {
        internal TaskCompletionSource<RemoteImageResult> Completion { get; } = new();
    }
}
