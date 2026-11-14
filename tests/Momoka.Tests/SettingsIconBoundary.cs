namespace Microsoft.UI.Xaml.Data;

// 仅替代框架接口；字形转换执行源链接的真实转换器，不构造 WinUI 对象。
public interface IValueConverter
{
    object Convert(object value, Type targetType, object parameter, string language);
    object ConvertBack(object value, Type targetType, object parameter, string language);
}
