using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed record MoviePilotMediaSeason(int Number, string Name, int EpisodeCount, string AirDate, string PosterUrl, double? Vote)
{
    public string Label => Number == 0 ? "第 0 季 · 特别篇" : $"第 {Number} 季";

    /// <summary>选季保留来源的专用季名；剔除上游常见的重复季号标题。</summary>
    public string PickerLabel
    {
        get
        {
            var name = Name.Trim();
            var normalized = string.Concat(name.Where(character => !char.IsWhiteSpace(character))).ToLowerInvariant();
            var generic = new[] { $"第{Number}季", $"season{Number}", $"season{Number:00}", $"s{Number}", $"s{Number:00}" };
            return $"第 {Number} 季" + (name.Length > 0 && !generic.Contains(normalized, StringComparer.Ordinal) ? " · " + name : "");
        }
    }

    public static IReadOnlyList<MoviePilotMediaSeason> Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array) throw new MoviePilotException("MoviePilot 未返回有效的季列表");
        return data.EnumerateArray().Where(item => MoviePilotSubscription.Number(item, "season_number") is >= 0)
            .Select(item => new MoviePilotMediaSeason(MoviePilotSubscription.Number(item, "season_number")!.Value,
                MoviePilotSubscription.Text(item, "name"), MoviePilotSubscription.Number(item, "episode_count") ?? 0,
                MoviePilotSubscription.Text(item, "air_date"), MoviePilotMediaDetail.Image(MoviePilotSubscription.Text(item, "poster_path")),
                MoviePilotSubscription.Real(item, "vote_average")))
            .DistinctBy(item => item.Number).OrderBy(item => item.Number == 0 ? int.MaxValue : item.Number).ToArray();
    }
}

public sealed record MoviePilotMediaEpisode(int Number, string Name, string Overview, string AirDate, string ImageUrl, double? Vote)
{
    public string Title => $"{Number} - {Name}";
    public static IReadOnlyList<MoviePilotMediaEpisode> Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array) throw new MoviePilotException("MoviePilot 未返回有效的集列表");
        return data.EnumerateArray().Where(item => MoviePilotSubscription.Number(item, "episode_number") is > 0)
            .Select(item => new MoviePilotMediaEpisode(MoviePilotSubscription.Number(item, "episode_number")!.Value,
                MoviePilotSubscription.Text(item, "name"), MoviePilotSubscription.Text(item, "overview"),
                MoviePilotSubscription.Text(item, "air_date"), MoviePilotMediaDetail.Image(MoviePilotSubscription.Text(item, "still_path")),
                MoviePilotSubscription.Real(item, "vote_average")))
            .DistinctBy(item => item.Number).OrderBy(item => item.Number).ToArray();
    }
}

public sealed record MoviePilotMediaLink(string Label, string Url);

public sealed record MoviePilotMediaFact(string Label, string Value, bool Cloud = false, string ReleaseCountry = "", bool Last = false);

public sealed record MoviePilotMediaDetail(MoviePilotMedia Media, JsonElement Data)
{
    public bool SupportsSeasons => Media.Type == "电视剧";
    public bool SupportsEpisodeGroups => SupportsSeasons && Media.MediaSource == "themoviedb" && Media.MediaId?.All(char.IsAsciiDigit) == true;
    public double? Vote => MoviePilotSubscription.Real(Data, "vote_average");
    public string Tagline => MoviePilotSubscription.Text(Data, "tagline");
    public IReadOnlyList<MoviePilotMediaSeason> Seasons => MoviePilotSubscription.Member(Data, "season_info") is { ValueKind: JsonValueKind.Array } seasons
        ? MoviePilotMediaSeason.Parse(seasons) : [];
    public string Genres => Names(MoviePilotSubscription.Member(Data, "genres"));

    /// <summary>出品国家，已按详情页的顿号连接；订阅页的 AniBT 入口据此判断日本动画。</summary>
    public string Countries => Names(MoviePilotSubscription.Member(Data, "production_countries"));

    /// <summary>原始语言（TMDB 的 ISO 639-1 代码，例如 <c>ja</c>）；来源不提供时是空串。</summary>
    public string OriginalLanguage => Text("original_language");

    public IReadOnlyList<MoviePilotMediaFact> Facts
    {
        get
        {
            var facts = new List<MoviePilotMediaFact>();
            // 网页以辅助 TMDB ID 优先选资料卡；这里只投影展示，不改变订阅的主来源身份。
            var tmdb = Text("tmdb_id");
            if (tmdb.Length == 0 && Media.MediaSource == "themoviedb") tmdb = Media.MediaId ?? "";
            if (tmdb.Length > 0)
            {
                Add("ID", tmdb);
                Add("原始标题", Text("original_title", "original_name"));
                Add("状态", Text("status"));
                Add("上映日期", Text("release_date", "first_air_date"), cloud: true);
                if (Media.Type == "电影") { Release("数字发行", 4); Release("实体发行", 5); }
                Add("原始语言", OriginalLanguage);
                Add("出品国家", Names(MoviePilotSubscription.Member(Data, "production_countries"), "\n"));
                Add("制作公司", Names(MoviePilotSubscription.Member(Data, "production_companies"), "\n"));
            }
            else if (Text("douban_id").Length > 0 || Media.MediaSource == "douban")
            {
                Add("豆瓣 ID", Text("douban_id") is { Length: > 0 } id ? id : Media.MediaId ?? "");
                Add("原始标题", Text("original_title"));
                Add("上映日期", Text("release_date"));
                Add("出品国家", Names(MoviePilotSubscription.Member(Data, "production_countries"), "\n"));
            }
            else if (Text("bangumi_id", "anilist_id").Length > 0 || Media.MediaSource is "bangumi" or "anilist")
            {
                Add("ID", Text("bangumi_id"));
                Add("ID", Text("anilist_id"));
                if (facts.Count == 0) Add("ID", Media.MediaId ?? "");
                Add("原始标题", Text("original_title"));
                Add("上映日期", Text("release_date"));
            }
            if (facts.Count > 0) facts[^1] = facts[^1] with { Last = true };
            return facts;
            void Add(string label, string value, bool cloud = false, string country = "")
            {
                if (value.Length > 0) facts.Add(new(label, value, cloud, country));
            }
            void Release(string label, int type)
            {
                var releases = MoviePilotSubscription.Member(Data, "release_dates");
                if (releases.ValueKind != JsonValueKind.Array) return;
                var earliest = releases.EnumerateArray().Where(item => MoviePilotSubscription.Number(item, "type") == type)
                    .Select(item => (Date: MoviePilotSubscription.Text(item, "date"), Country: MoviePilotSubscription.Text(item, "iso_code")))
                    .Where(item => DateTimeOffset.TryParse(item.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    .OrderBy(item => DateTimeOffset.Parse(item.Date, CultureInfo.InvariantCulture)).FirstOrDefault();
                if (earliest.Date is { Length: >= 10 }) Add(label, earliest.Date[..10], country: earliest.Country);
            }
        }
    }
    public IReadOnlyList<MoviePilotMediaLink> Links
    {
        get
        {
            var links = new List<MoviePilotMediaLink>();
            Link("TMDB", "tmdb_id", "https://www.themoviedb.org/" + (Media.Type == "电视剧" ? "tv/" : "movie/"));
            Link("豆瓣", "douban_id", "https://movie.douban.com/subject/");
            Link("IMDb", "imdb_id", "https://www.imdb.com/title/");
            Link("Bangumi", "bangumi_id", "https://bgm.tv/subject/");
            Link("AniList", "anilist_id", "https://anilist.co/anime/");
            if (Text("tvdb_id") is { Length: > 0 } tvdb) links.Add(new("TVDB", "https://www.thetvdb.com/?tab=series&id=" + Uri.EscapeDataString(tvdb)));
            return links;
            void Link(string label, string key, string prefix)
            {
                if (Text(key) is { Length: > 0 } id) links.Add(new(label, prefix + Uri.EscapeDataString(id)));
            }
        }
    }
    public string Text(params string[] keys) => keys.Select(key => MoviePilotSubscription.Text(Data, key)).FirstOrDefault(value => value.Length > 0) ?? "";

    public static MoviePilotMediaDetail Parse(JsonElement data, MoviePilotMedia expected)
    {
        var parsed = MoviePilotMediaParser.Parse(JsonSerializer.SerializeToElement(new[] { data })).SingleOrDefault();
        if (parsed is null || !parsed.CanSubscribe)
            throw new MoviePilotMediaNotRecognizedException();
        if (!MoviePilotSubscriptionPlan.SameMedia(parsed, expected))
            throw new MoviePilotException("媒体详情身份不匹配，请重新打开页面");
        return new(parsed with
        {
            Season = null,
            ConnectionStamp = expected.ConnectionStamp,
            PosterUrl = Image(parsed.PosterUrl ?? ""),
            BackdropUrl = Image(MoviePilotSubscription.Text(data, "backdrop_path"))
        }, data.Clone());
    }

    internal static string Image(string value) => value.StartsWith('/') ? "https://image.tmdb.org/t/p/original" + value : value;
    private static string Names(JsonElement data, string separator = "、") => data.ValueKind == JsonValueKind.Array
        ? string.Join(separator, data.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : MoviePilotSubscription.Text(item, "name")).Where(value => !string.IsNullOrEmpty(value)))
        : data.ValueKind == JsonValueKind.String ? data.GetString() ?? "" : "";
}
