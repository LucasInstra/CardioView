using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CardioView.Services;

namespace CardioView.Converters;

/// <summary>Converte FindingSeverity em um pincel de destaque (chip colorido).</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is FindingSeverity s
            ? s switch
            {
                FindingSeverity.Critical => new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55)),
                FindingSeverity.Attention => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
                _ => new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
            }
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
