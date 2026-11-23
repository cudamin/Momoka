using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

/// <summary>
/// 综合筛选弹窗（网页端「全部筛选」的对应物）。与结果共用同一个
/// <see cref="MoviePilotResourceBrowserViewModel"/>，勾选即生效；关闭只收起弹窗，不回滚已选条件。
/// </summary>
public sealed partial class MoviePilotResourceFilterDialog : ContentDialog
{
    public MoviePilotResourceBrowserViewModel ViewModel { get; }

    public MoviePilotResourceFilterDialog(MoviePilotResourceBrowserViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // 关闭后 ItemsRepeater 不再订阅 DialogFacets：弹窗收起后若新结果改到这份集合，
        // 隐藏的 repeater 会收到集合变更并在原生层抛 COMException（探针里 0x80004005）。
        Closed += (_, _) => DialogFacetGroups.ItemsSource = null;
    }

    private static Visibility FilterCheckVisibility(bool selected) => selected ? Visibility.Visible : Visibility.Collapsed;
}
