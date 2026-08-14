using System.Windows.Media;
using CardioView.Services;

namespace CardioView.Controls;

public static class AnnotationPalette
{
    public static Color ForCode(int code)
    {
        switch (code)
        {
            case 1: return Color.FromRgb(0x2B, 0xFF, 0x5A);   // N — normal
            case 5: return Color.FromRgb(0xFF, 0x3B, 0x30);   // V — PVC
            case 13: return Color.FromRgb(0xFF, 0xB0, 0x20);  // Q — unclassifiable
            case 14: return Color.FromRgb(0x8A, 0x8A, 0x8A);  // n — noise/quality
            case 16: return Color.FromRgb(0x6E, 0x6E, 0x6E);  // ? — artifact
            case 28: return Color.FromRgb(0x4F, 0xC3, 0xF7);  // + — rhythm change
            default: return Color.FromRgb(0xFF, 0xC4, 0x00);
        }
    }

    public static Color For(MitBihAnnotation ann) => ForCode(ann.Code);
}