using Momoka.Tests;
#if R010_COMPATIBILITY
using Momoka.Services;
#endif

namespace Momoka.Tests;

/// <summary>Only the local-save dependency contract is substituted, never Store or protected IO.</summary>
internal sealed class AnonymousLocalSave
#if R010_COMPATIBILITY
    : ISettingsPersistence
#endif
{
    private EventHandler? _changed;
    public bool HasUnsavedChanges { get; private set; }
    internal int Attempts { get; private set; }
    internal bool NextSuccess { get; set; } = true;
    internal int Subscribers => _changed?.GetInvocationList().Length ?? 0;
    public event EventHandler? SaveStateChanged
    {
        add => _changed += value;
        remove => _changed -= value;
    }
    public bool TrySave()
    {
        Attempts++;
        HasUnsavedChanges = !NextSuccess;
        _changed?.Invoke(this, EventArgs.Empty);
        return NextSuccess;
    }
    internal void ObserveResult(bool saved)
    {
        HasUnsavedChanges = !saved;
        _changed?.Invoke(this, EventArgs.Empty);
    }
}
