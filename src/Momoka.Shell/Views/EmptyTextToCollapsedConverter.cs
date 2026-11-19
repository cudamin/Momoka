using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Momoka.Shell.Views;

/// <summary>
/// 空串折成 <see cref="Visibility.Collapsed"/>，有字才露面。
/// <para>
/// 详情页音频、字幕下拉的第二行用它：没有标题的轨道（自动、不使用字幕）不该在展开列表里留一条空行，
/// 而第二行的字来自 <c>TrackRow.Title</c>（<see cref="Momoka.Emby.TrackRow"/>），Core 不引用 WinUI，
/// 所以在视图这一侧折。
/// </para>
/// </summary>
public sealed class EmptyTextToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
