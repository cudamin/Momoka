using System.Reflection;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Tests;

namespace Momoka.Shell.Views;

public sealed partial class AniBtScrapeDialog
{
    // This is the missing InitializeComponent endpoint, driven by the ACTUAL embedded XAML.
    // It evaluates member bindings generically; it contains no preview/submit state transitions.
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private readonly Dictionary<string, XElement> _controls = new(StringComparer.Ordinal);
    internal XDocument Markup { get; private set; } = null!;

    private void InitializeComponent()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Production.AniBtScrapeDialog.xaml")!;
        Markup = XDocument.Load(stream);
        foreach (var element in Markup.Descendants().Where(element => element.Attribute(Xaml + "Name") is not null))
            _controls.Add(element.Attribute(Xaml + "Name")!.Value, element);
        ViewModel.PropertyChanged += (_, _) => UpdateRootBindings();
        UpdateRootBindings();
    }

    private object? Evaluate(string value, object source)
    {
        if (!value.StartsWith("{x:Bind ", StringComparison.Ordinal)) return value;
        var expression = value[8..].TrimEnd('}');
        var member = expression.Split(',')[0].Trim();
        object? result = source;
        foreach (var part in member.Split('.'))
        {
            var property = result?.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(property is not null, "实际 XAML 绑定没有生成/声明成员：" + member);
            result = property!.GetValue(result);
        }
        return result;
    }

    private void UpdateRootBindings()
    {
        var element = Markup.Root!;
        PrimaryButtonText = (string?)Evaluate(element.Attribute("PrimaryButtonText")!.Value, this) ?? "";
        var enabled = element.Attribute("IsPrimaryButtonEnabled");
        IsPrimaryButtonEnabled = enabled is null || (bool)Evaluate(enabled.Value, this)!;
    }

    internal async Task PrimaryForFixtureAsync(UiQueue queue)
    {
        var handler = Markup.Root!.Attribute("PrimaryButtonClick")!.Value;
        var method = GetType().GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(method is not null, "实际 XAML 的 primary 事件处理器不存在");
        var args = new ContentDialogButtonClickEventArgs();
        method!.Invoke(this, [this, args]);
        await queue.IdleAsync();
        Assert.True(args.Cancel, "异步流程必须留在同一 ContentDialog，不能自动关闭或叠对话框");
    }

    internal bool HasControl(string name) => _controls.ContainsKey(name);
    internal object? BoundValue(string name, string attribute, object? source = null)
    {
        Assert.True(_controls.TryGetValue(name, out var control), "实际 XAML 缺控件：" + name);
        var value = control!.Attribute(attribute);
        Assert.True(value is not null, "实际 XAML 缺属性：" + name + "." + attribute);
        return Evaluate(value!.Value, source ?? this);
    }

    internal void ClickForFixture(string name)
    {
        Assert.True(_controls.TryGetValue(name, out var control), "实际 XAML 缺控件：" + name);
        var handler = control!.Attribute("Click")?.Value;
        Assert.True(handler is not null, "实际 XAML 缺 Click 绑定：" + name);
        GetType().GetMethod(handler!, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, [this, new RoutedEventArgs()]);
    }
}
