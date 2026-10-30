using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSitePicker : UserControl
{
    private MoviePilotSitePickerViewModel _viewModel = new();
    private string _label = "搜索站点";

    public MoviePilotSitePicker() => InitializeComponent();

    /// <summary>
    /// 这一颗选择器用的视图模型。搜索框里那几处由页面/窗口在 <c>Attach</c> 里交进来；设置页那张卡是
    /// 模板里的控件，只能靠 <c>x:Bind</c> 把行的这一份写进来，所以它得是可设的。
    /// </summary>
    public MoviePilotSitePickerViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;
            _viewModel = value;
            Bindings.Update();
        }
    }

    /// <summary>
    /// 左边那行字。默认是搜索框里的「搜索站点」；设置页那一条要说清它管的是默认值，所以写「默认搜索站点」。
    /// 属性直接落到控件上而不是走绑定：它只在建模板时写一次，包一层依赖属性换不来什么。
    /// </summary>
    public string Label
    {
        get => _label;
        set
        {
            _label = value;
            if (SiteLabel is not null) SiteLabel.Text = value;
        }
    }

    internal void Attach(MoviePilotSitePickerViewModel viewModel) => ViewModel = viewModel;

    private void OnOpening(object? sender, object args)
    {
        // 面板一开就照存下来的那一份重读一次：另一个窗口可能刚改过，而这一颗的标题不能停在旧选择上。
        ViewModel.RefreshSelection();
        _ = ViewModel.EnsureLoadedAsync();
    }

    internal void SetCompact(double maxWidth = 180)
    {
        SiteLabel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        ChooseSites.MaxWidth = maxWidth;
        // 搜索框里不给这颗下拉留外圈：只让文字和箭头占位，右边仍留 2 给清除键。
        ChooseSites.Padding = new Microsoft.UI.Xaml.Thickness(2, 0, 2, 0);
    }

    internal void ClosePopup() => SitesFlyout.Hide();
}
