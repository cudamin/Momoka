using System.Text.Json;
using Momoka.Diagnostics;
using Momoka.Emby;

namespace Momoka.MoviePilot;

/// <summary>
/// 「手动整理」那一半 MoviePilot 功能（partial，另一半会话管理在 <see cref="MoviePilotService"/>）。
/// <para>
/// 协议按上游 v3.0.1 核对过（2026-09-24，源码快照在 <c>work/moviepilot-protocol-20260924-M8nFhS</c>）：
/// 目的路径匹配 <c>transfer/manual/target-path</c>、成功历史 <c>transfer/manual/history</c>、执行
/// <c>transfer/manual?background=…</c>（<c>preview:true</c> 只出预览、不动文件）。这一层不猜路径：
/// 源路径先经 <c>storage/list</c> 换成 MoviePilot 自己认的 FileItem，换不出一个真实文件就停，绝不退而
/// 整理父目录 —— 那种「兜底」会把整个目录的文件搬走。
/// </para>
/// <para>
/// 只支持本地存储的源文件：MoviePilot 是另一台机器（或容器），Emby 报来的路径对它可见与否只有服务器
/// 自己知道，<c>storage/list</c> 就是那道问询。
/// </para>
/// </summary>
public sealed partial class MoviePilotService
{
    /// <summary>存储、媒体库目录和刮削源：表单那几颗下拉的内容。</summary>
    public async Task<MoviePilotTransferOptions> TransferOptionsAsync(CancellationToken cancellationToken)
    {
        var identity = CaptureIdentity();
        var storages = MoviePilotTransfer.Array(await CallAsync((apiBase, token, client) =>
            client.GetAsync(apiBase, token, "storage/options", cancellationToken), cancellationToken, identity)
            .ConfigureAwait(false))
            .Where(item => MoviePilotTransfer.Text(item, "name").Length > 0)
            .Select(item => new MoviePilotStorage(
                MoviePilotTransfer.Text(item, "name"), MoviePilotTransfer.Text(item, "type")))
            .ToList();

        var directories = MoviePilotTransfer.Array(await CallAsync((apiBase, token, client) =>
            client.GetAsync(apiBase, token, "storage/directories?directory_type=library", cancellationToken),
            cancellationToken, identity).ConfigureAwait(false))
            .Where(item => MoviePilotTransfer.Text(item, "library_path").Length > 0)
            .Select(item => new MoviePilotDirectory(
                MoviePilotTransfer.Text(item, "library_storage") is { Length: > 0 } storage ? storage : "local",
                MoviePilotTransfer.Text(item, "library_path"),
                MoviePilotTransfer.Text(item, "transfer_type"),
                MoviePilotTransfer.Text(item, "overwrite_mode"),
                MoviePilotTransfer.Bool(item, "scraping"),
                MoviePilotTransfer.Bool(item, "library_type_folder"),
                MoviePilotTransfer.Bool(item, "library_category_folder")))
            .ToList();

        var sources = ParseMediaSources(await CallAsync((apiBase, token, client) =>
            client.GetAsync(apiBase, token, "media/source", cancellationToken), cancellationToken, identity).ConfigureAwait(false))
            .Where(source => source.IsVideo).ToList();
        CheckIdentity(identity, cancellationToken);

        var defaultSource = "themoviedb";
        try
        {
            var global = await CallAsync((apiBase, token, client) => client.GetAsync(apiBase, token,
                "system/global/user", cancellationToken), cancellationToken, identity).ConfigureAwait(false);
            var configured = MoviePilotTransfer.Text(global, "RECOGNIZE_SOURCE");
            if (sources.Any(source => source.Id == configured)) defaultSource = configured;
        }
        catch (MoviePilotException error) when (error.Status == System.Net.HttpStatusCode.NotFound)
        {
            // Older servers without the user-global read retain the webpage's TMDB fallback.
        }
        CheckIdentity(identity, cancellationToken);
        if (!sources.Any(source => source.Id == defaultSource)) defaultSource = "";
        if (storages.Count == 0) storages.Add(new MoviePilotStorage("本地", "local"));

        Log.Info(Category, $"MoviePilot 整理选项：{storages.Count} 个存储、{directories.Count} 个媒体库目录、{sources.Count} 个数据源");
        return new MoviePilotTransferOptions(storages, directories, sources, defaultSource);
    }

    /// <summary>手动整理只读查询 TMDB 剧集组；表单负责检查类型及拒绝过期回话。</summary>
    public async Task<IReadOnlyList<MoviePilotEpisodeGroup>> TransferEpisodeGroupsAsync(
        string mediaId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(mediaId, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0)
            return [];
        var identity = CaptureIdentity();
        var groups = await CallAsync((address, token, client) => client.GetAsync(address, token,
            "media/groups/" + Uri.EscapeDataString(mediaId), cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        CheckIdentity(identity, cancellationToken);
        return MoviePilotEpisodeGroups.Build(groups, default);
    }

    /// <summary>
    /// 把 Emby 报来的源路径换成 MoviePilot 的 FileItem。列表里没有这一个文件（路径 MoviePilot 看不见、或
    /// 已被移走）就抛出人话 —— 调用方不该再往下走。
    /// </summary>
    public async Task<JsonElement> TransferFileAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var reply = await CallAsync((apiBase, token, client) => client.PostReplyAsync(apiBase, token,
            "storage/list?sort=name",
            new Dictionary<string, object?> { ["storage"] = "local", ["type"] = "file", ["path"] = sourcePath },
            cancellationToken), cancellationToken).ConfigureAwait(false);

        var files = MoviePilotTransfer.Array(reply.Data)
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .ToList();

        var file = files.FirstOrDefault(item =>
            MoviePilotTransfer.SamePath(MoviePilotTransfer.Text(item, "path"), sourcePath));
        if (file.ValueKind is not JsonValueKind.Object || !reply.Success)
            throw new MoviePilotException(
                $"MoviePilot 看不到这个文件：{sourcePath}。它是 MoviePilot 那台机器上的路径吗？（不会改为整理父目录）");

        MoviePilotTransfer.RequireFile(file, sourcePath);
        return file.Clone();
    }

    /// <summary>一批文件挨个换成 FileItem；哪一个换不出来整批就停，报的那句话里带上是哪一个。</summary>
    public async Task<IReadOnlyList<JsonElement>> TransferFilesAsync(
        IReadOnlyList<MoviePilotTransferFile> files, CancellationToken cancellationToken)
    {
        var resolved = new List<JsonElement>(files.Count);
        foreach (var file in files)
            resolved.Add(await TransferFileAsync(file.Path, cancellationToken).ConfigureAwait(false));
        return resolved;
    }

    /// <summary>
    /// 目的路径匹配： MoviePilot 按源文件算出该去哪个存储、哪个目录、用什么方式 —— 和官方前端的
    /// 「整理目的路径，选择自动将由后缀匹配路径匹配」同一趟调用。匹配不上时服务端回一个空对象，照实交出去。
    /// </summary>
    public async Task<MoviePilotTransferOptions> TransferTargetAsync(
        JsonElement file, CancellationToken cancellationToken)
    {
        var reply = await CallAsync((apiBase, token, client) => client.PostReplyAsync(apiBase, token,
            "transfer/manual/target-path",
            new Dictionary<string, object?> { ["fileitem"] = file }, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        if (!reply.Success) throw new MoviePilotException(reply.Message);
        return ParseTransferTarget(reply.Data);
    }

    /// <summary>
    /// 预览（<c>preview:true</c>）：只让 MoviePilot 算一遍会怎么整理，不动任何文件。
    /// </summary>
    public async Task<MoviePilotTransferPreview> TransferPreviewAsync(
        MoviePilotTransferRequest request, IReadOnlyList<JsonElement> files, CancellationToken cancellationToken)
    {
        var identity = CaptureIdentity();
        ValidateHistoryConnection(request.Histories, identity);
        var snapshot = request with
        {
            Files = Array.AsReadOnly(request.Files.ToArray()),
            Histories = Array.AsReadOnly(request.Histories.ToArray())
        };
        var resolved = Array.AsReadOnly(files.Select(file => file.Clone()).ToArray());
        await ValidateTransferCleanupAsync(snapshot, resolved, identity, cancellationToken).ConfigureAwait(false);
        var result = await TransferRunAsync(snapshot, resolved, background: false, preview: true, cancellationToken, identity)
            .ConfigureAwait(false);
        CheckIdentity(identity, cancellationToken);
        var preview = new MoviePilotTransferPreview(snapshot, resolved, result);
        preview.Seal(TransferConnectionStamp(identity));
        return preview;
    }

    /// <summary>
    /// 真正整理。<paramref name="background"/> 为真时加入 MoviePilot 的整理队列（不等结果就回「已接收」）；
    /// 为假时同步等它做完。
    /// <para>
    /// 有副作用（移动/复制文件、可能覆盖），调用方须先向用户确认 —— 话由
    /// <see cref="MoviePilotTransfer.Confirmation"/> 拼好。
    /// </para>
    /// </summary>
    public async Task<MoviePilotTransferResult> TransferSubmitAsync(
        MoviePilotTransferPreview preview, bool background, CancellationToken cancellationToken)
    {
        SessionIdentity identity;
        Dictionary<string, object?> body;
        try
        {
            identity = CaptureIdentity();
            CheckIdentity(identity, cancellationToken);
            preview.ClaimSubmission(TransferConnectionStamp(identity));
            ValidateHistoryConnection(preview.Request.Histories, identity);
            await ValidateTransferCleanupAsync(preview.Request, preview.Files, identity, cancellationToken).ConfigureAwait(false);
            foreach (var group in preview.Request.Histories.GroupBy(history => history.Title))
            {
                CheckIdentity(identity, cancellationToken);
                var current = await FindTransferHistoriesAsync(group.Key, cancellationToken).ConfigureAwait(false);
                CheckIdentity(identity, cancellationToken);
                foreach (var history in group)
                {
                    var fresh = current.FirstOrDefault(item => item.Id == history.Id);
                    if (fresh is null || history.Snapshot.ValueKind != JsonValueKind.Object ||
                        !JsonElement.DeepEquals(fresh.Snapshot, history.Snapshot))
                        throw new MoviePilotTransferBlockedException("原整理记录已经改变，请重新查找记录和预览");
                    MoviePilotTransfer.RequireFile(fresh.TransferFile, history.TransferPath);
                }
            }
            foreach (var path in preview.Request.Histories.Select(history => history.TargetPath)
                         .Where(path => path.Length > 0).Distinct(StringComparer.Ordinal))
            {
                CheckIdentity(identity, cancellationToken);
                var current = await FindTransferHistoriesAsync(path, cancellationToken).ConfigureAwait(false);
                CheckIdentity(identity, cancellationToken);
                var dependents = MoviePilotTransferHistoryMatch.Dependents(preview.Request.Histories, current);
                if (dependents.Count > 0)
                    throw new MoviePilotTransferBlockedException("旧目标还被软链接记录引用（" +
                        string.Join("、", dependents.Select(history => $"#{history.Id}")) +
                        "）。重新整理可能让它们失效，请先在 MoviePilot 处理引用关系；本次没有提交。");
            }
            CheckIdentity(identity, cancellationToken);
            body = preview.Request.Body(preview.Files, preview: false);
        }
        catch (MoviePilotTransferBlockedException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            // Everything above is preparation/read-only. Keep the consumed preview invalid, but do
            // not let a read failure masquerade as an actual request with an uncertain receipt.
            throw new MoviePilotTransferBlockedException("提交前核验失败：" + Failure.Describe(error), error);
        }
        // WriteAsync alone owns the sent/uncertain ledger; never wrap its failures as preflight.
        var reply = await WriteAsync(identity, TransferKeys(preview), (apiBase, token, client) => client.PostReplyAsync(
            apiBase, token, $"transfer/manual?background={(background ? "true" : "false")}", body, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var result = MoviePilotTransfer.ParseResult(reply.Data, reply.Success, reply.Message, preview: false);
        result = MoviePilotTransfer.CompleteReceipt(result, preview);
        Log.Info(Category, $"MoviePilot 手动整理（{(background ? "队列" : "同步")}）：{result.Summary}");
        return result;
    }

    private static string TransferConnectionStamp(SessionIdentity identity) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{identity.ApiBase}\n{identity.Username}\n{identity.ProtectedPassword}")));

    private async Task ValidateTransferCleanupAsync(MoviePilotTransferRequest request, IReadOnlyList<JsonElement> files,
        SessionIdentity identity, CancellationToken cancellationToken)
    {
        _ = request.Body(files, preview: true);
        for (var index = 0; index < files.Count; index++)
        {
            CheckIdentity(identity, cancellationToken);
            var path = MoviePilotTransfer.Text(files[index], "path");
            MoviePilotTransfer.RequireFile(files[index], path);
            var selected = request.Histories.Count > 0 ? request.Histories[index] : null;
            var all = await FindTransferHistoriesAsync(path, cancellationToken).ConfigureAwait(false);
            CheckIdentity(identity, cancellationToken);
            // Fixed upstream 48874bc selects history again by source storage/path at execution;
            // a logid is not a cleanup boundary. Without a server-side compare-and-set, reject
            // ambiguous or unconfirmed candidates instead of guessing which record is removed.
            var candidates = all.Where(history =>
                MoviePilotTransfer.SamePath(history.SourcePath, path) &&
                    (MoviePilotTransferHistoryMatch.Storage(history, source: true) is "local" or "") ||
                history.Success && history.Mode.Contains("move", StringComparison.Ordinal) &&
                    MoviePilotTransfer.SamePath(history.TargetPath, path) &&
                    (MoviePilotTransferHistoryMatch.Storage(history, source: false) is "local" or "")).ToArray();
            foreach (var history in all)
            {
                var rawSource = MoviePilotTransferHistory.Scalar(history.Snapshot, "src");
                var rawTarget = MoviePilotTransferHistory.Scalar(history.Snapshot, "dest");
                _ = MoviePilotTransferHistoryMatch.Storage(history, source: true);
                _ = MoviePilotTransferHistoryMatch.Storage(history, source: false);
                if (rawSource.Length > 0 && !MoviePilotTransfer.SamePath(rawSource, history.SourcePath) ||
                    rawTarget.Length > 0 && !MoviePilotTransfer.SamePath(rawTarget, history.TargetPath))
                    throw new MoviePilotTransferBlockedException("原记录的路径身份互相矛盾，请先在 MoviePilot 核对");
            }
            foreach (var history in candidates)
            {
                if (history.Success && history.Mode.Contains("move", StringComparison.Ordinal)) continue;
                if (history.TargetFile.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) continue;
                if (history.TargetFile.ValueKind != JsonValueKind.Object ||
                    MoviePilotTransfer.Text(history.TargetFile, "type") != "file" ||
                    history.TargetPath.Length == 0 || MoviePilotTransferHistoryMatch.Storage(history, source: false).Length == 0)
                    throw new MoviePilotTransferBlockedException("旧目标不是已核对的单个文件，不能授权目录或未知范围清理；请先在 MoviePilot 核对");
            }
            if (candidates.Length > 1 || candidates.Any(history => selected is null || history.Id != selected.Id) ||
                selected is not null && candidates.Length == 0)
                throw new MoviePilotTransferBlockedException("同源文件存在未确认或不明确的清理记录，请先在 MoviePilot 处理，不能扩大本次整理范围");
        }
    }

    private async Task<MoviePilotTransferResult> TransferRunAsync(
        MoviePilotTransferRequest request,
        IReadOnlyList<JsonElement> files,
        bool background,
        bool preview,
        CancellationToken cancellationToken,
        SessionIdentity identity)
    {
        CheckIdentity(identity, cancellationToken);
        ValidateHistoryConnection(request.Histories, identity);
        var body = request.Body(files, preview);
        var reply = await CallAsync((apiBase, token, client) => client.PostReplyAsync(
            apiBase, token, $"transfer/manual?background={(background ? "true" : "false")}", body, cancellationToken),
            cancellationToken, identity).ConfigureAwait(false);

        return MoviePilotTransfer.ParseResult(reply.Data, reply.Success, reply.Message, preview);
    }

    /// <summary>media/source 里没有 media_types 时的容错：缺这个键就当不限类型。</summary>
    private static JsonElement TryMember(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) ? value : default;

    /// <summary>
    /// 把 <c>media/source</c> 的回话拆成来源条目。名字没给的用标识兜底（屏上不留空名字）；
    /// media_types 缺失或空按「不限类型」处理，交给 <see cref="MoviePilotMediaSource.IsVideo"/> 判断。
    /// </summary>
    private static IReadOnlyList<MoviePilotMediaSource> ParseMediaSources(JsonElement data) =>
        MoviePilotTransfer.Array(data)
            .Where(item => MoviePilotTransfer.Text(item, "media_source").Length > 0)
            .Select(item => new MoviePilotMediaSource(
                MoviePilotTransfer.Text(item, "name") is { Length: > 0 } name
                    ? name : MoviePilotTransfer.Text(item, "media_source"),
                MoviePilotTransfer.Text(item, "media_source"),
                MoviePilotTransfer.Array(TryMember(item, "media_types"))
                    .Select(one => one.ValueKind == JsonValueKind.String ? one.GetString() ?? "" : "")
                    .Where(kind => kind.Length > 0)
                    .ToList()))
            .ToList();
}
