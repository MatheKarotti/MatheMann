namespace MatheMann;

// How the main-window ledger is ordered.
public enum LedgerSort
{
    TotalDesc,    // line total, high to low
    TotalAsc,     // line total, low to high
    UnitDesc,     // unit price, high to low
    UnitAsc,      // unit price, low to high
    SellOrder,    // order sold in (no sorting)
}

public static class LedgerSortInfo
{
    // Labels for the dropdown, in enum order. → is a real right-arrow.
    public static readonly string[] Labels =
    {
        "Total (high \u2192 low)",
        "Total (low \u2192 high)",
        "Unit Price (high \u2192 low)",
        "Unit Price (low \u2192 high)",
        "Sell order",
    };
}
