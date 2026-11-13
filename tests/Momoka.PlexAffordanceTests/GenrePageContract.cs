using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Momoka.Emby;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>Transparent calls into the current Shell VM; only the unavailable theme brush is substituted.</summary>
internal sealed class ReflectedDetail(object value) : INotifyPropertyChanged
{
    public object Value { get; } = value;
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => ((INotifyPropertyChanged)Value).PropertyChanged += value;
        remove => ((INotifyPropertyChanged)Value).PropertyChanged -= value;
    }
    public IReadOnlyList<string> SublineGenres => ShellAssembly.Get<IReadOnlyList<string>>(Value, nameof(SublineGenres));
    public bool CanOpenGenre => ShellAssembly.Get<bool>(Value, nameof(CanOpenGenre));
    public bool CanOpenGenreValue(string genre) => (bool)ShellAssembly.Invoke(Value, nameof(CanOpenGenreValue), genre)!;
    public Action? CaptureGenreNavigation(string genre) => (Action?)ShellAssembly.Invoke(Value, nameof(CaptureGenreNavigation), genre);
    public string ItemType => ShellAssembly.Get<string>(Value, nameof(ItemType));
    public string Subline => ShellAssembly.Get<string>(Value, nameof(Subline));
    public bool BackdropShown => ShellAssembly.Get<bool>(Value, nameof(BackdropShown));
    public bool HeroCardShown => ShellAssembly.Get<bool>(Value, nameof(HeroCardShown));

    /// <summary>只给提取过来的通知处理认这个名字用；这一份夹具不读其他信息。</summary>
    public object OtherFacts { get; } = new();
    public object SublineBrush { get; } = new();
    public Task LastLoad { get; private set; } = Task.CompletedTask;
    public void UseImagePresentation(Task presentation) =>
        ShellAssembly.Invoke(Value, nameof(UseImagePresentation), presentation);
    public void Attach(ReflectedDetailRequest request, PlexActionsProxy actions, ISettingsService settings, EmbySession session, EmbyImageStore images) =>
        ShellAssembly.Invoke(Value, "Attach", request.Value, actions, settings, session, images);
    public Task ReloadAsync() => LastLoad = (Task)ShellAssembly.Invoke(Value, "ReloadAsync")!;
    public void OpenGenre(string genre) => ShellAssembly.Invoke(Value, nameof(OpenGenre), genre);
}

/// <summary>Paint, notification handler and readings are extracted unchanged from the actual page at build time.</summary>
internal sealed partial class GenrePageContract : IDisposable
{
    public ReflectedDetail ViewModel { get; }
    public InlineText GenreLine { get; } = new();
    private double _paperCover;
    private int _paintPhase;
    private bool _washedInk;
    public bool PaintStateReset => _paperCover == -1 && _paintPhase == -1 && !_washedInk;
    public int VisualTransitionAttaches { get; private set; }
    public EmbyItem? TransitionItem { get; private set; }
    public GenrePageContract(object vm)
    {
        ViewModel = new ReflectedDetail(vm);
        ((INotifyPropertyChanged)vm).PropertyChanged += OnViewModelChanged;
    }
    public void Paint()
    {
        GenreLine.Visibility = ShellAssembly.Get<object>(ViewModel.Value, "GenreVisibility").ToString() == "Visible"
            ? Visibility.Visible : Visibility.Collapsed;
        PaintGenres();
    }
    internal void Enter(object request, PlexActionsProxy actions, FixtureServices services, EmbySession session, EmbyImageStore images) =>
        EnterCore(new ReflectedDetailRequest(request), actions, services, session, images);
    internal void ObserveTransitionBoundary(EmbyItem item)
    {
        VisualTransitionAttaches++;
        TransitionItem = item;
    }
    public (bool Ok, string Detail) Read() => GenreRead();
    public IReadOnlyList<Hyperlink> Links => GenreLine.Inlines.OfType<Hyperlink>().ToArray();
    public string Text => string.Concat(GenreLine.Inlines.Select(inline => inline switch
    {
        Run run => run.Text,
        Hyperlink link => string.Concat(link.Inlines.OfType<Run>().Select(run => run.Text)),
        _ => throw new AssertionException("未知内联元素")
    }));
    // Only unrelated visual painting is a boundary. The actual genre handler still runs above.
    private void PaintScrim() { }
    private void PaintWash(bool force) { }
    private void PlaceEpisodes() { }
    private void PaintOtherInfo() { }
    public void Leave()
    {
        ((INotifyPropertyChanged)ViewModel.Value).PropertyChanged -= OnViewModelChanged;
        ShellAssembly.Invoke(ViewModel.Value, "Cancel");
    }
    public void Dispose() => ((INotifyPropertyChanged)ViewModel.Value).PropertyChanged -= OnViewModelChanged;
}

/// <summary>只转发当前Shell的DetailRequest值与Item，不复制任何权限或视图模型逻辑。</summary>
internal sealed class ReflectedDetailRequest(object value)
{
    internal object Value { get; } = value;
    internal EmbyItem Item => ShellAssembly.Get<EmbyItem>(Value, nameof(Item));
}

/// <summary>无Frame/XamlRoot的无关视觉端点：记录源接线，不访问账号、页面或native动画。</summary>
internal static class CardDetailTransition
{
    internal static void Attach(GenrePageContract page, EmbyItem item) => page.ObserveTransitionBoundary(item);
}

internal sealed class InlineText
{
    public List<Inline> Inlines { get; } = [];
    public Visibility Visibility { get; set; } = Visibility.Visible;
}

internal sealed class FixtureServices(ISettingsService settings)
{
    public T GetRequiredService<T>() where T : class => settings as T
        ?? throw new AssertionException("本项页面进入只取假设置服务");
}
