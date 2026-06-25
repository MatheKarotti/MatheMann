using System.Globalization;

namespace MatheMann;

// Number format for the Copy buttons (separate from the on-screen display format).
public enum GilFormat
{
    German,         // 1.234.567
    International,   // 1,234,567
    Raw,            // 1234567
}

public static class GilFormatter
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    // Labels for the settings dropdown, in enum order.
    public static readonly string[] Labels =
    {
        "German (1.234.567)",
        "International (1,234,567)",
        "Raw (1234567)",
    };

    public static string Format(ulong value, GilFormat format) => format switch
    {
        GilFormat.German        => value.ToString("N0", De),
        GilFormat.International => value.ToString("N0", En),
        GilFormat.Raw            => value.ToString(CultureInfo.InvariantCulture),
        _                        => value.ToString("N0", De),
    };
}
