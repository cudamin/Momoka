using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class HomeRowTemplates : DataTemplateSelector
{
    public DataTemplate? Library { get; set; }
    public DataTemplate? Subscription { get; set; }
    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        CardShelf => Library,
        MoviePilotHomeRow => Subscription,
        _ => null
    };
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
