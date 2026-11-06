using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed partial class MoviePilotService
{
    private readonly SemaphoreSlim _historyCapabilityRead = new(1, 1);
    private MoviePilotHistoryCapabilities? _historyCapabilities;
    private readonly Dictionary<(object Owner, string Stamp, string ItemId), List<string>> _removedItemLibraryPaths = [];
    public event EventHandler? HistoryCapabilitiesChanged;

    private void SetHistoryCapabilities(MoviePilotHistoryCapabilities? value)
    {
        bool changed;
        lock (_sessionGate)
        {
            changed = _historyCapabilities != value;
            _historyCapabilities = value;
        }
        if (changed) HistoryCapabilitiesChanged?.Invoke(this, EventArgs.Empty);
    }
    public bool CanManageHistory => Available && _historyCapabilities is { CanManage: true } capabilities
        && IsCurrentConnection(capabilities.ConnectionStamp);
    public bool CanSmartReorganize => CanManageHistory && _historyCapabilities?.CanSmartReorganize == true;

    /// <summary>只读取当前用户权限和公开给登录用户的 AI 总开关，不读取整份设置或秘密值。</summary>
    public async Task<MoviePilotHistoryCapabilities> HistoryCapabilitiesAsync(CancellationToken cancellationToken)
    {
        await _historyCapabilityRead.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadHistoryCapabilitiesAsync(cancellationToken).ConfigureAwait(false); }
        finally { _historyCapabilityRead.Release(); }
    }

    private async Task<MoviePilotHistoryCapabilities> ReadHistoryCapabilitiesAsync(CancellationToken cancellationToken)
    {
        if (!Available) throw new MoviePilotOperationBlockedException("MoviePilot 连接不可用，请重新连接");
        var identity = CaptureIdentity();
        var stamp = TransferConnectionStamp(identity);
        try
        {
            var user = await CallAsync((address, token, client) => client.GetAsync(address, token, "user/current", cancellationToken),
                cancellationToken, identity).ConfigureAwait(false);
            CheckIdentity(identity, cancellationToken);
            var manage = MoviePilotTransfer.Bool(user, "is_active") && (MoviePilotTransfer.Bool(user, "is_superuser") ||
                MoviePilotTransfer.Bool(MoviePilotTransferHistory.Member(user, "permissions"), "manage"));
            var smart = false;
            if (manage)
            {
                var settings = await CallAsync((address, token, client) => client.GetAsync(address, token, "system/global/user", cancellationToken),
                    cancellationToken, identity).ConfigureAwait(false);
                smart = MoviePilotTransfer.Bool(settings, "AI_AGENT_ENABLE");
            }
            CheckIdentity(identity, cancellationToken);
            if (!Available) throw new MoviePilotOperationBlockedException("MoviePilot 连接不可用，请重新连接");
            var next = new MoviePilotHistoryCapabilities(stamp, manage, smart);
            SetHistoryCapabilities(next);
            return next;
        }
        catch
        {
            if (MatchesConfiguration(identity)) SetHistoryCapabilities(null);
            throw;
        }
    }

    public Task<MoviePilotHistoryPlan> PrepareItemHistoryActionAsync(Emby.EmbySessionScope scope, Emby.EmbyItem item,
        MoviePilotHistoryAction action, string connectionStamp, CancellationToken cancellationToken) =>
        scope.ExecuteAsync(async (client, token) =>
        {
            var context = await MoviePilotTransferCollect.CollectAsync(client, item, token, seriesFiles: true, maxFiles: MoviePilotHistoryPlan.MaxFiles).ConfigureAwait(false);
            // MP 删除历史可能先于媒体服务器刷新；本进程明确移除的同条目版本不再要求重复匹配、提交。
            if (action is MoviePilotHistoryAction.DeleteDestination or MoviePilotHistoryAction.DeleteAll)
            {
                lock (_operationGate)
                    if (_removedItemLibraryPaths.TryGetValue((scope.UserStateOwner, connectionStamp, item.Id), out var removed))
                        context = context with
                        {
                            Files = context.Files.Where(file => !removed.Any(path => MoviePilotTransfer.SamePath(path, file.Path))).ToArray()
                        };
            }
            var plan = await PrepareHistoryActionAsync(context, action, item.Type is Emby.EmbyItemType.Series or Emby.EmbyItemType.Season,
                connectionStamp, token).ConfigureAwait(false);
            return new MoviePilotHistoryPlan(plan.Title, plan.Action, plan.Batch, plan.ConnectionStamp, plan.Histories,
                plan.LibraryPaths, scope.UserStateOwner, item.Id);
        }, cancellationToken);

    public async Task<MoviePilotHistoryPlan> PrepareHistoryActionAsync(MoviePilotTransferContext context,
        MoviePilotHistoryAction action, bool batch, string connectionStamp, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        var identity = RequireResultConnection(connectionStamp);
        if (context.Files.Count is 0 or > MoviePilotHistoryPlan.MaxFiles || string.IsNullOrWhiteSpace(context.Title))
            throw new MoviePilotOperationBlockedException("没有完整、可核对的媒体文件范围，本次没有提交");
        var capabilities = await HistoryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        RequireHistoryCapability(capabilities, action);
        CheckIdentity(identity, cancellationToken);
        var found = (await FindTransferHistoriesAsync(context.Title, cancellationToken).ConfigureAwait(false)).ToList();
        foreach (var file in context.Files)
        {
            var matches = action == MoviePilotHistoryAction.SmartReorganize
                ? MoviePilotTransferHistoryMatch.Candidates(found, file.Path)
                : MoviePilotTransferHistoryMatch.DeletionCandidates(found, file.Path,
                    action is MoviePilotHistoryAction.DeleteDestination or MoviePilotHistoryAction.DeleteAll);
            if (matches.Count != 0) continue;
            // MP 和媒体服务器可能显示不同片名。补查具体文件路径，不退回父目录，也不采用同名候选。
            var byPath = await FindTransferHistoriesAsync(file.Path, cancellationToken).ConfigureAwait(false);
            CheckIdentity(identity, cancellationToken);
            foreach (var history in byPath)
            {
                var previous = found.FirstOrDefault(value => value.Id == history.Id);
                if (previous is null) found.Add(history);
                else if (!JsonElement.DeepEquals(previous.Snapshot, history.Snapshot))
                    throw new MoviePilotOperationBlockedException("整理记录在查找过程中发生变化，请重新查询");
            }
        }
        var histories = action == MoviePilotHistoryAction.SmartReorganize
            ? MoviePilotTransferHistoryMatch.RequireMatches(context, found)
            : MoviePilotTransferHistoryMatch.RequireDeletionMatches(context, found,
                requireDestination: action is MoviePilotHistoryAction.DeleteDestination or MoviePilotHistoryAction.DeleteAll);
        foreach (var history in histories)
        {
            if (history.Snapshot.ValueKind != JsonValueKind.Object || history.ConnectionStamp != connectionStamp)
                throw new MoviePilotOperationBlockedException("整理记录已失效，请重新查询");
            // 路径足以定位文件，但不能让同名、跨来源的媒体编号悄悄混入自动处理。
            if (history.Type != context.Type || context.MediaId.Length > 0 &&
                (!string.Equals(history.MediaSource, "themoviedb", StringComparison.OrdinalIgnoreCase) || history.MediaId != context.MediaId))
                throw new MoviePilotOperationBlockedException("原整理记录的媒体身份与当前作品不一致，请先在 MoviePilot 核对");
        }
        var plan = new MoviePilotHistoryPlan(context.Title, action, batch, connectionStamp, histories,
            context.Files.Select(file => file.Path).ToArray());
        // 删除候选尚未选择：先封存精确文件和快照，引用保护在最终选择提交前核对。
        await ValidateHistoryPlanAsync(plan, identity, cancellationToken, validateReferences: plan.Smart).ConfigureAwait(false);
        CheckIdentity(identity, cancellationToken);
        lock (_operationGate)
            if (HistoryOperationKeys(plan).Any(_operations.ContainsKey))
                throw new MoviePilotOperationBlockedException("这些记录正在处理、已提交或结果待核对，请先到 MoviePilot 核对；不会再次弹出可提交确认");
        return plan;
    }

    public Task<MoviePilotHistoryResult> SubmitItemHistoryActionAsync(Emby.EmbySessionScope scope,
        MoviePilotHistoryPlan plan, CancellationToken cancellationToken) =>
        scope.ExecuteAsync(async (_, token) =>
        {
            if (plan.ItemOwner is not null && !ReferenceEquals(plan.ItemOwner, scope.UserStateOwner))
                throw new MoviePilotOperationBlockedException("删除计划所属的媒体服务器账号已改变，请重新查询");
            var result = await SubmitHistoryActionAsync(plan, token).ConfigureAwait(false);
            if (plan.ItemOwner is not null && plan.ItemId.Length > 0 && IsCurrentConnection(plan.ConnectionStamp))
            {
                var paths = result.RemovedLibraryPaths(plan)
                    .Where(path => plan.LibraryPaths.Any(file => MoviePilotTransfer.SamePath(path, file))).ToArray();
                if (paths.Length > 0)
                {
                    lock (_operationGate)
                    {
                        var key = (plan.ItemOwner, plan.ConnectionStamp, plan.ItemId);
                        if (!_removedItemLibraryPaths.TryGetValue(key, out var removed))
                            _removedItemLibraryPaths.Add(key, removed = []);
                        foreach (var path in paths)
                            if (!removed.Any(existing => MoviePilotTransfer.SamePath(existing, path))) removed.Add(path);
                    }
                }
            }
            return result;
        }, cancellationToken);

    public async Task<MoviePilotHistoryResult> SubmitHistoryActionAsync(MoviePilotHistoryPlan plan, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(plan.ConnectionStamp);
        plan.Claim();
        RequireHistoryCapability(await HistoryCapabilitiesAsync(cancellationToken).ConfigureAwait(false), plan.Action);
        await ValidateHistoryPlanAsync(plan, identity, cancellationToken).ConfigureAwait(false);
        CheckIdentity(identity, cancellationToken);
        if (!Available) throw new MoviePilotOperationBlockedException("MoviePilot 连接不可用，本次没有提交");
        // 与手动整理共用历史与文件键；重开菜单或改选另一档删除不能绕过已发请求的账本。
        var keys = HistoryOperationKeys(plan);
        return await WriteAsync(identity, keys, async (address, token, client) =>
        {
            if (plan.Smart)
            {
                var path = plan.Batch || plan.Histories.Count > 1 ? "history/transfer/ai-redo"
                    : $"history/transfer/{plan.Histories[0].Id.ToString(CultureInfo.InvariantCulture)}/ai-redo";
                object? body = plan.Batch || plan.Histories.Count > 1
                    ? new { history_ids = plan.Histories.Select(history => history.Id).ToArray() } : null;
                var data = await client.MutateAsync(address, token, HttpMethod.Post, path, body, cancellationToken).ConfigureAwait(false);
                var progress = MoviePilotTransfer.Text(data, "progress_key");
                if (progress.Length == 0) throw new MoviePilotException("智能整理未返回进度键，请到 MoviePilot 核对，勿重复提交");
                var ids = MoviePilotTransferHistory.Member(data, "history_ids");
                var accepted = ids.ValueKind == JsonValueKind.Undefined
                    ? plan.Histories.Select(history => history.Id).ToArray()
                    : ParseAcceptedIds(ids, plan);
                return new MoviePilotHistoryResult(true, progress, accepted, [], plan.Histories.Count);
            }
            var receipts = new List<MoviePilotHistoryReceipt>();
            foreach (var history in plan.Histories)
            {
                try
                {
                    CheckIdentity(identity, cancellationToken);
                    var reply = await client.MutateReplyAsync(address, token, HttpMethod.Delete,
                        $"history/transfer?deletesrc={BoolQuery(plan.DeleteSource)}&deletedest={BoolQuery(plan.DeleteDestination)}",
                        new { id = history.Id }, cancellationToken).ConfigureAwait(false);
                    receipts.Add(MoviePilotHistoryReceipt.Parse(history.Id, reply, plan));
                }
                catch (MoviePilotTokenExpiredException) when (receipts.Count == 0) { throw; }
                catch (MoviePilotException error) when (receipts.Count == 0 && error.Status is
                    System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden or
                    System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.UnprocessableEntity)
                { throw; }
                catch (Exception error)
                {
                    // 不自动重发有已完成步骤的批次；401 也不能把前面的 DELETE 一起重放。
                    return new MoviePilotHistoryResult(false, "", [], receipts.AsReadOnly(), plan.Histories.Count,
                        $"记录 #{history.Id} 未取得完整结果：{Diagnostics.Failure.Describe(error)}。后续未发送，请到 MP 核对。");
                }
                if (!receipts[^1].Complete)
                    return new MoviePilotHistoryResult(false, "", [], receipts.AsReadOnly(), plan.Histories.Count,
                        "存在部分成功或缺少分项回执，后续记录未发送，请先到 MP 核对");
            }
            return new MoviePilotHistoryResult(false, "", [], receipts.AsReadOnly(), plan.Histories.Count);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string[] HistoryOperationKeys(MoviePilotHistoryPlan plan) =>
        plan.Histories.Select(history => OperationKey(plan.ConnectionStamp, "transfer-history",
                history.Id.ToString(CultureInfo.InvariantCulture)))
            .Concat(plan.Histories.SelectMany(history => new[] { history.TransferPath, history.SourcePath, history.TargetPath })
                .Where(path => path.Length > 0).Select(path => OperationKey(plan.ConnectionStamp, "transfer-file", path)))
            .Distinct(StringComparer.Ordinal).ToArray();

    private static string BoolQuery(bool value) => value ? "true" : "false";

    private static void RequireHistoryCapability(MoviePilotHistoryCapabilities capabilities, MoviePilotHistoryAction action)
    {
        if (!capabilities.CanManage) throw new MoviePilotOperationBlockedException("当前 MoviePilot 账号没有媒体管理权限");
        if (action == MoviePilotHistoryAction.SmartReorganize && !capabilities.CanSmartReorganize)
            throw new MoviePilotOperationBlockedException("MoviePilot 智能助手未启用，本次没有提交");
    }

    private async Task ValidateHistoryPlanAsync(MoviePilotHistoryPlan plan, SessionIdentity identity,
        CancellationToken cancellationToken, bool validateReferences = true)
    {
        foreach (var history in plan.Histories)
        {
            if (!plan.Smart && !plan.LibraryPaths.Any(path =>
                MoviePilotTransferHistoryMatch.DeletionCandidates([history], path, plan.DeleteDestination).Count == 1))
                throw new MoviePilotOperationBlockedException("原记录不能证明属于当前本地库文件，请重新核对；本次没有提交");
            if (plan.DeleteSource || plan.Smart) MoviePilotTransfer.RequireFile(history.SourceFile, history.SourcePath);
            if (plan.DeleteDestination || plan.Smart && history.TargetPath.Length > 0)
                MoviePilotTransfer.RequireFile(history.TargetFile, history.TargetPath);
            if (plan.Smart) MoviePilotTransfer.RequireFile(history.TransferFile, history.TransferPath);
        }
        foreach (var group in plan.Histories.GroupBy(history => history.Title, StringComparer.Ordinal))
        {
            var current = await FindTransferHistoriesAsync(group.Key, cancellationToken).ConfigureAwait(false);
            CheckIdentity(identity, cancellationToken);
            foreach (var history in group)
            {
                var fresh = current.SingleOrDefault(item => item.Id == history.Id);
                if (fresh is null || !JsonElement.DeepEquals(fresh.Snapshot, history.Snapshot))
                    throw new MoviePilotOperationBlockedException("原整理记录已经改变，请重新查询并确认；本次没有提交");
            }
        }
        if (plan.Action == MoviePilotHistoryAction.DeleteRecord || !validateReferences) return;
        // 源和目标都可能被其它软链接复用；不自动级联删除或让助手接管未确认的作品。
        var paths = plan.Histories.SelectMany(history => new[]
            {
                (Path: plan.DeleteSource || plan.Smart ? history.SourcePath : "", History: history, IsSource: true),
                (Path: plan.DeleteDestination || plan.Smart ? history.TargetPath : "", History: history, IsSource: false)
            }.Where(value => value.Path.Length > 0)).ToArray();
        foreach (var group in paths.GroupBy(value => value.Path, StringComparer.Ordinal))
        {
            var related = await FindTransferHistoriesAsync(group.Key, cancellationToken).ConfigureAwait(false);
            CheckIdentity(identity, cancellationToken);
            if (related.Any(history => history.Success && history.Mode == "softlink" &&
                MoviePilotTransfer.SamePath(history.SourcePath, group.Key) && group.Any(value => value.History.Id != history.Id &&
                    MoviePilotTransferHistoryMatch.SameStorage(MoviePilotTransferHistoryMatch.Storage(history, source: true),
                        MoviePilotTransferHistoryMatch.Storage(value.History, value.IsSource)) &&
                    // 同一目标重复记录只描述同一份链接；不同目标共用源与库文件作为下游源仍须保护。
                    (plan.Smart || !value.IsSource || history.TargetStorage is not ("" or "local") ||
                        value.History.TargetStorage is not ("" or "local") ||
                        !MoviePilotTransfer.SamePath(history.TargetPath, value.History.TargetPath)))))
                throw new MoviePilotOperationBlockedException("要处理的文件仍被其他软链接整理记录引用（包括本批次内引用），请先在 MoviePilot 处理引用关系；本次没有提交");
        }
    }

    private static long[] ParseAcceptedIds(JsonElement ids, MoviePilotHistoryPlan plan)
    {
        if (ids.ValueKind != JsonValueKind.Array) throw new MoviePilotException("智能整理回执缺少记录范围，请到 MP 核对");
        var accepted = ids.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var id) ? id : 0).ToArray();
        if (accepted.Length != plan.Histories.Count || accepted.Distinct().Count() != accepted.Length ||
            accepted.Any(id => !plan.Histories.Any(history => history.Id == id)))
            throw new MoviePilotException("智能整理回执的记录范围不一致，请到 MP 核对");
        return accepted;
    }
}
