using System.Globalization;

namespace Portfolio.Web.Tests.Support;

/// <summary>WCAG contrast ratio between two #rrggbb colours.</summary>
public static class Contrast
{
    public static double OnWhite(string hex) => Ratio(hex, "#ffffff");

    public static double Ratio(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        static double Channel(string h, int at) { var s = int.Parse(h.AsSpan(at, 2), NumberStyles.HexNumber) / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(hex, 1) + 0.7152 * Channel(hex, 3) + 0.0722 * Channel(hex, 5);
    }
}
