using System.Net;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class PlexMediaTests
{
    private static void RegisterBrowsePeople()
    {
        Test("Plex 人物关系：音乐与播放列表不生成电影类型关联", () =>
        {
            foreach (var type in new[] { "artist", "album", "track", "playlist" })
            {
                var item = PlexMediaAdapter.Item(JsonNode.Parse($$"""
                    {"ratingKey":"40","type":"{{type}}","title":"音乐","librarySectionID":"9",
                     "Genre":[{"id":"7","tag":"流行"}],"Director":[{"id":"12","tag":"署名"}]}
                    """)!.AsObject());
                Assert.Equal(0, item.PlexGenres.Count);
                Assert.Equal(0, item.People.Count);
                Assert.Equal("流行", item.Genres.Single());
            }
        });
        Test("Plex 人物：演员导演编剧保留署名与头像，同名不同身份不合并", () =>
        {
            var item = PlexMediaAdapter.Item(JsonNode.Parse("""
                {"ratingKey":"10","title":"电影","type":"movie","librarySectionID":"3",
                 "Role":[{"id":41,"tag":"同名演员","role":"主角","thumb":"/library/metadata/41/thumb"},
                         {"id":"42","tag":"同名演员","role":"配角"}],
                 "Director":{"id":43,"tag":"导演甲"},"Writer":{"id":"44","tag":"编剧甲"}}
                """)!.AsObject());
            Assert.Equal(4, item.People.Count);
            Assert.Equal("Actor,Actor,Director,Writer", string.Join(',', item.People.Select(person => person.Type)));
            Assert.Equal("主角", item.People[0].Credit);
            Assert.Equal("/library/metadata/41/thumb", item.People[0].PrimaryImageTag);
            var cast = ItemDetail.Cast(item);
            Assert.Equal(4, cast.Count);
            Assert.Equal("导演甲,同名演员,同名演员,编剧甲", string.Join(',', cast.Select(credit => credit.Card.Name)));
        });
        Test("Plex 人物：没有服务器身份仍显示署名，既有后端无身份仍过滤", () =>
        {
            var item = PlexMediaAdapter.Item(JsonNode.Parse("""
                {"ratingKey":"10","title":"电影","type":"movie","librarySectionID":"3",
                 "Role":{"tag":"可读演员","role":"主角"},"Writer":{"tag":"可读编剧"}}
                """)!.AsObject());
            Assert.Equal(2, item.People.Count);
            Assert.Equal("可读演员,可读编剧", string.Join(',', ItemDetail.Cast(item).Select(credit => credit.Card.Name)));
            Assert.Equal(0, ItemDetail.Cast(new EmbyItem
            {
                People = [new EmbyPerson { Name = "旧无身份演员", Type = "Actor" }]
            }).Count);
        });
        Test("Plex 人物：同库同署名去重，跨库及署名类型保留独立身份", () =>
        {
            static EmbyItem Read(string section)
            {
                var value = JsonNode.Parse("""
                {"ratingKey":"10","title":"电影","type":"movie","Role":[{"id":"41","tag":"同一个人"},
                {"id":41,"tag":"同一个人"}],"Director":{"id":41,"tag":"同一个人"},"Genre":{"id":7,"tag":"动画"}}
                """)!.AsObject();
                value["librarySectionID"] = section;
                return PlexMediaAdapter.Item(value);
            }
            var first = Read("3");
            var second = Read("4");
            Assert.Equal(2, ItemDetail.Cast(first).Count);
            Assert.False(first.People[0].Id == second.People[0].Id);
            Assert.False(first.People[0].Id == first.People[2].Id);
            Assert.Equal(new PlexBrowseRelation("3", 1, "genre", "7"), first.PlexGenres["动画"]);
        });
        Test("Plex 人物：头像偏好先过滤，缺id有图可读，无section不制造关系", () =>
        {
            var item = PlexMediaAdapter.Item(JsonNode.Parse("""
                {"ratingKey":"10","type":"movie","Role":[{"id":41,"tag":"无图"},
                {"tag":"有图无id","thumb":"/library/metadata/41/thumb"}]}
                """)!.AsObject());
            Assert.Equal(2, ItemDetail.Cast(item).Count);
            var card = ItemDetail.Cast(item, portraitsOnly: true).Single().Card;
            Assert.Equal("有图无id", card.Name);
            Assert.True(card.IsDisplayOnly);
            Assert.Null(card.PlexRelation);
            Assert.True(item.People.All(person => person.PlexRelation is null));
            Assert.Equal(2, ItemDetail.Cast(item).Count);
        });
        foreach (var field in new[] { "actor", "director", "writer", "genre" })
            Test("Plex 关联：指定库和媒体类型保留原生条件及分页头 " + field, () =>
            {
                using var transport = new RelationTransport(field);
                using var http = new PlexHttp(transport);
                var connection = new PlexConnection(new Uri("https://plex-relation.invalid/proxy/"), "placeholder", "7", "fixture", "fixture", "1");
                var adapter = new PlexMediaAdapter(new PlexClient(http), connection);
                var result = adapter.GetItemsAsync(new ItemQuery
                {
                    ParentId = "3",
                    PlexRelation = new PlexBrowseRelation("3", 2, field, "41"),
                    StartIndex = 12,
                    Limit = 7,
                    IncludeItemTypes = [EmbyItemType.Movie]
                }, default).GetAwaiter().GetResult();
                Assert.Equal("30", result.Items.Single().Id);
                Assert.Equal(28, result.TotalRecordCount);
                Assert.Equal(1, transport.Requests);
            });
        Test("Plex 关联：非法身份与跨库请求不发HTTP", () =>
        {
            using var transport = new RelationTransport("actor");
            using var http = new PlexHttp(transport);
            var adapter = new PlexMediaAdapter(new PlexClient(http),
                new PlexConnection(new Uri("https://plex-relation.invalid/proxy/"), "placeholder", "7", "fixture", "fixture", "1"));
            foreach (var relation in new[]
            {
                new PlexBrowseRelation("3", 2, "actor", ""), new PlexBrowseRelation("3", 2, "actor", "../41"),
                new PlexBrowseRelation("3", 2, "actor", "41&genre=2"), new PlexBrowseRelation("3", 2, "actor", "-1"),
                new PlexBrowseRelation("3", 2, "actor", "0"), new PlexBrowseRelation("3", 2, "label", "41"),
                new PlexBrowseRelation("3", 4, "actor", "41"), new PlexBrowseRelation("4", 2, "actor", "41")
            })
                Assert.Throws<NotSupportedException>(() => adapter.GetItemsAsync(new ItemQuery
                { ParentId = "3", PlexRelation = relation }, default).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Requests);
        });
        Test("Plex 人物：头像读取同源限制不因人物路径放宽", () =>
        {
            using var transport = new RelationTransport("actor");
            using var http = new PlexHttp(transport);
            var adapter = new PlexMediaAdapter(new PlexClient(http),
                new PlexConnection(new Uri("https://plex-relation.invalid/proxy/"), "placeholder", "7", "fixture", "fixture", "1"));
            Assert.Throws<ArgumentException>(() => adapter.GetImageAsync("https://portrait-external.invalid/person.jpg", 120, default)
                .GetAwaiter().GetResult());
            Assert.Equal(0, transport.Requests);
        });
    }

    private sealed class RelationTransport(string field) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal("https://plex-relation.invalid/proxy/library/sections/3/all", request.RequestUri!.GetLeftPart(UriPartial.Path));
            Assert.Contains("type=2", request.RequestUri.Query);
            Assert.Contains(field + "=41", request.RequestUri.Query);
            Assert.Equal("12", request.Headers.GetValues("X-Plex-Container-Start").Single());
            Assert.Equal("7", request.Headers.GetValues("X-Plex-Container-Size").Single());
            Assert.Equal("placeholder", request.Headers.GetValues("X-Plex-Token").Single());
            Assert.DoesNotContain("placeholder", request.RequestUri.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"MediaContainer":{"totalSize":28,"Metadata":[{"ratingKey":"30","title":"节目","type":"show"}]}}""")
            });
        }
    }
}
