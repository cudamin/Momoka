using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Momoka.LocalMedia;

public static partial class LocalMediaOrganizer
{
    /// <summary>
    /// 执行原地入库。视频只做「仍在原处且未变化」的核对，绝不改名、移动或创建链接；
    /// 需要写入的只有元数据目录里的 NFO 和图片。
    /// </summary>
    public static async Task<LocalMediaResult> ExecuteAsync(LocalMediaPlan plan,
        Func<string, CancellationToken, Task<byte[]>> imageFetch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(imageFetch);
        var results = new List<LocalMediaItemResult>();
        if (!plan.Consume()) return new([new(plan.VideoPath, LocalMediaOutputKind.Video, false, "本次计划已使用或取消，请重新扫描和确认")], false);
        using var leases = new LocalMediaFiles();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var expected in plan.Snapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = leases.Read(expected.Path);
                var current = LocalMediaFiles.Snapshot(expected.Path, stream, cancellationToken);
                if (current != expected) throw new IOException("源视频已改变，请重新扫描和确认");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!LocalMediaFiles.Exists(plan.VideoPath)) throw new IOException("源视频已不在原处，未入库");
            RequireTargets(plan, leases);
            foreach (var directory in plan.Sidecars.Select(item => Path.GetDirectoryName(item.Path)!).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                leases.Directories(directory, create: true);
            }
            leases.Recheck();
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(new(plan.VideoPath, LocalMediaOutputKind.Video, true, "已入库（原文件保留）"));
            foreach (var sidecar in plan.Sidecars)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    leases.Recheck();
                    if (LocalMediaFiles.Exists(sidecar.Path))
                    {
                        if (SharedSeries(sidecar, plan))
                        {
                            RequireSeriesIdentity(plan, leases);
                            _ = leases.Read(sidecar.Path);
                            results.Add(new(sidecar.Path, sidecar.Kind, true, "已核对并复用同作品文件", sidecar.Optional));
                            continue;
                        }
                        // 头像按 TMDB 人物编号命名、只落在本作品自己的目录里：存在就是上一轮写好的那一张。
                        if (sidecar.Kind == LocalMediaOutputKind.PersonImage)
                        {
                            _ = leases.Read(sidecar.Path);
                            results.Add(new(sidecar.Path, sidecar.Kind, true, "已核对并复用同作品文件", sidecar.Optional));
                            continue;
                        }
                        // 重试未完成条目时，上一轮已经写好的 NFO 只有身份完全一致才复用；别的作品的同名文件仍然拒写。
                        if (SameNfo(sidecar, plan, leases))
                        {
                            results.Add(new(sidecar.Path, sidecar.Kind, true, "已核对并复用同作品文件", sidecar.Optional));
                            continue;
                        }
                        throw new IOException("元数据已存在，未覆盖");
                    }
                    byte[] bytes;
                    if (sidecar.ImageUrl.Length > 0)
                    {
                        bytes = await imageFetch(sidecar.ImageUrl, cancellationToken).ConfigureAwait(false);
                        if (bytes.Length is 0 or > 64 * 1024 * 1024) throw new IOException("图片为空或超过64 MiB，未写入");
                        RequireImageType(sidecar.Path, bytes);
                    }
                    else bytes = Nfo(plan.Metadata, sidecar.Kind);
                    cancellationToken.ThrowIfCancellationRequested();
                    leases.Recheck();
                    LocalMediaFiles.WriteCompleted(sidecar.Path, bytes);
                    results.Add(new(sidecar.Path, sidecar.Kind, true, "已完成", sidecar.Optional));
                }
                catch (OperationCanceledException) { throw; }
                // The image reader is supplied by the chosen metadata provider. Its domain-specific
                // exception must still become a partial receipt after the video was registered.
                catch (Exception error)
                { results.Add(new(sidecar.Path, sidecar.Kind, false, error.Message, sidecar.Optional)); }
            }
            return new(Array.AsReadOnly(results.ToArray()), false);
        }
        catch (OperationCanceledException) { return new(Array.AsReadOnly(results.ToArray()), true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or XmlException)
        {
            results.Add(new(plan.VideoPath, LocalMediaOutputKind.Video, false, error.Message));
            return new(Array.AsReadOnly(results.ToArray()), false);
        }
    }

    private static void RequireImageType(string path, ReadOnlySpan<byte> bytes)
    {
        var actual = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 ? ".jpg"
            : bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? ".png"
                : bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8) ? ".webp" : "";
        if (actual.Length == 0) throw new IOException("图片格式无法识别，仅接受 JPEG、PNG 或 WEBP；未写入图片");
        if (!actual.Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase))
            throw new IOException("图片实际格式与预览目标后缀不一致；未写入图片，请重新核对图片来源");
    }

    /// <summary>元数据全部在库目录里：只核对要写的文件，视频本来就该在原处。</summary>
    private static void RequireTargets(LocalMediaPlan plan, LocalMediaFiles leases)
    {
        foreach (var sidecar in plan.Sidecars)
        {
            leases.Directories(Path.GetDirectoryName(sidecar.Path)!, create: false);
            if (!LocalMediaFiles.Exists(sidecar.Path)) continue;
            if (SharedSeries(sidecar, plan))
            {
                RequireSeriesIdentity(plan, leases);
                _ = leases.Read(sidecar.Path);
                continue;
            }
            if (sidecar.Kind == LocalMediaOutputKind.PersonImage)
            {
                _ = leases.Read(sidecar.Path);
                continue;
            }
            if (SameNfo(sidecar, plan, leases)) continue;
            throw new IOException($"元数据已存在，未覆盖：{sidecar.Path}");
        }
    }

    /// <summary>
    /// 已有的 NFO 是不是这一份计划要写的那一个作品。只认 NFO 里的可信编号与季集号：
    /// 那张文件可能是上一次入库写成、后来图片失败留下的，重试时应当复用；同名但不是同一作品的仍拒写。
    /// </summary>
    private static bool SameNfo(LocalMediaSidecar sidecar, LocalMediaPlan plan, LocalMediaFiles leases)
    {
        if (sidecar.Kind is not (LocalMediaOutputKind.EpisodeNfo or LocalMediaOutputKind.MovieNfo)) return false;
        var expectedRoot = sidecar.Kind == LocalMediaOutputKind.EpisodeNfo ? "episodedetails" : "movie";
        var expectedId = sidecar.Kind == LocalMediaOutputKind.EpisodeNfo ? plan.Metadata.EpisodeId : plan.Metadata.MediaId;
        if (expectedId.Length == 0) return false;
        try
        {
            using var stream = leases.Read(sidecar.Path);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false });
            var document = XDocument.Load(reader);
            var root = document.Root;
            if (root?.Name != expectedRoot) return false;
            var ids = root.Elements("uniqueid").ToArray();
            if (ids.Length != 1 || ids[0].Attribute("type")?.Value != NfoSource(plan.Metadata.MediaSource)
                || ids[0].Value != expectedId) return false;
            return sidecar.Kind != LocalMediaOutputKind.EpisodeNfo
                || Number(root, "season") == plan.Metadata.Season && Number(root, "episode") == plan.Metadata.Episode;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        {
            return false;
        }
    }

    private static int? Number(XElement root, string name) =>
        int.TryParse(root.Element(name)?.Value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private static bool SharedSeries(LocalMediaSidecar sidecar, LocalMediaPlan plan) =>
        plan.Metadata.MediaType == LocalMediaType.Episode &&
        sidecar.Kind is LocalMediaOutputKind.SeriesNfo or LocalMediaOutputKind.Poster or LocalMediaOutputKind.Backdrop
            or LocalMediaOutputKind.PersonImage;

    private static void RequireSeriesIdentity(LocalMediaPlan plan, LocalMediaFiles leases)
    {
        var file = plan.Sidecars.Single(item => item.Kind == LocalMediaOutputKind.SeriesNfo).Path;
        var stream = leases.Read(file);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false });
        var document = XDocument.Load(reader);
        var ids = document.Root?.Elements("uniqueid").ToArray() ?? [];
        if (document.Root?.Name != "tvshow" || ids.Length != 1 || ids[0].Attribute("type")?.Value != NfoSource(plan.Metadata.MediaSource) || ids[0].Value != plan.Metadata.MediaId)
            throw new IOException("目标系列目录属于其他作品或无法核对身份，未混用元数据");
    }

    private static byte[] Nfo(LocalMediaMetadata metadata, LocalMediaOutputKind kind)
    {
        var episode = kind == LocalMediaOutputKind.EpisodeNfo;
        var root = new XElement(episode ? "episodedetails" : kind == LocalMediaOutputKind.SeriesNfo ? "tvshow" : "movie",
            new XElement("title", episode && metadata.EpisodeTitle.Length > 0 ? metadata.EpisodeTitle : metadata.Title));
        if (metadata.Year is { } year) root.Add(new XElement("year", year));
        if (episode)
        {
            root.Add(new XElement("showtitle", metadata.Title), new XElement("season", metadata.Season), new XElement("episode", metadata.Episode));
            if (metadata.EpisodeAired is { } aired) root.Add(new XElement("aired", aired.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        }
        else if (metadata.Premiered is { } premiered) root.Add(new XElement("premiered", premiered.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        var overview = episode && metadata.EpisodeOverview.Length > 0 ? metadata.EpisodeOverview : metadata.Overview;
        root.Add(new XElement("plot", overview));
        if (!episode && metadata.Tagline.Length > 0) root.Add(new XElement("tagline", metadata.Tagline));
        if (!episode && metadata.RuntimeMinutes is > 0 and var runtime) root.Add(new XElement("runtime", runtime));
        if (metadata.CommunityRating is { } rating)
            root.Add(new XElement("ratings", new XElement("rating", new XAttribute("name", NfoSource(metadata.MediaSource)),
                new XAttribute("max", "10"), new XAttribute("default", "true"),
                new XElement("value", rating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))));
        foreach (var genre in metadata.Genres) root.Add(new XElement("genre", genre));
        foreach (var studio in metadata.Studios) root.Add(new XElement("studio", studio));
        foreach (var person in metadata.People)
            root.Add(person.Director
                ? new XElement("director", person.Name)
                : new XElement("actor", new XElement("name", person.Name), new XElement("role", person.Role), new XElement("type", "Actor")));
        var id = episode ? metadata.EpisodeId : metadata.MediaId;
        if (id.Length > 0) root.Add(new XElement("uniqueid", new XAttribute("type", NfoSource(metadata.MediaSource)), new XAttribute("default", "true"), id));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) document.Save(writer);
        return buffer.ToArray();
    }

    private static string NfoSource(string source) => source is "themoviedb" or "tmdb" ? "tmdb" : source;
}
