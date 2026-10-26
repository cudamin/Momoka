using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

// Field/value endpoints for the verbatim PlayerViewModel queue + shader-choice transaction.
// No UI framework, real window, configuration, dispatcher, or copy of commit control flow.
internal sealed partial class ShaderChoiceBoundary
{
    private readonly PlaybackService _playback;
    private int _generation = 51;
    private int _shaderPending;
    private bool _shaderPinned;
    private ShaderDecision _shaderPlan = ShaderDecision.None;
    private Task _pictureCommandTask = Task.CompletedTask;
    private Task _shaderSwitchTask = Task.CompletedTask;
    internal ShaderGroup? ActiveShader { get; private set; }
    private bool ShaderStateKnown => _playback.ShaderStateKnown;
    private event Action<string, InfoBarSeverity>? Noticed;
    internal List<(string Message, InfoBarSeverity Severity)> Notices { get; } = [];
    internal int Pending => _shaderPending;
    internal bool Pinned => _shaderPinned;
    internal ShaderChoiceBoundary(PlaybackService playback)
    {
        _playback = playback;
        Noticed += (message, severity) => Notices.Add((message, severity));
    }
    internal Task ChooseAsync(ShaderGroup? group) => ChangeShaderGroupAsync(group, pinned: true);
    private void Switch(ShaderDecision plan, string because) =>
        throw new AssertionException("R051 不执行自动换屏分支");
}

internal enum InfoBarSeverity { Informational, Warning }
