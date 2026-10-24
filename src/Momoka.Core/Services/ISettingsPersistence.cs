namespace Momoka.Services;

/// <summary>当前内存设置的本地保存状态与重试能力，不包含服务器请求或账号编辑。</summary>
public interface ISettingsPersistence
{
    /// <summary>任一调用者的最近保存失败；后续成功保存同一文档才清除。</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>只把当前内存文档保存到本地；失败返回 false，不回滚或重放服务器写入。</summary>
    bool TrySave();

    /// <summary>
    /// 共享 Store 的保存尝试结束后发出。可能来自后台线程；订阅者须切回自己的调度器，
    /// 并重新读取当前状态，不把迟到通知当作某一旧次保存的结果。
    /// </summary>
    event EventHandler? SaveStateChanged;
}
