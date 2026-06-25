using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Game;

namespace MatheMann;

public readonly record struct SellInfo(uint Quantity, uint Price);

// Pulls qty + price out of the game's "you sold X for Y gil" chat line. The item
// itself comes from the ItemPayload (language-independent) so we only need the verb
// per language. One regex each, anchored on the sell verb so buyback lines don't match.
// Wordings + the separator/article gotchas are in DEVNOTES.
public static class SellMessageParser
{
    private static readonly RegexOptions Opts =
        RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex English =
        new(@"[Yy]ou sell (?<qty>\d+)?.*? for (?<price>[\d.,\s]+) gil", Opts);

    // verb at the end; "Gil" can be singular ("für 1 Gil verkauft"). zurückgekauft won't match.
    private static readonly Regex German =
        new(@"[Dd]u hast (?<qty>\d+)?.*? für (?<price>[\d.,\s]+) [Gg]il verkauft", Opts);

    // buyback uses "rachetez", won't match "vendez".
    private static readonly Regex French =
        new(@"[Vv]ous vendez (?<qty>\d+)?.*? pour (?<price>[\d.,\s]+) gils?", Opts);

    // qty always present (even ×1). 売却 = sell, distinct from 買い戻 (buy back).
    private static readonly Regex Japanese =
        new(@"(?<qty>\d+)を(?<price>[\d.,\s]+)ギルで売却", Opts);

    // Null if the line isn't a sell notice for this language. Unknown langs use English.
    public static SellInfo? TryParse(string text, ClientLanguage language)
    {
        var regex = language switch
        {
            ClientLanguage.German   => German,
            ClientLanguage.French   => French,
            ClientLanguage.Japanese => Japanese,
            _                        => English,
        };

        var match = regex.Match(text);
        if (!match.Success) return null;

        // Quantity: absent (article instead of a number) means a single item.
        var qty = ParseNumber(match.Groups["qty"].Value);
        if (qty == 0) qty = 1;

        var price = ParseNumber(match.Groups["price"].Value);
        if (price == 0) return null;   // a real sale is never 0 gil; treat as no-match

        return new SellInfo(qty, price);
    }

    // Digits only - ignores whatever thousands separator the language used, and
    // normalizes full-width JP digits. 0 if no digits.
    private static uint ParseNumber(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return 0;

        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c >= '0' && c <= '9')
                sb.Append(c);
            else if (c >= '\uFF10' && c <= '\uFF19')   // full-width ０-９
                sb.Append((char)('0' + (c - '\uFF10')));
        }

        return sb.Length > 0 && uint.TryParse(sb.ToString(), NumberStyles.None,
                   CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }
}
