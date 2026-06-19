using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MatheMann;

/// <summary>A single sold item: its name, quantity, and the gil the NPC paid.</summary>
public readonly record struct ShopEntry(string Name, uint Quantity, uint Price);

/// <summary>
/// Reads the vendor Buyback list (the items you've sold to an NPC) and accumulates
/// everything sold this session into a running ledger, so the total survives past
/// the game's rolling 10-item Buyback limit.
///
/// The data is read from the Shop addon's AtkValues at confirmed offsets: the
/// active tab (index 0), item count (index 2), names (index 14 + i), prices
/// (index 75 + i) and quantities (index 380 + i). The addon's lifecycle events
/// trigger a re-read whenever the buyback list changes.
///
/// New sales appear at the TOP of the Buyback list, pushing older rows down; once
/// the list holds 10 rows the oldest falls off. To work out how many rows are new
/// on each update we use the reported item count (grows by the number of new rows,
/// reliable even for identical items) and fall back to shift-detection once the
/// list caps. Buying an item back shrinks the list, which we detect and reconcile.
/// </summary>
public sealed class ShopReader : IDisposable
{
    public IReadOnlyList<ShopEntry> Entries => ledger;
    public uint   TotalPrice    { get; private set; }
    public uint   TotalQuantity { get; private set; }
    public string Status        { get; private set; } = DefaultStatus;

    public bool OnBuyback { get; private set; }

    public event Action? ShopOpened;
    public event Action? ShopClosed;

    /// <summary>
    /// Fired only when a brand-new selling session begins — the buyback view (or a
    /// fresh retainer chat session) transitions from closed to open. Unlike
    /// <see cref="ShopOpened"/>, this does NOT fire again for every subsequent sale
    /// within the same session (ShopOpened fires on each new sale too, so a
    /// manually-closed window reopens). Intended for one-shot effects like the
    /// "play sound on open" setting, so the sound plays once per session instead
    /// of once per item sold.
    /// </summary>
    public event Action? SessionOpened;

    /// <summary>
    /// Fired when the player leaves the retainer entirely (RetainerList closes)
    /// during a retainer selling session. Plugin saves the session to history and
    /// closes the windows in response.
    /// </summary>
    public event Action? RetainerSessionEnded;

    private const string AddonName         = "Shop";
    private const string RetainerListAddon = "RetainerList";
    private const string DefaultStatus     = "Open a shop's Buyback tab.";

    // Confirmed AtkValues layout for the Shop addon. Values sit in parallel
    // "columns" 61 slots wide, indexed by item position.
    private const int    TabIndex        = 0;   // 0 = Current Stock, 1 = Buyback
    private const int    ItemCountIndex  = 2;
    private const int    NameColumn      = 14;
    private const int    PriceColumn     = 75;
    private const int    QuantityColumn  = 380;
    private const int    MaxItems        = 100;

    private readonly List<ShopEntry> ledger = new();
    private List<ShopEntry> lastSnapshot = new();
    private bool wasShown;

    /// <summary>Set when the buyback read looked stale (addon reports items but none
    /// resolved), so Refresh can keep the "may need updating" status visible.</summary>
    private bool layoutLooksStale;

    /// <summary>
    /// The character/FC for the CURRENT session, captured the moment the ledger
    /// gets its first item — not at session end. Captured this early because
    /// ObjectTable.LocalPlayer can already be null by the time a zone change
    /// (which ends an NPC session) actually fires, leaving Character/FreeCompany
    /// blank on the saved session ("Unknown" in the history window) otherwise.
    /// </summary>
    private string sessionCharacter    = "";
    private string sessionFreeCompany  = "";

    /// <summary>
    /// True while we're tracking a retainer selling session via chat messages
    /// (the retainer sell window has no buyback view, so sales are read from chat).
    /// Distinguishes the retainer flow from the NPC vendor flow.
    /// </summary>
    private bool retainerSession;

    /// <summary>
    /// Add a sale parsed from a chat message ("You sell N item for X gil"). This
    /// same chat line is produced for BOTH retainer sales and NPC vendor sales, but
    /// only retainer sales actually need it — NPC sales are already tracked
    /// directly from the Shop addon's Buyback list, which updates immediately.
    /// So: if the Shop window is currently open (any tab), this message is from an
    /// NPC sale the window-based reader will pick up on its next refresh, and
    /// adding it here too would double-count it. Only retainer sales (where the
    /// Shop addon is NOT open — the separate Markets/sell window is) get added.
    /// </summary>
    public void AddChatSale(string name, uint quantity, uint price)
    {
        if (IsShopWindowOpen())
            return;

        // Starting a fresh retainer session: clear any stale NPC ledger first.
        bool freshSession = !retainerSession;
        if (freshSession)
        {
            ClearLedger();
            retainerSession = true;
        }

        // New sales go to the top, matching the in-game buyback ordering.
        ledger.Insert(0, new ShopEntry(name, quantity, price));
        CaptureIdentity();

        RecalculateTotals();
        Status = $"{ledger.Count} item{(ledger.Count == 1 ? "" : "s")} — {TotalPrice:N0} gil";

        // Make the window visible. Fire unconditionally so that if the user closed
        // it and then sells again, it reopens. Re-opening an open window is a no-op.
        wasShown = true;
        ShopOpened?.Invoke();

        // Only the FIRST sale of a fresh session counts as "opening" for one-shot
        // effects (sound) — subsequent sales just grow the ledger.
        if (freshSession)
            SessionOpened?.Invoke();
    }

    /// <summary>True if the "Shop" addon is currently open, regardless of tab.
    /// Used to tell an NPC vendor sale (window already open, window-based reader
    /// handles it) apart from a retainer sale (Shop addon closed, chat is the
    /// only source) for the same chat sell line.</summary>
    private unsafe bool IsShopWindowOpen()
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(AddonName).Address;
        return addon is not null && addon->IsVisible;
    }

    public ShopReader()
    {
        // The retainer "Item buyback" window reuses the same "Shop" addon as the
        // NPC vendor, so a single set of listeners covers both.
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   AddonName, OnUpdate);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, AddonName, OnUpdate);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnClose);

        // Returning to the retainer choose list ends the current retainer's selling
        // session (each retainer has its own buyback). The RetainerList window closes
        // when you enter a specific retainer and re-opens (PostSetup) when you back
        // out to the choose list — so PostSetup while a session is active is the exact
        // "left this retainer" signal. It does NOT fire when merely drilling into the
        // Sell or Buyback sub-windows, so it won't end the session prematurely.
        // Guarded by retainerSession so the initial list appearance (before any sale)
        // doesn't trigger a save.
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, RetainerListAddon, OnRetainerListReturn);
    }

    /// <summary>
    /// The retainer choose list re-appeared (PostSetup). If a retainer selling
    /// session was active, the player backed out of that retainer, so end it.
    /// </summary>
    private void OnRetainerListReturn(AddonEvent type, AddonArgs args)
    {
        if (retainerSession)
            RetainerSessionEnded?.Invoke();
    }

    // ── Addon lifecycle (used only as a "something changed" trigger) ───────────

    private void OnUpdate(AddonEvent type, AddonArgs args)
    {
        int before = ledger.Count;
        bool wasOnBuyback = OnBuyback;

        Refresh();

        // Fire ShopOpened when the buyback view first becomes active, OR whenever a
        // new item appears in the buyback list (count grew). The latter means that
        // even if the user manually closed the MatheMann window, selling another
        // item reopens it — the window should always be present on the buyback view.
        bool justOpened = OnBuyback && !wasOnBuyback;
        bool grew       = OnBuyback && ledger.Count > before;

        if (justOpened || grew)
        {
            wasShown = true;
            ShopOpened?.Invoke();

            // Only the actual open transition counts as "opening" for one-shot
            // effects (sound) — growth from selling another item does not.
            if (justOpened)
                SessionOpened?.Invoke();
        }
        else if (!OnBuyback && wasShown)
        {
            wasShown = false;
            ShopClosed?.Invoke();
        }
    }

    private void OnClose(AddonEvent type, AddonArgs args)
    {
        // Note: we deliberately do NOT clear the ledger here. The game keeps the
        // Buyback list in memory until the player changes zones, so closing the
        // shop window mid-session should preserve the running total. The ledger is
        // saved to history and cleared on zone change (see EndSession).
        OnBuyback = false;
        if (wasShown)
        {
            wasShown = false;
            ShopClosed?.Invoke();
        }
    }

    // ── Core: read the shop agent and reconcile the ledger ─────────────────────

    /// <summary>
    /// Read the current Buyback list and fold it into the ledger. Both the NPC
    /// vendor and the retainer "Item buyback" use the same "Shop" addon, so a
    /// single AtkValues read covers both (distinguished by the view value at
    /// index 0: 1 = NPC vendor buyback, 2 = retainer buyback).
    /// </summary>
    public unsafe void Refresh()
    {
        uint view;
        List<ShopEntry>? snapshot = TryReadNpcShop(out view);

        if (snapshot is null)
        {
            OnBuyback        = false;
            layoutLooksStale = false;
            Status           = DefaultStatus;
            return;
        }

        OnBuyback = true;

        if (view == 2)
        {
            // Retainer buyback window: this is the authoritative list for the
            // current retainer session, so replace the chat-built ledger with it.
            // This naturally drops anything that was bought back.
            retainerSession = true;
            ledger.Clear();
            ledger.AddRange(snapshot);
            lastSnapshot = snapshot;
        }
        else
        {
            // NPC vendor buyback: accumulate across refreshes as before.
            retainerSession = false;
            MergeIntoLedger(snapshot);
            lastSnapshot = snapshot;
        }

        if (ledger.Count > 0) CaptureIdentity();

        RecalculateTotals();

        // Keep the staleness warning if the read looked broken; otherwise show the
        // normal item/total summary.
        if (!layoutLooksStale)
            Status = $"{ledger.Count} item{(ledger.Count == 1 ? "" : "s")} — {TotalPrice:N0} gil";
    }

    /// <summary>
    /// Read the NPC vendor Buyback list from the "Shop" addon's AtkValues. Returns
    /// null if that addon isn't open or isn't on the Buyback tab.
    /// </summary>
    private unsafe List<ShopEntry>? TryReadNpcShop(out uint view)
    {
        view = 0;

        var addon = (AddonShop*)Plugin.GameGui.GetAddonByName("Shop").Address;
        if (addon is null || !addon->AtkUnitBase.IsVisible)
            return null;

        var atkValues = addon->AtkUnitBase.AtkValuesSpan;

        // The Shop addon backs both the NPC vendor and the retainer "Item buyback".
        // Index 0 identifies the view: 1 = NPC vendor's Buyback tab, 2 = retainer
        // buyback (which opens directly into buyback with no Current Stock tab).
        // Both expose the same name/price/quantity layout, so we read either.
        view = atkValues.Length > TabIndex ? atkValues[TabIndex].UInt : 0;
        bool isBuyback = view == 1 || view == 2;
        if (!isBuyback)
        {
            // Shop is open but on Current Stock — treat as "not buyback".
            Status = "Switch to the Buyback tab.";
            return null;
        }

        // Values are stored in parallel "columns" 61 slots wide, indexed by item
        // position: count at index 2, name at 14+i, price at 75+i, quantity at 380+i.
        var snapshot = new List<ShopEntry>();

        uint itemCount = atkValues.Length > ItemCountIndex ? atkValues[ItemCountIndex].UInt : 0;
        if (itemCount > MaxItems) itemCount = 0;

        for (var i = 0; i < itemCount; i++)
        {
            int nameIdx  = NameColumn     + i;
            int priceIdx = PriceColumn    + i;
            int qtyIdx   = QuantityColumn + i;
            if (priceIdx >= atkValues.Length) break;

            var name  = nameIdx < atkValues.Length ? ReadAtkString(atkValues[nameIdx]) : "";
            var price = atkValues[priceIdx].UInt;
            var qty   = qtyIdx  < atkValues.Length ? atkValues[qtyIdx].UInt : 0u;

            // The retainer view sometimes reports one more slot than there are real
            // items; the extra slot has no name but carries stale price/qty values.
            // A genuine buyback row always has a name, so skip nameless rows.
            if (string.IsNullOrEmpty(name) || name == "?")
                continue;

            snapshot.Add(new ShopEntry(name, qty, price));
        }

        // Offset-staleness heuristic (NOT a correctness guarantee — see note below).
        // The index bounds checks above stop us reading PAST the array, but they
        // can't tell us if a game patch SHIFTED the columns so that, e.g., the price
        // now lives at a different index. In that case the old indices are still
        // in-bounds and we'd silently read the wrong numbers. The one signal we have
        // is this: if the addon says there are items (itemCount > 0) but we couldn't
        // resolve a single valid named row, the name column has almost certainly
        // moved — so surface a soft "may need updating" status instead of pretending
        // the (empty) read succeeded. This is conservative: in normal operation a
        // populated buyback list always yields at least one named row, so false
        // positives are unlikely. It does NOT catch a shift that still lands on
        // plausible-looking data; that can only be fixed by re-deriving the offsets.
        //
        // To re-derive offsets after a patch: add temporary Plugin.Log lines dumping
        // each AtkValue index/type/value while a buyback window is open, and find the
        // columns where names/prices/quantities now sit (the original discovery method).
        if (itemCount > 0 && snapshot.Count == 0)
        {
            layoutLooksStale = true;
            Status = "Couldn't read the buyback list — MatheMann may need updating after a game patch.";
        }
        else
        {
            layoutLooksStale = false;
        }

        return snapshot;
    }

    // ── Ledger reconciliation ──────────────────────────────────────────────────

    private void MergeIntoLedger(List<ShopEntry> snapshot)
    {
        if (ledger.Count == 0 && lastSnapshot.Count == 0)
        {
            ledger.AddRange(snapshot);
            return;
        }

        if (snapshot.Count < lastSnapshot.Count)
        {
            // List shrank → an item was bought back (covers emptying completely).
            RemoveBoughtBack(snapshot);
            return;
        }

        if (snapshot.Count == 0)
            return;

        int newCount;
        if (snapshot.Count > lastSnapshot.Count)
        {
            // List grew but isn't full yet: the count grew by exactly the new rows.
            newCount = snapshot.Count - lastSnapshot.Count;
        }
        else
        {
            // Same count: either a plain redraw (no shift → 0 new) or the list is
            // full and rotated as new items pushed old ones off (shift → N new).
            // CountNewTopRows handles both, so we don't need to know the cap size —
            // which is good, since the NPC list caps at 10 and the retainer at 20.
            newCount = CountNewTopRows(snapshot, lastSnapshot);
        }

        for (var i = newCount - 1; i >= 0; i--)
            ledger.Insert(0, snapshot[i]);
    }

    private void RemoveBoughtBack(List<ShopEntry> snapshot)
    {
        var stillPresent = new List<ShopEntry>(snapshot);
        foreach (var old in lastSnapshot)
        {
            int idx = stillPresent.IndexOf(old);
            if (idx >= 0)
                stillPresent.RemoveAt(idx);
            else
            {
                int ledgerIdx = ledger.IndexOf(old);
                if (ledgerIdx >= 0) ledger.RemoveAt(ledgerIdx);
            }
        }
    }

    private static int CountNewTopRows(List<ShopEntry> current, List<ShopEntry> previous)
    {
        if (previous.Count == 0) return current.Count;

        for (var k = 0; k <= current.Count; k++)
        {
            var aligned = true;
            for (var j = 0; j + k < current.Count && j < previous.Count; j++)
            {
                if (!current[j + k].Equals(previous[j])) { aligned = false; break; }
            }
            if (aligned) return k;
        }
        return current.Count;
    }

    /// <summary>
    /// Read a string AtkValue's text. The String field is a CStringPointer whose
    /// ToString() yields the UTF-8 text; we then strip the binary SeString payload
    /// markup so only the readable item name remains.
    /// </summary>
    private static unsafe string ReadAtkString(AtkValue value)
    {
        if (value.String.Value == null) return "?";
        var raw = value.String.ToString();
        return string.IsNullOrEmpty(raw) ? "?" : CleanItemName(raw);
    }

    /// <summary>Strip SeString payload markup and control bytes from an item name.</summary>
    private static string CleanItemName(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        var inPayload = false;
        foreach (var c in raw)
        {
            switch (c)
            {
                case '\x02': inPayload = true;  continue;
                case '\x03': inPayload = false; continue;
            }
            if (inPayload || c < ' ') continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private void RecalculateTotals()
    {
        TotalPrice    = 0;
        TotalQuantity = 0;
        foreach (var e in ledger)
        {
            TotalPrice    += e.Price;
            TotalQuantity += e.Quantity;
        }
    }

    /// <summary>
    /// Stamp the current character/FC onto the session, if not already captured
    /// this session. Idempotent — safe to call on every update. Called as soon as
    /// the ledger has its first item, while the player object is still guaranteed
    /// valid (unlike at session end, which can coincide with a zone transition).
    /// </summary>
    private void CaptureIdentity()
    {
        if (sessionCharacter != "") return;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null) return;

        sessionCharacter   = player.Name.TextValue;
        sessionFreeCompany = player.CompanyTag.TextValue;
    }

    /// <summary>
    /// End the current selling session: if the ledger holds anything, return it as
    /// a <see cref="SavedSession"/> for the history, then clear. Called on zone
    /// change, which is when the game wipes the Buyback list. Returns null if there
    /// was nothing to save.
    /// </summary>
    public SavedSession? EndSession()
    {
        SavedSession? saved = null;

        if (ledger.Count > 0)
        {
            // Fallback only — normally already captured via CaptureIdentity() the
            // moment the first item was added, while the player object was still
            // guaranteed valid.
            CaptureIdentity();

            saved = new SavedSession
            {
                EndedAt     = DateTime.Now,
                TotalGil    = TotalPrice,
                ItemCount   = (uint)ledger.Count,
                Character   = sessionCharacter,
                FreeCompany = sessionFreeCompany,
                Items       = ledger.ConvertAll(e => new SavedItem
                {
                    Name     = e.Name,
                    Quantity = e.Quantity,
                    Price    = e.Price,
                }),
            };
        }

        ClearLedger();
        return saved;
    }

    private void ClearLedger()
    {
        ledger.Clear();
        lastSnapshot       = new List<ShopEntry>();
        TotalPrice         = 0;
        TotalQuantity      = 0;
        OnBuyback          = false;
        retainerSession    = false;
        sessionCharacter   = "";
        sessionFreeCompany = "";
        Status             = DefaultStatus;
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(OnUpdate);
        Plugin.AddonLifecycle.UnregisterListener(OnClose);
        Plugin.AddonLifecycle.UnregisterListener(OnRetainerListReturn);
    }
}
