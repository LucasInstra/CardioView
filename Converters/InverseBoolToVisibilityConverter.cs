using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CardioView.Converters;

/// <summary>Inverte um bool para Visibility (true → Collapsed).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Collapsed;
}
