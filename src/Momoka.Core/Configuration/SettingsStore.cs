using System.Text;
using System.Text.Json;
using Momoka.Diagnostics;
using Momoka.Infrastructure;

namespace Momoka.Configuration;

/// <summary>
/// Loads and saves settings.json. Load never throws: a corrupt file is preserved
/// under a new name and the app starts on defaults rather than refusing to launch.
/// </summary>
public sealed class SettingsStore(AppPaths paths, ISecretProtector protector)
{
    private const string Category = "settings";
    private readonly object _gate = new();
    // 编辑登记不能等待磁盘锁；滑块的热路径只递增代次，不快照、不做 IO。
    private readonly object _stateGate = new();
    private long _editVersion;
    private long _savedVersion;
    private bool _hasUnsavedChanges;

    /// <summary>包括会话直接保存的失败；所有页面读取同一份状态，而非各自记最后一次保存。</summary>
    public bool HasUnsavedChanges
    {
        get { lock (_stateGate) return _hasUnsavedChanges; }
    }

    public event EventHandler? SaveStateChanged;

    public AppPaths Paths { get; } = paths;

    /// <summary>普通启动先确定当前可恢复配置，再考虑旧产品数据目录；不用于只读自检。</summary>
    public string? PrepareStartupData(IEnumerable<string> priorRoots)
    {
        if (File.Exists(Paths.SettingsFile)) return null;
        // 复用正常 Load 的实际读取规则，而不是仅凭备份存在就封死旧目录恢复。
        // 只读验证不消耗/改写备份，也不隔离；稍后的 Load 从同一当前目录正式恢复。
        if (File.Exists(Paths.SettingsBackupFile) && TryRead(Paths.SettingsBackupFile, out _)) return null;
        return Paths.MigrateFromAny(priorRoots);
    }

    public AppSettings Load()
    {
        if (File.Exists(Paths.SettingsFile))
        {
            if (TryRead(Paths.SettingsFile, out var settings)) return settings;

            Log.Warn(Category, "主设置文件无法解析，尝试读取备份");
            Quarantine(Paths.SettingsFile);
        }
        else
        {
            Log.Info(Category, $"未找到 {Paths.SettingsFile}，尝试读取备份");
        }

        // 主文件缺失也走恢复路径：上次启动可能刚隔离坏文件，还没来得及重新保存就退出了。
        if (File.Exists(Paths.SettingsBackupFile) && TryRead(Paths.SettingsBackupFile, out var fromBackup))
            return fromBackup;

        Log.Info(Category, "没有可用的设置文件，使用默认设置");
        return SettingsMigration.NewDefaults();
    }

    /// <summary>
    /// Loads only the copied, saved DPAPI-format document. Unlike ordinary startup, a diagnostic must
    /// fail rather than silently switch to backup/defaults or quarantine its input. No file writes.
    /// Legacy plaintext documents are deliberately not imported by self-check.
    /// </summary>
    public AppSettings LoadStrict()
    {
        var json = File.ReadAllText(Paths.SettingsFile);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("SchemaVersion", out var version)
            || !version.TryGetInt32(out var number) || number < 2 || number > AppSettings.CurrentSchemaVersion)
            throw new InvalidDataException("自检需要已保存的 DPAPI 格式设置，不导入旧明文设置或默认值");
        return SettingsMigration.FromJson(json, protector);
    }

    /// <summary>
    /// 把偏好设置导出成一个备份文件：只含偏好，不含身份和凭据（服务器、账号、令牌、设备 id 都不写；见
    /// <see cref="SettingsPreferences"/>），所以这个文件可以安全带到别的机器。写的是普通的 settings.json 形状，
    /// 自己也读得回来、也能手看。落点是用户选的，不走 <see cref="Save"/> 那条原子写 ＋ 备份主文件的路。
    /// </summary>
    public void SaveBackup(string path, AppSettings settings)
    {
        var document = SettingsPreferences.ToBackupDocument(settings);
        document.SchemaVersion = AppSettings.CurrentSchemaVersion;

        var json = JsonSerializer.Serialize(document, SettingsSerializer.WriteOptions);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    /// <summary>
    /// 读一个偏好备份文件，交回解析好的设置。和 <see cref="LoadStrict"/> 一样：认不出来的文件<b>抛异常</b>，不悄悄
    /// 回退默认、不隔离、不写盘 —— 恢复配置要么真拿到一份能用的偏好，要么明确告诉用户这不是个有效备份。备份里没有
    /// 凭据（身份是空的），所以 <paramref name="path"/> 里即便有点什么，protector 也没有令牌要去解；签名要它只是因为
    /// 迁移管线统一收它。
    /// </summary>
    public AppSettings ReadBackup(string path)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("SchemaVersion", out var version)
            || !version.TryGetInt32(out var number) || number < 2 || number > AppSettings.CurrentSchemaVersion)
            throw new InvalidDataException("这个文件不是有效的 Momoka 配置备份");

        return SettingsMigration.FromJson(json, protector);
    }

    internal long PendingVersion
    {
        get { lock (_stateGate) return _savedVersion < _editVersion ? _editVersion : 0; }
    }

    internal void RegisterDeferredChange()
    {
        lock (_stateGate) _editVersion++;
    }

    /// <summary>
    /// 必须由活设置的拥有线程调用。JsonElement 独立持有序列化结果；后台只读它，
    /// 不枚举 WinUI 正在绑定/编辑的对象或列表。捕获期间若有另一同步保存，旧代次作废。
    /// </summary>
    internal SettingsSaveSnapshot? CapturePendingSave(AppSettings settings)
    {
        var version = PendingVersion;
        if (version == 0) return null;
        try
        {
            var document = JsonSerializer.SerializeToElement(settings, SettingsSerializer.WriteOptions);
            return PendingVersion == version ? new(version, document) : null;
        }
        catch (Exception error)
        {
            RecordSaveFailure(version, error);
            throw;
        }
    }

    public void Save(AppSettings settings)
    {
        long version = 0;
        try
        {
            lock (_gate)
            {
                lock (_stateGate) version = ++_editVersion;
                // 原有同步调用者仍在调用线程捕获。只 Normalize 独立文档，不写回活设置。
                var document = JsonSerializer.SerializeToElement(settings, SettingsSerializer.WriteOptions);
                WriteSnapshot(new(version, document));
            }
            Log.Debug(Category, "设置已保存");
        }
        catch (Exception error)
        {
            RecordSaveFailure(version, error, notify: false);
            throw;
        }
        finally { NotifySaveStateChanged(); }
    }

    /// <summary>只提交稳定快照。与同步 Save 共用磁盘锁；晚来的旧快照不能覆盖新保存。</summary>
    internal bool TrySaveSnapshot(SettingsSaveSnapshot snapshot)
    {
        try
        {
            lock (_gate)
            {
                if (PendingVersion != snapshot.Version) return true;
                WriteSnapshot(snapshot);
            }
            Log.Debug(Category, "设置已保存");
            NotifySaveStateChanged();
            return true;
        }
        catch (Exception error)
        {
            RecordSaveFailure(snapshot.Version, error);
            return false;
        }
    }

    private void WriteSnapshot(SettingsSaveSnapshot snapshot)
    {
        var document = snapshot.Document.Deserialize<AppSettings>(SettingsSerializer.ReadOptions)
            ?? throw new InvalidDataException("无法创建设置保存快照");
        SettingsMigration.Normalize(document);
        document.SchemaVersion = AppSettings.CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(document, SettingsSerializer.WriteOptions);
        AtomicFile.WriteAllText(Paths.SettingsFile, json, new UTF8Encoding(false), Paths.SettingsBackupFile);
        lock (_stateGate)
        {
            _savedVersion = Math.Max(_savedVersion, snapshot.Version);
            // 写盘期间的新编辑仍欠一次保存；旧成功不能清除较新的失败/欠项。
            if (_editVersion == snapshot.Version) _hasUnsavedChanges = false;
        }
    }

    internal void ReportPendingSaveFailure(Exception error) => RecordSaveFailure(PendingVersion, error);

    private void RecordSaveFailure(long version, Exception error, bool notify = true)
    {
        lock (_stateGate)
        {
            if (version > _savedVersion) _hasUnsavedChanges = true;
        }
        Log.Error(Category, "保存设置失败", error);
        if (notify) NotifySaveStateChanged();
    }

    private void NotifySaveStateChanged()
    {
        // 不在文件锁内调用任意 UI 订阅者；一个坏订阅者也不能把已成功落盘改判为失败。
        foreach (var handler in SaveStateChanged?.GetInvocationList() ?? [])
        {
            try { ((EventHandler)handler)(this, EventArgs.Empty); }
            catch (Exception error) { Log.Warn(Category, "保存状态通知失败", error); }
        }
    }

    private bool TryRead(string path, out AppSettings settings)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("设置文件必须是 JSON 对象");
            settings = SettingsMigration.FromJson(json, protector);
            return true;
        }
        // Deliberately every exception, not the three that used to be listed here (JSON, IO,
        // permissions). This class promises Load never throws, and the promise is what keeps the app
        // launchable: a file the migration cannot make sense of has to end up in the backup /
        // quarantine / defaults path, not on the way out of Main. The three-exception version let a
        // structurally odd document through — `{"Playback": null}` threw a NullReferenceException from
        // inside Normalize — and the window then never appeared at all.
        //
        // 代价说清楚：这一手也会把迁移代码自己的 bug 当成「文件坏了」，于是用户的设置被改名收起来、程序回到
        // 默认值。所以那一份**始终留在磁盘上**（Quarantine 只改名），异常整条写进日志。拿不准的时候，
        // 「打得开但设置回到默认」比「打不开」值钱。
        catch (Exception error)
        {
            Log.Warn(Category, $"读取 {path} 失败", error);
            settings = SettingsMigration.NewDefaults();
            return false;
        }
    }

    private static void Quarantine(string path)
    {
        try
        {
            var target = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, target, overwrite: true);
            Log.Warn(Category, $"已将损坏的设置文件保留为 {target}");
        }
        catch (Exception error)
        {
            Log.Warn(Category, "无法保留损坏的设置文件", error);
        }
    }
}
