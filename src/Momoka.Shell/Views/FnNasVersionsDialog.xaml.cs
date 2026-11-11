using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.FnNas;

namespace Momoka.Shell.Views;

/// <summary>飞牛的版本表：只负责把文件列出来、把选中的那一版交回去；删除确认与请求在 ItemCommands。</summary>
public sealed partial class FnNasVersionsDialog : ContentDialog
{
    public FnNasVersionsDialog(string itemName, IReadOnlyList<FnNasVersion> versions, bool allowRemoval = false)
    {
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Title = $"管理版本 · {itemName}";
        Files.ItemsSource = versions;
        SecondaryButtonText = allowRemoval ? "移出所选版本" : "";
        IsSecondaryButtonEnabled = false;
        DefaultButton = ContentDialogButton.Close;
    }

    public FnNasVersion? Selected => Files.SelectedItem as FnNasVersion;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = Selected is { MediaGuid.Length: > 0 };
        IsSecondaryButtonEnabled = IsPrimaryButtonEnabled && SecondaryButtonText.Length > 0;
    }
}
