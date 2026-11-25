using System.Windows;
using System.Windows.Media;

namespace ResetExplode;

/// <summary>
/// Troca entre modo escuro e claro substituindo os pincéis da paleta (Theme.xaml). O XAML os referencia com DynamicResource,
/// então a troca vale na hora, sem recarregar janelas.
/// </summary>
public static class ThemeManager
{
    public static bool IsLight { get; private set; }

    private static readonly (object Key, string Dark, string Light)[] Palette =
    {
        ("Bg", "#101C28", "#F2F4F6"),
        ("Panel", "#192B3B", "#FFFFFF"),
        ("Card", "#192B3B", "#FFFFFF"),
        ("Hover", "#223F56", "#E8F1F7"),
        ("Line", "#30465A", "#D9E1E7"),
        ("LineStrong", "#49657D", "#B8C7D3"),
        ("Fg", "#FFFFFF", "#183247"),
        ("Muted", "#B8C7D3", "#486275"),
        ("Faint", "#91A7B9", "#5D7486"),
        ("Ink", "#101C28", "#FFFFFF"),
        ("Accent", "#145B8C", "#073B63"),
        ("OnAccent", "#FFFFFF", "#FFFFFF"),
        ("Selection", "#23445F", "#E8F1F7"),
        ("ActiveFg", "#FFFFFF", "#073B63"),
        ("GraphArea", "#33145B8C", "#22073B63"),
        ("ThumbBrush", "#30465A", "#D9E1E7"),
        ("ThumbHover", "#5D7D97", "#AFC0CC"),
        ("ThumbDrag", "#91AFC5", "#073B63"),
        (SystemColors.ControlBrushKey, "#192B3B", "#FFFFFF"),
        (SystemColors.ControlTextBrushKey, "#FFFFFF", "#183247"),
        (SystemColors.WindowBrushKey, "#101C28", "#F2F4F6"),
        (SystemColors.WindowTextBrushKey, "#FFFFFF", "#183247"),
        (SystemColors.HighlightBrushKey, "#23445F", "#E8F1F7"),
        (SystemColors.InactiveSelectionHighlightBrushKey, "#23445F", "#E8F1F7"),
        (SystemColors.HighlightTextBrushKey, "#FFFFFF", "#073B63"),
    };

    public static void Apply(bool light)
    {
        IsLight = light;
        var res = Application.Current.Resources;
        foreach (var (key, dark, lightHex) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(light ? lightHex : dark);
            if (res.Contains(key) && res[key] is SolidColorBrush current && !current.IsFrozen)
                current.Color = color;
            else
                res[key] = new SolidColorBrush(color);
        }
    }
}
