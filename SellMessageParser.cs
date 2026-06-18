using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Game;

namespace MatheMann;

/// <summary>The quantity and price pulled from a retainer "you sold X for Y gil" line.</summary>
public readonly record struct SellInfo(uint Quantity, uint Price);

/// <summary>
/// Parses the retainer/vendor sell notice that the game prints to chat when an item
/// sells ("You sell 690 dark matter clusters for 6,900 gil." and its translations).
///
/// The item itself is NOT parsed from this text — it comes from the language-
/// independent <c>ItemPayload</c> in the message — so this parser only has to pull
/// the QUANTITY and the PRICE out of each language's wording. That keeps the job
/// small: one regex per client language, anchored on that language's "sell" phrasing
/// so buyback lines (which use a different verb) never match.
///
/// Confirmed wording per language (from in-game screenshots):
///   EN: "You sell 690 dark matter clusters for 6,900 gil."   /  "You sell a cracked materia for 5 gil."
///   DE: "Du hast 689 ... für 6.890 Gil verkauft."            /  "Du hast ein/eine ... für 1.000 Gil verkauft."
///   FR: "Vous vendez 689 ... pour 6 890 gils."               /  "Vous vendez un/une ... pour 1 000 gils."
///   JA: "...×689を6,890ギルで売却しました。"                  /  "...×1を1,000ギルで売却しました。"
///
/// Two things vary across languages and are handled deliberately:
///   1. The thousands separator differs — comma (EN/JA), dot (DE), space (FR, incl.
///      non-breaking/narrow spaces). Rather than special-case each, the price is
///      captured loosely and then reduced to digits only (see <see cref="ParseNumber"/>),
///      which normalizes every separator uniformly.
///   2. A single item drops the number entirely and uses an article instead
///      ("a"/"an", "ein"/"eine"/"einen", "un"/"une"). The quantity group is therefore
///      optional; when it doesn't capture a digit, the quantity defaults to 1. This is
///      why German genders ("ein" vs "eine") need no special handling — we never match
///      the article, only the digit-or-nothing in that slot.
/// </summary>
public static class SellMessageParser
{
    private static readonly RegexOptions Opts =
        RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // EN: "You sell [qty] ... for [price] gil."
    private static readonly Regex English =
        new(@"[Yy]ou sell (?<qty>\d+)?.*? for (?<price>[\d.,\s]+) gil", Opts);

    // DE: "Du hast [qty] ... für [price] Gil verkauft."  (verb at the end; "Gil" can
    // be singular as in "für 1 Gil verkauft", so it's matched case-insensitively and
    // not assumed plural). "zurückgekauft" (bought back) lacks "verkauft", so buyback
    // lines won't match.
    private static readonly Regex German =
        new(@"[Dd]u hast (?<qty>\d+)?.*? für (?<price>[\d.,\s]+) [Gg]il verkauft", Opts);

    // FR: "Vous vendez [qty] ... pour [price] gil(s)."  Buyback uses "rachetez", which
    // won't match "vendez".
    private static readonly Regex French =
        new(@"[Vv]ous vendez (?<qty>\d+)?.*? pour (?<price>[\d.,\s]+) gils?", Opts);

    // JA: "...×[qty]を[price]ギルで売却しました"  Quantity always present (even ×1).
    // 売却 (sell) distinguishes from 買い戻 (buy back).
    private static readonly Regex Japanese =
        new(@"(?<qty>\d+)を(?<price>[\d.,\s]+)ギルで売却", Opts);

    /// <summary>
    /// Try to parse a sell notice in the given client language. Returns null if the
    /// line isn't a sell notice for that language (e.g. it's a buyback, or unrelated
    /// system text). Unknown languages fall back to the English pattern.
    /// </summary>
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

    /// <summary>
    /// Reduce a captured number to a plain <see cref="uint"/>, ignoring whatever
    /// thousands separators the language used (comma, dot, or any kind of space) and
    /// normalizing full-width digits (０-９, used by the Japanese client) to ASCII.
    /// Returns 0 if there are no digits.
    /// </summary>
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
