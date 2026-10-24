using Momoka.Infrastructure;

namespace Momoka.Configuration;

// IO endpoint only. The real EmbySession.SelectSource still performs SetCurrent, Persist,
// PruneOthers and SourcesChanged. No settings document, credentials or DPAPI are read/written.
public sealed class SettingsStore(AppPaths paths, ISecretProtector protector)
{
    public AppPaths Paths { get; } = paths;
    public bool HasUnsavedChanges => false;
    internal int SaveCalls { get; private set; }
    internal int ForbiddenAccesses { get; private set; }
    public event EventHandler? SaveStateChanged { add { } remove { } }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(protector);
        SaveCalls++;
    }

    public AppSettings Load() => throw RejectIo();
    public AppSettings LoadStrict() => throw RejectIo();
    public string? PrepareStartupData(IEnumerable<string> priorRoots) => throw RejectIo();
    public void SaveBackup(string path, AppSettings settings) => throw RejectIo();
    public AppSettings ReadBackup(string path) => throw RejectIo();

    private InvalidOperationException RejectIo()
    {
        ForbiddenAccesses++;
        return new InvalidOperationException("R042 禁止设置文件、凭据与生产用户数据 IO");
    }
}
