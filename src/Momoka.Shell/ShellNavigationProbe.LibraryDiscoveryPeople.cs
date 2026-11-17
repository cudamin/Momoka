using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LibraryDiscoveryPeopleAsync()
    {
        using var fixture = CreateFixture();
        var server = new DiscoveryPeopleServer(await FixtureImageAsync(400, 600));
        fixture.Transport.Reply = server.ReplyAsync;
        await fixture.SelectAsync("discovery-people.invalid", kind: MediaServerKind.Plex);
        fixture.Settings.AggregateMode = false;
        fixture.Settings.Ui.EnableImmersive = false;
        var source = fixture.Session.Capture();
        var shell = DiscoveryShell(fixture);
        fixture.Actions.CaptureOpen = shell.OpenItem;
        var frame = Get<Frame>(shell, "ContentFrame");
        _root.Children.Add(shell);
        var evidence = new List<object>();
        try
        {
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 900);
                foreach (var field in new[] { "actor", "director", "writer", "genre" })
                {
                    (frame.Content as IShellContent)?.Release();
                    Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, source.Bind(new EmbyItem
                    { Id = "101", Name = "星海旅人", Type = EmbyItemType.Movie }))), "Plex 人物详情导航失败");
                    var page = (DetailPage)frame.Content;
                    page.AttachWindow(_window!);
                    await UntilAsync(() => page.IsReady && !page.ViewModel.Busy);
                    await LayoutAsync(page);
                    var cast = page.ViewModel.CastShelf;
                    Require(!page.ViewModel.NoticeOpen && cast?.Cards.Count == 4, "Plex 详情没有显示演员、导演、编剧和无标识署名");
                    Require(page.ViewModel.Directors?.Contains("导演乙", StringComparison.Ordinal) == true,
                        "Plex 导演说明没有映射");
                    Require(cast?.Cards.FirstOrDefault()?.Item.Name == "导演乙",
                        "Plex 导演没有排在演职人员第一位");
                    var castPanel = Get<StackPanel>(page, "CastPanel");
                    Require(castPanel.Visibility == Visibility.Visible, "Plex 人物区域仍被隐藏");
                    await ScrollFnNasDetailPanelAsync(page, shell, castPanel);
                    await UntilAsync(() => cast!.Cards.Where(card => card.Item.Id.Length > 0).All(card => card.Poster is not null));
                    var before = fixture.Actions.Opened;
                    page.ViewModel.OpenCard(cast!.Cards.Single(card => card.Item.Id.Length == 0));
                    Require(fixture.Actions.Opened == before && ReferenceEquals(frame.Content, page),
                        "缺少服务器标识的人物仍能触发导航");
                    await SaveProbeScreenAsync($"discovery-people-{field}-{width}-screen.png");
                    if (field == "genre") page.ViewModel.OpenGenre("动画");
                    else page.ViewModel.OpenCard(cast.Cards.Single(card => card.Item.Name == (field switch
                    { "actor" => "演员甲", "director" => "导演乙", _ => "编剧丙" })));
                    await UntilAsync(() => frame.Content is LibraryPage works && works.IsReady && !works.ViewModel.Busy);
                    var opened = (LibraryPage)frame.Content;
                    Require(!opened.ViewModel.NoticeOpen && opened.ViewModel.Cards.Count == 1
                        && opened.ViewModel.Cards[0].Item.Id == "501", "Plex 关系入口没有得到当前库作品");
                    Require(server.Fields.LastOrDefault() == field
                        && (opened.NavigationRequest as LibraryRequest)?.Source?.Key == source.Key,
                        "人物字段或捕获来源在导航中丢失");
                    await LayoutAsync(opened);
                    await SaveProbeScreenAsync($"discovery-works-{field}-{width}-screen.png");
                    evidence.Add(new { Width = width, Relation = field, SameLibrary = true, Items = 1 });
                }
            }
            Require(fixture.Actions.Plays == 0, "Plex 人物探针触发了播放");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            frame.Content = null;
            fixture.Actions.CaptureOpen = null;
            Get<DispatcherTimer>(shell, "_toast").Stop();
            _root.Children.Remove(shell);
        }
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "discovery-people-receipt.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class DiscoveryPeopleServer(byte[] poster)
    {
        public List<string> Fields { get; } = [];
        public Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Require(request.Method == HttpMethod.Get, "Plex 人物探针拒绝写入");
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)
                || path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal)) return Reply(new(HttpStatusCode.NotFound));
            if (path.EndsWith("/identity", StringComparison.Ordinal) || path is "/media" or "/media/")
                return Reply(Json(new { MediaContainer = new { machineIdentifier = "discovery-people", friendlyName = "匿名 Plex", version = "1.41.2" } }));
            if (path.EndsWith("/library/sections", StringComparison.Ordinal))
                return Reply(Json(new { MediaContainer = new { Directory = new[] { new { key = "1", type = "movie", title = "影片", filters = false } } } }));
            if (path.EndsWith("/library/metadata/101", StringComparison.Ordinal))
                return Reply(Json(new
                {
                    MediaContainer = new
                    {
                        Metadata = new[] { new
                {
                    ratingKey = "101", type = "movie", title = "星海旅人", summary = "匿名 Plex 人物关联与来源库验收。",
                    librarySectionID = 1, thumb = "/library/metadata/101/thumb/1", year = 2025,
                    Role = new object[] { new { id = 11, tag = "演员甲", role = "领航员", thumb = "/library/metadata/11/thumb/1" }, new { tag = "无标识署名" } },
                    Director = new { id = "12", tag = "导演乙", thumb = "/library/metadata/12/thumb/1" },
                    Writer = new[] { new { id = 13, tag = "编剧丙", thumb = "/library/metadata/13/thumb/1" } },
                    Genre = new[] { new { id = 7, tag = "动画" } }
                } }
                    }
                }));
            if (path.Contains("/thumb/", StringComparison.Ordinal))
                return Reply(new(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            if (path.EndsWith("/similar", StringComparison.Ordinal))
                return Reply(Json(new { MediaContainer = new { Metadata = Array.Empty<object>() } }));
            if (path.EndsWith("/library/sections/1/all", StringComparison.Ordinal))
            {
                var relation = new[] { "actor", "director", "writer", "genre" }.SingleOrDefault(field => Query(request, field) is not null);
                Require(relation is not null && Query(request, "type") == "1", "Plex 关系请求缺少字段或库内媒体类型");
                Fields.Add(relation!);
                Require(Query(request, relation!) == (relation switch { "actor" => "11", "director" => "12", "writer" => "13", _ => "7" }),
                    "Plex 关系查询使用了显示名称而非原生标识");
                return Reply(Json(new
                {
                    MediaContainer = new
                    {
                        totalSize = 1,
                        Metadata = new[] { new
                { ratingKey = "501", type = "movie", title = "星海 关联作品", librarySectionID = 1, thumb = "/library/metadata/501/thumb/1" } }
                    }
                }));
            }
            throw new InvalidOperationException("Plex 人物探针出现未登记的读取：" + path);
        }
        private static Task<HttpResponseMessage> Reply(HttpResponseMessage value) => Task.FromResult(value);
    }
}
