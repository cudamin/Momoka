using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 匿名 HTTP 执行生产搜索、选择草稿与生成命令。请求和完整结果以 MoviePilot v3.2.9
/// MediaDetailView.vue、resource.vue、useSearchPagination.ts 的调用契约为依据。
/// </summary>
internal static class DetailSearchParityTests
{
    internal static void Register()
    {
        foreach (var area in new[] { "title", "imdbid" })
        {
            Test("详情搜索：原生身份、类型、季和站点送进精确分页入口 " + area, () =>
            {
                using var fixture = new ParityFixture();
                using var vm = fixture.Resources();
                ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search(area)));
                var request = fixture.Transport.SearchRequests.Single();
                Assert.Equal("/api/v1/search/media/42/stream", request.AbsolutePath);
                CheckQuery(request, "media_source", "themoviedb");
                CheckQuery(request, "mtype", "电视剧");
                CheckQuery(request, "area", area);
                CheckQuery(request, "title", "匿名影片");
                CheckQuery(request, "year", "2024");
                CheckQuery(request, "season", "2");
                CheckQuery(request, "sites", "2");
                CheckQuery(request, "manual_paging", "true");
                CheckQuery(request, "page", "0");
                Assert.False(Query(request).ContainsKey("keyword"));
                Assert.False(Query(request).ContainsKey("episode"));
                Assert.True(vm.Versions.Select(row => row.Resource.Title).SequenceEqual(["第一页 E02", "第一页 E03"]));
                Assert.False(vm.SearchProgress.IsActive);
                Assert.False(vm.NoticeOpen);
                Assert.Equal(0, fixture.Saves);
            });
        }

        Test("详情搜索：单集入口显示网页端首轮完整清单", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = fixture.Resources();
            ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("title", episode: 2)));
            Assert.Equal(2, vm.Versions.Count, "资源接口按季检索，客户端不能自动把网页完整清单筛成单集");
            Assert.Equal(MoviePilotResourceBrowserViewModel.AllEpisodes, vm.Browser.SelectedEpisode);
            Assert.False(Query(fixture.Transport.SearchRequests.Single()).ContainsKey("episode"));
        });

        Test("详情协议：直接调用保留显式站点用于分页，不读取本机选择覆盖请求", () =>
        {
            using var fixture = new ParityFixture();
            var search = fixture.Search("imdbid");
            var progress = new ImmediateProgress();
            ResourceFixture.Complete(fixture.Service.SearchDetailResourcesAsync(search, CancellationToken.None, progress));
            var request = fixture.Transport.SearchRequests.Single();
            CheckQuery(request, "sites", "1");
            CheckQuery(request, "area", "imdbid");
            Assert.True(fixture.Service.SearchSitePreference().SiteIds.SequenceEqual([2]));
            Assert.Equal(0, fixture.Saves);
        });

        Test("详情搜索：特别篇零季保留，电影不带季参数", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = fixture.Resources();
            var series = fixture.Search("title") with { Media = fixture.Media with { Season = 0 } };
            ResourceFixture.Complete(vm.SearchDetailAsync(series));
            CheckQuery(fixture.Transport.SearchRequests.Last(), "season", "0");
            var movie = series with
            {
                Media = fixture.Media with { Type = "电影", Season = 0 },
                Sites = new(fixture.Service.ConnectionStamp, [1])
            };
            ResourceFixture.Complete(vm.SearchDetailAsync(movie));
            CheckQuery(fixture.Transport.SearchRequests.Last(), "mtype", "电影");
            Assert.False(Query(fixture.Transport.SearchRequests.Last()).ContainsKey("season"));
        });

        Test("详情搜索：缓存恢复及同词刷新保留 IMDb 原生身份和站点", () =>
        {
            using var fixture = new ParityFixture();
            var cache = new MoviePilotResourceSearchCache();
            using (var first = fixture.Resources())
            {
                first.UseCache(cache);
                ResourceFixture.Complete(first.SearchDetailAsync(fixture.Search("imdbid")));
            }
            var before = fixture.Transport.SearchRequests.Count;
            using var restored = fixture.Resources();
            restored.UseCache(cache);
            Assert.True(restored.RestoreCached("匿名影片"));
            Assert.Equal(before, fixture.Transport.SearchRequests.Count, "缓存恢复应直接显示保存的完整结果");
            Assert.Equal(2, restored.Versions.Count);
            ResourceFixture.Complete(restored.SearchAsync("匿名影片"));
            var request = fixture.Transport.SearchRequests.Last();
            Assert.Equal("/api/v1/search/media/42/stream", request.AbsolutePath);
            CheckQuery(request, "area", "imdbid");
            CheckQuery(request, "sites", "2");
        });

        Test("详情搜索：修改关键词后清除影片身份并使用关键词入口", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = fixture.Resources();
            ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("imdbid")));
            ResourceFixture.Complete(vm.SearchAsync("另一个词"));
            var request = fixture.Transport.SearchRequests.Last();
            Assert.Equal("/api/v1/search/title/stream", request.AbsolutePath);
            CheckQuery(request, "keyword", "另一个词");
            Assert.False(Query(request).ContainsKey("media_source"));
            Assert.False(Query(request).ContainsKey("area"));
            Assert.Equal("2", vm.SearchSites.Selection!.QueryValue());
            Assert.True(fixture.Service.SearchSitePreference().SiteIds.SequenceEqual([2]));
        });

        foreach (var type in new[] { "电影", "电视剧" })
        {
            Test("详情选站：资源与字幕共同沿用不限类型的本机偏好 " + type, () =>
            {
                using var fixture = new ParityFixture();
                fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, type, false, [1]);
                var saves = fixture.Saves;
                var search = fixture.Search("imdbid") with { Media = fixture.Media with { Type = type } };
                using var resources = fixture.Resources();
                ResourceFixture.Complete(resources.SearchDetailAsync(search));
                using var subtitles = new MoviePilotSubtitleSearchViewModel();
                subtitles.Attach(fixture.Service);
                ResourceFixture.Complete(subtitles.LoadSubtitlesAsync(search.Media, null, search with { Options = new(Subtitles: true) }));
                foreach (var picker in new[] { resources.SearchSites, subtitles.SearchSites })
                {
                    Assert.Equal(2, picker.Sites.Count);
                    Assert.Equal("2", picker.Selection!.QueryValue(), "详情随不限类型的本机偏好起步");
                    Assert.False(picker.Sites.Any(row => row.Id == 3));
                }
                Assert.True(fixture.Transport.Reads.Any(uri => uri.AbsolutePath == "/api/v1/site/"));
                Assert.False(fixture.Transport.Reads.Any(uri => uri.AbsolutePath.StartsWith("/api/v1/site/media/", StringComparison.Ordinal)));
                Assert.False(fixture.Transport.Reads.Any(uri => uri.AbsolutePath == "/api/v1/system/setting/public/IndexerSites"));
                Assert.True(fixture.Transport.SearchRequests.All(uri => Query(uri)["sites"] == "2"));
                Assert.Equal(saves, fixture.Saves);
                Assert.True(fixture.Service.SearchSitePreference().SiteIds.SequenceEqual([2]));
            });
        }

        Test("详情选站：取消草稿保留偏好，确认保存并同步资源与字幕", () =>
        {
            using var fixture = new ParityFixture();
            using var picker = new MoviePilotSitePickerViewModel();
            picker.Attach(fixture.Service);
            ResourceFixture.Complete(picker.ReloadAsync());
            using var resources = fixture.Resources();
            ResourceFixture.Complete(resources.SearchDetailAsync(fixture.Search("title")));
            using var subtitles = new MoviePilotSubtitleSearchViewModel();
            subtitles.Attach(fixture.Service);
            var search = fixture.Search("title") with { Options = new(Subtitles: true) };
            ResourceFixture.Complete(subtitles.LoadSubtitlesAsync(search.Media, null, search));
            var requests = fixture.Transport.SearchRequests.Count;
            using (var cancelled = picker.CreateDraft())
            {
                cancelled.ClearSelectionCommand.Execute(null);
                cancelled.Sites.Single(row => row.Id == 1).Selected = true;
                Assert.Equal("1", cancelled.Selection!.QueryValue());
                cancelled.Cancel();
            }
            Assert.Equal("2", picker.Selection!.QueryValue());
            Assert.Equal(0, fixture.Saves);
            using (var confirmed = picker.CreateDraft())
            {
                confirmed.ClearSelectionCommand.Execute(null);
                confirmed.Sites.Single(row => row.Id == 1).Selected = true;
                Assert.True(confirmed.CommitSelection());
            }
            Assert.Equal("1", picker.Selection!.QueryValue());
            Assert.Equal("1", resources.SearchSites.Selection!.QueryValue());
            Assert.Equal("1", subtitles.SearchSites.Selection!.QueryValue());
            Assert.Equal(1, fixture.Saves);
            Assert.True(fixture.Service.SearchSitePreference().SiteIds.SequenceEqual([1]));
            Assert.Equal(requests, fixture.Transport.SearchRequests.Count, "选站确认只保存，既有面板等待用户重搜");
            Assert.Equal(2, resources.Versions.Count);
            Assert.Equal(2, subtitles.Subtitles.Count);
            Assert.False(resources.LoadMoreCommand.CanExecute(null));
        });

        Test("详情选站：空目录保留本机显式偏好并阻止扩大搜索", () =>
        {
            using var fixture = new ParityFixture();
            fixture.Transport.EmptyCatalogue = true;
            using var resources = fixture.Resources();
            ResourceFixture.Complete(resources.SearchDetailAsync(fixture.Search("title")));
            using var subtitles = new MoviePilotSubtitleSearchViewModel();
            subtitles.Attach(fixture.Service);
            var search = fixture.Search("title") with { Options = new(Subtitles: true) };
            ResourceFixture.Complete(subtitles.LoadSubtitlesAsync(search.Media, null, search));
            Assert.Equal(0, resources.SearchSites.Sites.Count);
            Assert.False(resources.SearchSites.HasSelection);
            Assert.False(subtitles.SearchSites.HasSelection);
            Assert.True(resources.ShowEmptyNotice && subtitles.ShowEmptyNotice);
            Assert.Equal(0, fixture.Transport.SearchRequests.Count);
            Assert.True(fixture.Service.SearchSitePreference().SiteIds.SequenceEqual([2]));
            Assert.Equal(0, fixture.Saves);
        });

        Test("详情选站：本机默认模式由服务器决定范围，旧导航站点不覆盖默认", () =>
        {
            using var fixture = new ParityFixture(useDefault: true);
            using var resources = fixture.Resources();
            var search = fixture.Search("title");
            ResourceFixture.Complete(resources.SearchDetailAsync(search));
            using var subtitles = new MoviePilotSubtitleSearchViewModel();
            subtitles.Attach(fixture.Service);
            ResourceFixture.Complete(subtitles.LoadSubtitlesAsync(search.Media, null, search with { Options = new(Subtitles: true) }));
            Assert.True(resources.SearchSites.UseDefault && subtitles.SearchSites.UseDefault);
            Assert.Null(resources.SearchSites.Selection);
            Assert.Null(subtitles.SearchSites.Selection);
            Assert.True(fixture.Transport.SearchRequests.All(uri => !Query(uri).ContainsKey("sites")));
            Assert.False(fixture.Transport.Reads.Any(uri => uri.AbsolutePath.StartsWith("/api/v1/site/", StringComparison.Ordinal)));
            Assert.Equal(0, fixture.Saves);
        });

        Test("详情分页：下一页完整追加且保留排序与已选筛选", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = fixture.Resources();
            ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("title")));
            var rows = vm.Versions;
            var first = rows[0];
            vm.Browser.SiteFacet.Choices.Single(choice => choice.Value == "站点乙").Selected = true;
            vm.Browser.SortIndex = 2;
            vm.Browser.SortDescending = false;
            Assert.True(vm.LoadMoreCommand.CanExecute(null));
            ResourceFixture.Complete(vm.LoadMoreCommand.ExecuteAsync(null));
            Assert.True(ReferenceEquals(rows, vm.Versions));
            Assert.True(vm.Versions.Contains(first));
            Assert.Equal(3, vm.Versions.Count);
            Assert.True(vm.Browser.SiteFacet.Selected.SequenceEqual(["站点乙"]), "追加结果不能清除用户筛选");
            Assert.Equal(2, vm.Browser.SortIndex);
            Assert.False(vm.Browser.SortDescending);
            Assert.Equal(Visibility.Collapsed, vm.MoreVisibility);
            var request = fixture.Transport.SearchRequests.Last();
            CheckQuery(request, "source", "fixture-source");
            CheckQuery(request, "page", "1");
            CheckQuery(request, "sites", "2");
        });

        foreach (var position in Enum.GetValues<PageSummaryPosition>())
        {
            Test("详情分页：分页摘要在完整快照或末批，done省略sources仍可续页 " + position, () =>
            {
                using var fixture = new ParityFixture();
                fixture.Transport.SummaryPosition = position;
                using var vm = fixture.Resources();
                ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("title")));
                Assert.Equal(2, vm.Versions.Count);
                Assert.True(vm.LoadMoreCommand.CanExecute(null));
                Assert.Equal(Visibility.Visible, vm.MoreVisibility);
                ResourceFixture.Complete(vm.LoadMoreCommand.ExecuteAsync(null));
                Assert.Equal(3, vm.Versions.Count);
                CheckQuery(fixture.Transport.SearchRequests.Last(), "source", "fixture-source");
                CheckQuery(fixture.Transport.SearchRequests.Last(), "page", "1");
                Assert.False(vm.LoadMoreCommand.CanExecute(null));
            });
        }

        Test("详情分页：快照带摘要后断流，普通接口回退不留下无效续页", () =>
        {
            using var fixture = new ParityFixture();
            fixture.Transport.UnfinishedPageSummary = true;
            using var vm = fixture.Resources();
            ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("title")));
            Assert.Equal(2, vm.Versions.Count);
            Assert.False(vm.LoadMoreCommand.CanExecute(null));
            Assert.Equal(Visibility.Collapsed, vm.MoreVisibility);
            Assert.False(vm.NoticeOpen);
            Assert.Equal(2, fixture.Transport.SearchRequests.Count);
            Assert.Equal("/api/v1/search/media/42", fixture.Transport.SearchRequests.Last().AbsolutePath);
        });

        Test("详情分页：改站点保留当前结果并撤销旧范围续页", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = fixture.Resources();
            ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("title")));
            Assert.True(vm.LoadMoreCommand.CanExecute(null));
            vm.SearchSites.Sites.Single(row => row.Id == 2).Selected = false;
            vm.SearchSites.Sites.Single(row => row.Id == 1).Selected = true;
            Assert.Equal(2, vm.Versions.Count);
            Assert.False(vm.LoadMoreCommand.CanExecute(null), "旧搜索范围的续页必须等待按新站点重搜");
            Assert.Equal(Visibility.Collapsed, vm.MoreVisibility);
            Assert.Equal(1, fixture.Transport.SearchRequests.Count);
        });

        Test("详情分页：改站点后的缓存恢复保留新范围，旧续页继续失效", () =>
        {
            using var fixture = new ParityFixture();
            var cache = new MoviePilotResourceSearchCache();
            using (var first = fixture.Resources())
            {
                first.UseCache(cache);
                ResourceFixture.Complete(first.SearchDetailAsync(fixture.Search("imdbid")));
                first.SearchSites.Sites.Single(row => row.Id == 2).Selected = false;
                first.SearchSites.Sites.Single(row => row.Id == 1).Selected = true;
            }
            var before = fixture.Transport.SearchRequests.Count;
            using var restored = fixture.Resources();
            restored.UseCache(cache);
            Assert.True(restored.RestoreCached("匿名影片"));
            Assert.Equal(before, fixture.Transport.SearchRequests.Count);
            Assert.Equal(2, restored.Versions.Count);
            ResourceFixture.Complete(restored.SearchSites.PrepareSelectionAsync());
            Assert.Equal("1", restored.SearchSites.Selection!.QueryValue());
            Assert.False(restored.LoadMoreCommand.CanExecute(null));
            Assert.Equal(Visibility.Collapsed, restored.MoreVisibility);
            ResourceFixture.Complete(restored.ReloadAsync());
            CheckQuery(fixture.Transport.SearchRequests.Last(), "area", "imdbid");
            CheckQuery(fixture.Transport.SearchRequests.Last(), "sites", "1");
            Assert.Equal(2, fixture.Saves);
        });

        Test("详情缓存：本机偏好已改变时保留旧结果，按当前范围重搜且禁用旧续页", () =>
        {
            using var fixture = new ParityFixture();
            var cache = new MoviePilotResourceSearchCache();
            using (var first = fixture.Resources())
            {
                first.UseCache(cache);
                ResourceFixture.Complete(first.SearchDetailAsync(fixture.Search("imdbid")));
                Assert.True(first.LoadMoreCommand.CanExecute(null));
            }
            fixture.Service.SaveSearchSitePreference(fixture.Service.ConnectionStamp, null, false, [1]);
            var before = fixture.Transport.SearchRequests.Count;
            using var restored = fixture.Resources();
            restored.UseCache(cache);
            Assert.True(restored.RestoreCached("匿名影片"));
            Assert.Equal(before, fixture.Transport.SearchRequests.Count);
            Assert.Equal(2, restored.Versions.Count);
            Assert.Equal(Visibility.Collapsed, restored.MoreVisibility);
            Assert.False(restored.LoadMoreCommand.CanExecute(null));
            ResourceFixture.Complete(restored.SearchSites.PrepareSelectionAsync());
            Assert.Equal("1", restored.SearchSites.Selection!.QueryValue());
            ResourceFixture.Complete(restored.SearchAsync("匿名影片"));
            Assert.Equal("/api/v1/search/media/42/stream", fixture.Transport.SearchRequests.Last().AbsolutePath);
            CheckQuery(fixture.Transport.SearchRequests.Last(), "area", "imdbid");
            CheckQuery(fixture.Transport.SearchRequests.Last(), "sites", "1");
            CheckQuery(fixture.Transport.SearchRequests.Last(), "page", "0");
            Assert.Equal(1, fixture.Saves);
        });

        Test("详情分页：失败后重试同一页，不重复已接收结果", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = fixture.Resources();
            ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("title")));
            fixture.Transport.FailContinuation = true;
            ResourceFixture.Complete(vm.LoadMoreCommand.ExecuteAsync(null));
            Assert.True(vm.NoticeOpen);
            Assert.Equal(2, vm.Versions.Count);
            Assert.True(vm.LoadMoreCommand.CanExecute(null));
            fixture.Transport.FailContinuation = false;
            ResourceFixture.Complete(vm.LoadMoreCommand.ExecuteAsync(null));
            Assert.Equal(3, vm.Versions.Count);
            Assert.True(fixture.Transport.SearchRequests.Skip(1).All(uri => Query(uri)["page"] == "1"));
            Assert.False(fixture.Transport.SearchRequests.Any(uri => uri.AbsolutePath == "/api/v1/search/media/42"));
        });

        Test("详情分页：一个来源失败继续加载其他来源，再单独重试失败页", () =>
        {
            using var fixture = new ParityFixture();
            fixture.Transport.MultipleSources = true;
            using var vm = fixture.Resources();
            ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("title")));
            fixture.Transport.FailPrimarySource = true;
            ResourceFixture.Complete(vm.LoadMoreCommand.ExecuteAsync(null));
            Assert.True(vm.NoticeOpen);
            Assert.Equal(3, vm.Versions.Count);
            Assert.True(vm.Versions.Any(row => row.Title == "第二来源 E04"));
            Assert.True(vm.LoadMoreCommand.CanExecute(null));
            var loaded = fixture.Transport.SearchRequests.Skip(1).ToArray();
            Assert.Equal(2, loaded.Length);
            CheckQuery(loaded[0], "source", "primary-source");
            CheckQuery(loaded[1], "source", "secondary-source");
            fixture.Transport.FailPrimarySource = false;
            ResourceFixture.Complete(vm.LoadMoreCommand.ExecuteAsync(null));
            Assert.Equal(4, vm.Versions.Count);
            Assert.Equal(4, fixture.Transport.SearchRequests.Count);
            CheckQuery(fixture.Transport.SearchRequests.Last(), "source", "primary-source");
            CheckQuery(fixture.Transport.SearchRequests.Last(), "page", "1");
            Assert.False(vm.LoadMoreCommand.CanExecute(null));
            Assert.Equal(Visibility.Collapsed, vm.MoreVisibility);
        });

        foreach (var failure in Enum.GetValues<StreamFailure>().Where(failure => failure != StreamFailure.ExplicitRejection))
        {
            foreach (var subtitles in new[] { false, true })
            {
                Test("详情首轮：流连接或解析失败回退普通接口，完整结果保持一致 " + failure + " subtitles=" + subtitles, () =>
                {
                    using var fixture = new ParityFixture();
                    fixture.Transport.Failure = failure;
                    if (subtitles)
                    {
                        using var vm = new MoviePilotSubtitleSearchViewModel();
                        vm.Attach(fixture.Service);
                        var search = fixture.Search("title", episode: 3) with { Options = new(Episode: 3, Subtitles: true) };
                        ResourceFixture.Complete(vm.LoadSubtitlesAsync(search.Media, search.Options.Episode, search));
                        Assert.Equal(2, vm.Subtitles.Count);
                        Assert.False(vm.NoticeOpen);
                    }
                    else
                    {
                        using var vm = fixture.Resources();
                        ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("imdbid")));
                        Assert.Equal(2, vm.Versions.Count);
                        Assert.False(vm.NoticeOpen);
                        Assert.False(vm.LoadMoreCommand.CanExecute(null));
                    }
                    var requests = fixture.Transport.SearchRequests;
                    Assert.Equal(2, requests.Count);
                    var stem = "/api/v1/search/" + (subtitles ? "subtitle/" : "") + "media/42";
                    Assert.Equal(stem + "/stream", requests[0].AbsolutePath);
                    Assert.Equal(stem, requests[1].AbsolutePath);
                    CheckQuery(requests[1], "media_source", "themoviedb");
                    CheckQuery(requests[1], "mtype", "电视剧");
                    CheckQuery(requests[1], "season", "2");
                    CheckQuery(requests[1], "sites", "2");
                    Assert.False(Query(requests[1]).ContainsKey("manual_paging"));
                    if (subtitles) CheckQuery(requests[1], "episode", "3");
                    else CheckQuery(requests[1], "area", "imdbid");
                    Assert.Equal(0, fixture.Saves);
                });
            }
        }

        foreach (var subtitles in new[] { false, true })
        {
            Test("详情首轮：来源明确拒绝不重放搜索 subtitles=" + subtitles, () =>
            {
                using var fixture = new ParityFixture();
                fixture.Transport.Failure = StreamFailure.ExplicitRejection;
                if (subtitles)
                {
                    using var vm = new MoviePilotSubtitleSearchViewModel();
                    vm.Attach(fixture.Service);
                    var search = fixture.Search("title") with { Options = new(Subtitles: true) };
                    ResourceFixture.Complete(vm.LoadSubtitlesAsync(search.Media, null, search));
                    Assert.Equal(0, vm.Subtitles.Count);
                    Assert.True(vm.NoticeOpen);
                }
                else
                {
                    using var vm = fixture.Resources();
                    ResourceFixture.Complete(vm.SearchDetailAsync(fixture.Search("title")));
                    Assert.Equal(0, vm.Versions.Count);
                    Assert.True(vm.NoticeOpen);
                }
                Assert.Equal(1, fixture.Transport.SearchRequests.Count);
            });
        }

        foreach (var fallback in new[] { false, true })
        {
            Test("详情字幕：同一身份季集站点，SSE 与普通回退保留完整清单 fallback=" + fallback, () =>
            {
                using var fixture = new ParityFixture();
                fixture.Transport.SubtitleFallback = fallback;
                using var vm = new MoviePilotSubtitleSearchViewModel();
                vm.Attach(fixture.Service);
                var search = fixture.Search("title", episode: 3) with { Options = new(Episode: 3, Subtitles: true) };
                ResourceFixture.Complete(vm.LoadSubtitlesAsync(search.Media, search.Options.Episode, search));
                Assert.True(vm.Subtitles.Select(row => row.Subtitle.Title).SequenceEqual(["简体字幕", "未提供下载链接"]));
                Assert.Equal(0, vm.MediaResults.Count);
                Assert.False(vm.SearchProgress.IsActive);
                Assert.False(vm.NoticeOpen);
                var stream = fixture.Transport.SearchRequests.First();
                Assert.Equal("/api/v1/search/subtitle/media/42/stream", stream.AbsolutePath);
                foreach (var request in fixture.Transport.SearchRequests)
                {
                    CheckQuery(request, "media_source", "themoviedb");
                    CheckQuery(request, "mtype", "电视剧");
                    CheckQuery(request, "season", "2");
                    CheckQuery(request, "episode", "3");
                    CheckQuery(request, "sites", "2");
                }
                Assert.Equal(fallback ? 2 : 1, fixture.Transport.SearchRequests.Count);
                Assert.Equal(0, fixture.Saves);
            });
        }

        Test("详情字幕：同词刷新保留身份和选站，改词恢复影片查找", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = new MoviePilotSubtitleSearchViewModel();
            vm.Attach(fixture.Service);
            var search = fixture.Search("title", episode: 3) with { Options = new(Episode: 3, Subtitles: true) };
            ResourceFixture.Complete(vm.LoadSubtitlesAsync(search.Media, search.Options.Episode, search));
            ResourceFixture.Complete(vm.SearchAsync("匿名影片"));
            Assert.Equal(2, fixture.Transport.SearchRequests.Count);
            CheckQuery(fixture.Transport.SearchRequests.Last(), "episode", "3");
            CheckQuery(fixture.Transport.SearchRequests.Last(), "sites", "2");
            ResourceFixture.Complete(vm.SearchAsync("另一个词"));
            Assert.Equal("2", vm.SearchSites.Selection!.QueryValue());
            Assert.False(vm.HasSelectedMedia);
            Assert.Equal(0, vm.Subtitles.Count);
            Assert.Equal(2, vm.MediaResults.Count);
            CheckQuery(fixture.Transport.Reads.Last(), "title", "另一个词");
            Assert.Equal("/api/v1/media/search", fixture.Transport.Reads.Last().AbsolutePath);
        });

        Test("详情字幕：进入详情清掉旧片单，返回后同词找片完成且不递归", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = new MoviePilotSubtitleSearchViewModel();
            vm.Attach(fixture.Service);
            ResourceFixture.Complete(vm.SearchAsync("匿名影片"));
            Assert.Equal(2, vm.MediaResults.Count);
            var search = fixture.Search("title") with { Options = new(Subtitles: true) };
            ResourceFixture.Complete(vm.LoadSubtitlesAsync(search.Media, null, search));
            Assert.Equal(0, vm.MediaResults.Count);
            Assert.Equal(Visibility.Collapsed, vm.BackVisibility);
            vm.BackToMediaListCommand.Execute(null);
            Assert.False(vm.HasSelectedMedia);
            Assert.Equal("2", vm.SearchSites.Selection!.QueryValue());
            ResourceFixture.Complete(vm.SearchAsync("匿名影片"));
            Assert.Equal(2, vm.MediaResults.Count);
            Assert.Equal(0, vm.Subtitles.Count);
            Assert.Equal("/api/v1/media/search", fixture.Transport.Reads.Last().AbsolutePath);
            Assert.Equal(1, fixture.Transport.SearchRequests.Count);
        });

        Test("详情字幕：在飞时改站点作废旧返回，下次重搜使用新范围", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = new MoviePilotSubtitleSearchViewModel();
            vm.Attach(fixture.Service);
            var search = fixture.Search("title", episode: 3) with { Options = new(Episode: 3, Subtitles: true) };
            ResourceFixture.Complete(vm.LoadSubtitlesAsync(search.Media, search.Options.Episode, search));
            fixture.Transport.PendingSubtitle = new();
            var pending = fixture.Transport.PendingSubtitle;
            var old = vm.ReloadAsync();
            Assert.True(vm.Busy);
            Assert.True(vm.SearchProgress.IsActive);
            vm.SearchSites.Sites.Single(row => row.Id == 2).Selected = false;
            vm.SearchSites.Sites.Single(row => row.Id == 1).Selected = true;
            Assert.False(vm.Busy);
            Assert.False(vm.SearchProgress.IsActive);
            Assert.True(vm.NoticeOpen);
            pending.SetResult(ParityTransport.SubtitleResponse());
            CompletePending(old);
            Assert.Equal(0, vm.Subtitles.Count);
            fixture.Transport.PendingSubtitle = null;
            ResourceFixture.Complete(vm.SearchAsync("匿名影片"));
            Assert.Equal(2, vm.Subtitles.Count);
            CheckQuery(fixture.Transport.SearchRequests.Last(), "sites", "1");
            CheckQuery(fixture.Transport.SearchRequests.Last(), "episode", "3");
            Assert.Equal(2, fixture.Saves);
        });

        Test("详情字幕：离页后迟到返回无效，再进入同词查找影片", () =>
        {
            using var fixture = new ParityFixture();
            using var vm = new MoviePilotSubtitleSearchViewModel();
            vm.Attach(fixture.Service);
            var search = fixture.Search("title") with { Options = new(Subtitles: true) };
            fixture.Transport.PendingSubtitle = new();
            var pending = fixture.Transport.PendingSubtitle;
            var old = vm.LoadSubtitlesAsync(search.Media, null, search);
            vm.Release();
            pending.SetResult(ParityTransport.SubtitleResponse());
            CompletePending(old);
            Assert.False(vm.HasSelectedMedia);
            Assert.True(fixture.Service.SearchSitePreference().SiteIds.SequenceEqual([2]));
            Assert.Equal(0, vm.Subtitles.Count);
            Assert.Equal(0, vm.MediaResults.Count);
            Assert.False(vm.SearchProgress.IsActive);
            fixture.Transport.PendingSubtitle = null;
            vm.Attach(fixture.Service);
            ResourceFixture.Complete(vm.SearchAsync("匿名影片"));
            Assert.Equal(2, vm.MediaResults.Count);
            Assert.Equal(1, fixture.Transport.SearchRequests.Count);
        });
    }

    private static void CompletePending(Task task)
    {
        using var completion = new ManualResetEventSlim();
        _ = task.ContinueWith(_ => completion.Set(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Assert.True(completion.Wait(TimeSpan.FromSeconds(5)), "匿名搜索任务没有完成");
        ResourceFixture.Complete(task);
    }

    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2)).ToDictionary(part => Uri.UnescapeDataString(part[0]),
            part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : "", StringComparer.Ordinal);

    private static void CheckQuery(Uri request, string field, string expected) => Assert.Equal(expected, Query(request).GetValueOrDefault(field));

    private sealed class ImmediateProgress : IProgress<MoviePilotSearchProgress>
    {
        public void Report(MoviePilotSearchProgress value) { }
    }

    private enum StreamFailure
    {
        Forbidden,
        ServerError,
        WrongContentType,
        Malformed,
        IncompleteSnapshot,
        Disconnected,
        ExplicitRejection
    }

    private enum PageSummaryPosition
    {
        Replace,
        FinalBatch
    }

    private sealed class ParityFixture : IDisposable
    {
        private readonly MoviePilotClient _client;
        internal ParityTransport Transport { get; } = new();
        internal MoviePilotService Service { get; }
        internal MoviePilotMedia Media { get; }
        internal int Saves { get; private set; }

        internal ParityFixture(bool useDefault = false)
        {
            var settings = new AppSettings();
            settings.MoviePilot.Enabled = true;
            settings.MoviePilot.Url = "http://192.0.2.10:3001";
            settings.MoviePilot.Username = "anonymous-fixture";
            settings.MoviePilot.ProtectedPassword = "not-a-secret-fixture";
            _client = new(Transport);
            Service = new(_client, new MoviePilotCredentials(new RejectSecretAccess()), settings, () => Saves++);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", flags)!.Invoke(Service, null)!;
            var sessionType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
            var session = Activator.CreateInstance(sessionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
            typeof(MoviePilotService).GetField("_session", flags)!.SetValue(Service, session);
            typeof(MoviePilotService).GetField("_availableIdentity", flags)!.SetValue(Service, identity);
            if (!useDefault) Service.SaveSearchSitePreference(Service.ConnectionStamp, null, false, [2]);
            Saves = 0;
            Media = new() { Title = "匿名影片", Type = "电视剧", MediaSource = "themoviedb", MediaId = "42", Season = 2, Year = 2024 };
            typeof(MoviePilotMedia).GetProperty("ConnectionStamp", flags)!.SetValue(Media, Service.ConnectionStamp);
        }

        internal MoviePilotDetailSearch Search(string area, int? episode = null) => new(Media, new(area, episode),
            new(Service.ConnectionStamp, [1]));

        internal MoviePilotVersionsViewModel Resources()
        {
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(Service, new FixtureLauncher());
            return vm;
        }

        public void Dispose()
        {
            Service.Dispose();
            _client.Dispose();
        }
    }

    private sealed class RejectSecretAccess : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("匿名夹具拒绝保存凭据");
        public string Unprotect(string cipherText) => throw new AssertionException("匿名夹具拒绝解密或登录");
    }

    private sealed class ParityTransport : HttpMessageHandler
    {
        internal List<Uri> Reads { get; } = [];
        internal List<Uri> SearchRequests { get; } = [];
        internal bool EmptyCatalogue { get; set; }
        internal bool SubtitleFallback { get; set; }
        internal bool FailContinuation { get; set; }
        internal bool MultipleSources { get; set; }
        internal bool FailPrimarySource { get; set; }
        internal StreamFailure? Failure { get; set; }
        internal PageSummaryPosition? SummaryPosition { get; set; }
        internal bool UnfinishedPageSummary { get; set; }
        internal TaskCompletionSource<HttpResponseMessage>? PendingSubtitle { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method, "匿名夹具拒绝登录、下载和其他写请求");
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.DoesNotContain("anonymous-token", request.RequestUri!.AbsoluteUri);
            var uri = request.RequestUri;
            Reads.Add(uri);
            var path = uri.AbsolutePath;
            if (path == "/api/v1/media/search") return Reply(new[]
            {
                new { title = "匿名影片", media_source = "themoviedb", media_id = "42", type = "电视剧", year = 2024 },
                new { title = "匿名同名影片", media_source = "themoviedb", media_id = "43", type = "电影", year = 2022 }
            });
            if (path == "/api/v1/system/setting/public/IndexerSites") return Reply(new { value = new[] { 1, 3, 1 } });
            if (path.StartsWith("/api/v1/site/", StringComparison.Ordinal))
            {
                var sites = new[]
                {
                    new { id = 1, name = "站点甲", is_active = true },
                    new { id = 2, name = "站点乙", is_active = true },
                    new { id = 3, name = "停用站点", is_active = false }
                };
                return Reply(EmptyCatalogue ? sites.Take(0) : path.EndsWith("/movie", StringComparison.Ordinal) ? sites.Where(site => site.id != 2) : sites);
            }
            if (!path.StartsWith("/api/v1/search/", StringComparison.Ordinal))
                throw new AssertionException("匿名夹具拒绝未登记读取");
            SearchRequests.Add(uri);
            if (path.EndsWith("/stream", StringComparison.Ordinal) && Failure is { } failure)
            {
                switch (failure)
                {
                    case StreamFailure.Forbidden:
                    case StreamFailure.ServerError:
                        return Task.FromResult(new HttpResponseMessage(failure == StreamFailure.Forbidden
                            ? HttpStatusCode.Forbidden : HttpStatusCode.InternalServerError)
                        {
                            Content = new StringContent("{\"message\":\"匿名流连接失败\"}", Encoding.UTF8, "application/json")
                        });
                    case StreamFailure.WrongContentType:
                        return Reply(Array.Empty<object>());
                    case StreamFailure.Malformed:
                        return EventStream("not-json");
                    case StreamFailure.IncompleteSnapshot:
                        return EventStream("{\"type\":\"append\",\"items\":[{}]}");
                    case StreamFailure.Disconnected:
                        throw new HttpRequestException("匿名流连接已断开");
                    case StreamFailure.ExplicitRejection:
                        return EventStream("{\"type\":\"error\",\"message\":\"来源拒绝\"}");
                }
            }
            if (path.StartsWith("/api/v1/search/subtitle/media/", StringComparison.Ordinal))
            {
                if (path.EndsWith("/stream", StringComparison.Ordinal) && SubtitleFallback)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                if (path.EndsWith("/stream", StringComparison.Ordinal) && PendingSubtitle is { } pending) return pending.Task;
                return path.EndsWith("/stream", StringComparison.Ordinal) ? Stream(SubtitleItems(), []) : Reply(SubtitleItems());
            }
            var query = Query(uri);
            var continuation = query.GetValueOrDefault("page") == "1";
            if (continuation && (FailContinuation || FailPrimarySource && query.GetValueOrDefault("source") == "primary-source"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var items = continuation
                ? new[] { Resource(query.GetValueOrDefault("source") == "secondary-source" ? "第二来源 E04" : "第二页 E04", 4, 4096) }
                : new[] { Resource("第一页 E02", 2, 2048), Resource("第一页 E03", 3, 1024) };
            object[] sources = continuation ? [] : [new { source = "fixture-source", page = 0, can_continue = true, error = (string?)null }];
            if (!continuation && MultipleSources) sources =
            [
                new { source = "primary-source", page = 0, can_continue = true, error = (string?)null },
                new { source = "secondary-source", page = 0, can_continue = true, error = (string?)null }
            ];
            if (!path.EndsWith("/stream", StringComparison.Ordinal)) return Reply(items);
            if (UnfinishedPageSummary) return EventStream(JsonSerializer.Serialize(new { type = "replace", items, sources }));
            return Stream(items, sources, continuation ? null : SummaryPosition);
        }

        internal static HttpResponseMessage SubtitleResponse() => StreamResponse(SubtitleItems(), []);

        private static object[] SubtitleItems() =>
        [
            new { title = "简体字幕", site_name = "站点乙", site = 2, language = "zh", enclosure = "https://fixture.invalid/subtitle" },
            new { title = "未提供下载链接", site_name = "站点乙", site = 2 }
        ];

        private static object Resource(string title, int episode, long size) => new
        {
            torrent_info = new { title, site_name = "站点乙", size, enclosure = "https://fixture.invalid/torrent/" + episode, seeders = 12 },
            media_info = new { title = "匿名影片", media_source = "themoviedb", media_id = "42", type = "电视剧", year = 2024 },
            meta_info = new { begin_season = 2, begin_episode = episode, resource_pix = "1080p" },
            match_status = "exact",
            media_info_is_target = true
        };

        private static Task<HttpResponseMessage> Reply(object value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        });

        private static Task<HttpResponseMessage> EventStream(params string[] events) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Concat(events.Select(value => "data: " + value + "\n\n")), Encoding.UTF8, "text/event-stream")
        });

        private static Task<HttpResponseMessage> Stream(object items, object[] sources, PageSummaryPosition? position = null) =>
            Task.FromResult(StreamResponse(items, sources, position));

        private static HttpResponseMessage StreamResponse(object items, object[] sources, PageSummaryPosition? position = null)
        {
            var preview = JsonSerializer.Serialize(new { type = "append", items = new[] { new { torrent_info = new { title = "未验证候选" } } } });
            var replace = JsonSerializer.Serialize(new { type = "replace", items, sources });
            var done = position is null ? JsonSerializer.Serialize(new { type = "done", sources }) : "{\"type\":\"done\"}";
            if (position == PageSummaryPosition.FinalBatch)
            {
                var results = JsonSerializer.SerializeToElement(items).EnumerateArray().ToArray();
                var first = JsonSerializer.Serialize(new { type = "replace", replace_batch = true, batch_index = 0,
                    batch_count = 2, total_items = results.Length, items = results.Take(1).ToArray() });
                var last = JsonSerializer.Serialize(new { type = "append", replace_batch = true, batch_index = 1,
                    batch_count = 2, total_items = results.Length, items = results.Skip(1).ToArray(), sources });
                replace = first + "\n\ndata: " + last;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: " + preview + "\n\n" + "data: " + replace + "\n\n" + "data: " + done + "\n\n",
                    Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
