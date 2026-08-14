using System;
using System.Globalization;
using System.Windows.Data;
using CardioView.Models;

namespace CardioView.Converters;

public sealed class PatientStateNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is PatientState s
            ? s switch
            {
                PatientState.Exercise => "Exercício",
                PatientState.Tachycardia => "Taquicardia",
                PatientState.Bradycardia => "Bradicardia",
                PatientState.Hypoxia => "Hipóxia",
                PatientState.Fever => "Febre",
                _ => "Normal",
            }
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
