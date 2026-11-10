using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Momoka.LocalMedia;

public static partial class LocalMediaOrganizer
{
    public static async Task<LocalMediaResult> ExecuteAsync(LocalMediaPlan plan,
        Func<string, CancellationToken, Task<byte[]>> imageFetch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(imageFetch);
        var results = new List<LocalMediaItemResult>();
        if (!plan.Consume()) return new([new(plan.TargetPath, LocalMediaOutputKind.VideoLink, false, "本次计划已使用或取消，请重新扫描和确认")], false);
        using var leases = new LocalMediaFiles();
        var reads = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var expected in plan.Snapshots)
            {
                var stream = leases.Read(expected.Path, rename: plan.Mode == LocalMediaLinkMode.Rename);
                var current = LocalMediaFiles.Snapshot(expected.Path, stream);
                if (current != expected) throw new IOException("源视频或字幕已改变，请重新扫描和确认");
                reads.Add(expected.Path, stream);
            }
            RequireTargets(plan, leases);
            foreach (var directory in new[] { Path.GetDirectoryName(plan.TargetPath)! }.Concat(plan.Sidecars.Select(item => Path.GetDirectoryName(item.Path)!)).Distinct(StringComparer.OrdinalIgnoreCase))
                leases.Directories(directory, create: true);
            leases.Recheck();
            CreateMedia(plan.Source.Path, plan.TargetPath, plan.Mode, reads[plan.Source.Path], leases);
            results.Add(new(plan.TargetPath, LocalMediaOutputKind.VideoLink, true, plan.Mode == LocalMediaLinkMode.Rename ? "已改名" : "已建立链接"));
            foreach (var sidecar in plan.Sidecars)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    leases.Recheck();
                    if (sidecar.Kind == LocalMediaOutputKind.SubtitleLink)
                        CreateMedia(sidecar.SourcePath, sidecar.Path, plan.Mode, reads[sidecar.SourcePath], leases);
                    else if (LocalMediaFiles.Exists(sidecar.Path) && SharedSeries(sidecar, plan))
                    {
                        RequireSeriesIdentity(plan, leases);
                        _ = leases.Read(sidecar.Path);
                        results.Add(new(sidecar.Path, sidecar.Kind, true, "已核对并复用同作品文件"));
                        continue;
                    }
                    else
                    {
                        if (LocalMediaFiles.Exists(sidecar.Path)) throw new IOException("目标已存在，未覆盖");
                        byte[] bytes;
                        if (sidecar.ImageUrl.Length > 0)
                        {
                            bytes = await imageFetch(sidecar.ImageUrl, cancellationToken).ConfigureAwait(false);
                            if (bytes.Length is 0 or > 64 * 1024 * 1024) throw new IOException("图片为空或超过64 MiB，未写入");
                        }
                        else bytes = Nfo(plan.Metadata, sidecar.Kind);
                        cancellationToken.ThrowIfCancellationRequested();
                        leases.Recheck();
                        LocalMediaFiles.WriteCompleted(sidecar.Path, bytes);
                    }
                    results.Add(new(sidecar.Path, sidecar.Kind, true, "已完成"));
                }
                catch (OperationCanceledException) { throw; }
                // The image reader is supplied by the chosen metadata provider. Its domain-specific
                // exception must still become a partial receipt after the local link was created.
                catch (Exception error)
                { results.Add(new(sidecar.Path, sidecar.Kind, false, error.Message)); }
            }
            return new(Array.AsReadOnly(results.ToArray()), false);
        }
        catch (OperationCanceledException) { return new(Array.AsReadOnly(results.ToArray()), true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or XmlException)
        {
            results.Add(new(plan.TargetPath, LocalMediaOutputKind.VideoLink, false, error.Message));
            return new(Array.AsReadOnly(results.ToArray()), false);
        }
    }

    private static void CreateMedia(string source, string target, LocalMediaLinkMode mode, FileStream sourceLease, LocalMediaFiles leases)
    {
        leases.Recheck();
        if (mode == LocalMediaLinkMode.Rename && source.Equals(target, StringComparison.OrdinalIgnoreCase)) return;
        if (LocalMediaFiles.Exists(target)) throw new IOException($"目标已存在，未覆盖：{target}");
        if (mode == LocalMediaLinkMode.HardLink) LocalMediaFiles.HardLink(source, target);
        else if (mode == LocalMediaLinkMode.Rename) LocalMediaFiles.Rename(sourceLease.SafeFileHandle, target);
        else
        {
            try { File.CreateSymbolicLink(target, source); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { throw new IOException("软链接创建失败，请开启 Windows 开发者模式或使用具有创建软链接权限的账号；没有改用硬链接或复制。" + error.Message, error); }
        }
    }

    private static void RequireTargets(LocalMediaPlan plan, LocalMediaFiles leases)
    {
        leases.Directories(Path.GetDirectoryName(plan.TargetPath)!, create: false);
        if (LocalMediaFiles.Exists(plan.TargetPath) && !(plan.Mode == LocalMediaLinkMode.Rename && plan.Source.Path.Equals(plan.TargetPath, StringComparison.OrdinalIgnoreCase)))
            throw new IOException($"目标已存在，未覆盖：{plan.TargetPath}");
        foreach (var sidecar in plan.Sidecars)
        {
            leases.Directories(Path.GetDirectoryName(sidecar.Path)!, create: false);
            if (!LocalMediaFiles.Exists(sidecar.Path)) continue;
            if (plan.Mode == LocalMediaLinkMode.Rename && sidecar.Kind == LocalMediaOutputKind.SubtitleLink && sidecar.SourcePath.Equals(sidecar.Path, StringComparison.OrdinalIgnoreCase)) continue;
            if (!SharedSeries(sidecar, plan)) throw new IOException($"目标已存在，未覆盖：{sidecar.Path}");
            RequireSeriesIdentity(plan, leases);
            _ = leases.Read(sidecar.Path);
        }
    }

    private static bool SharedSeries(LocalMediaSidecar sidecar, LocalMediaPlan plan) =>
        plan.Mode != LocalMediaLinkMode.Rename && plan.Metadata.MediaType == LocalMediaType.Episode &&
        sidecar.Kind is LocalMediaOutputKind.SeriesNfo or LocalMediaOutputKind.Poster or LocalMediaOutputKind.Backdrop;

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
        var id = episode ? metadata.EpisodeId : metadata.MediaId;
        if (id.Length > 0) root.Add(new XElement("uniqueid", new XAttribute("type", NfoSource(metadata.MediaSource)), new XAttribute("default", "true"), id));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) document.Save(writer);
        return buffer.ToArray();
    }

    private static string NfoSource(string source) => source is "themoviedb" or "tmdb" ? "tmdb" : source;
}
