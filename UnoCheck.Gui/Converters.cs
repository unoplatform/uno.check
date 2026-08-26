using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace UnoCheck.Gui;

public sealed class NullToCollapsedConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, string language)
		=> value is null || (value is string s && string.IsNullOrEmpty(s))
			? Visibility.Collapsed
			: Visibility.Visible;

	public object ConvertBack(object? value, Type targetType, object? parameter, string language)
		=> throw new NotSupportedException();
}

public sealed class BoolNegationConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, string language)
		=> value is bool b && !b;

	public object ConvertBack(object? value, Type targetType, object? parameter, string language)
		=> value is bool b && !b;
}
