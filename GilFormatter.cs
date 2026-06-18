using System.Globalization;

namespace MatheMann;

/// <summary>
/// How the Copy buttons format a gil amount onto the clipboard. The on-screen
/// table/total display always uses German-style grouping (see FormatGil in the
/// window classes) — this only controls what gets copied, so it can be pasted
/// straight into whatever spreadsheet locale the user is working with.
/// </summary>
public enum GilFormat
{
    /// <summary>1.234.567 — dot as the thousands separator.</summary>
    German,

    /// <summary>1,234,567 — comma as the thousands separator.</summary>
    International,

    /// <summary>1234567 — no separator at all.</summary>
    Raw,
}

/// <summary>Formats a gil amount for the clipboard according to a <see cref="GilFormat"/>.</summary>
public static class GilFormatter
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Human-readable labels for the settings dropdown, in enum declaration order.</summary>
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
