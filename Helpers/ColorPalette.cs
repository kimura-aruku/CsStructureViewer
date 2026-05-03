using System.Windows.Media;

namespace CsStructureViewer.Helpers;

public static class ColorPalette
{
    public static (Color NamespaceColor, Color ClassColor) GetColors(int index, int total)
    {
        var hue = total > 1 ? (double)index / total * 360.0 : 0.0;
        var nsColor = HslToColor(hue, saturation: 0.55, lightness: 0.70, alpha: 0.30);
        var classColor = HslToColor(hue, saturation: 0.55, lightness: 0.55, alpha: 1.00);
        return (nsColor, classColor);
    }

    private static Color HslToColor(double h, double saturation, double lightness, double alpha)
    {
        h %= 360;
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = lightness - c / 2;

        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromArgb(
            (byte)(alpha * 255),
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }
}
