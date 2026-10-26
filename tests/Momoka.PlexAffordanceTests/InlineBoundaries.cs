// Inline value/event substitutes only; no native/WinRT construction or layout takes place.
namespace Microsoft.UI.Xaml.Documents;

public enum UnderlineStyle { None, Single }
public abstract class Inline;
public sealed class Run : Inline
{
    public string Text { get; set; } = "";
}
public sealed class Hyperlink : Inline
{
    public UnderlineStyle UnderlineStyle { get; set; }
    public object? Foreground { get; set; }
    public List<Inline> Inlines { get; } = [];
    public event EventHandler? Click;
    public void ClickForFixture() => Click?.Invoke(this, EventArgs.Empty);
}
