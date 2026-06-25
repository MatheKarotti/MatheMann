using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MatheMann;

public readonly record struct ShopEntry(string Name, uint Quantity, uint Price);

// Reads the vendor Buyback list (items you've sold) from the Shop addon's AtkValues
// and accumulates everything sold this visit, so the total outlives the game's
// 10-item buyback cap. Offsets: tab at 0, count at 2, names at 14+i, prices at 75+i,
// qty at 380+i. New sales appear at the top; we track new rows by the reported count
// and fall back to shift-detection once the list caps at 10. Buying back shrinks the
// list and gets reconciled. See DEVNOTES for the offset details and how they were found.
public sealed class ShopReader : IDisposable
{
    public IReadOnlyList<ShopEntry> Entries => ledger;
    public uint   TotalPrice    { get; private set; }
    public uint   TotalQuantity { get; private set; }
    public string Status        { get; private set; } = DefaultStatus;

    // True only for a real warning (wrong shop tab, or offsets look stale), not the
    // idle prompt or the item counter. MainWindow uses it to decide prompt vs warning.
    public bool HasWarning { get; private set; }

    public bool OnBuyback { get; private set; }

    public event Action? ShopOpened;
    public event Action? ShopClosed;

    // Fires once when a selling session starts, unlike ShopOpened which fires per sale
    // (so a manually-closed window reopens). Used for one-shot stuff like the sound.
    public event Action? SessionOpened;

    // Fires when you leave the retainer entirely (RetainerList closes). Plugin saves
    // the session and closes the windows.
    public event Action? RetainerSessionEnded;

    // Shown when idle. MainWindow checks against this to tell idle apart from a real
    // warning status.
    public const string IdleStatus = "Open a shop's Buyback tab.";

    private const string AddonName         = "Shop";
    private const string RetainerListAddon = "RetainerList";
    private const string DefaultStatus     = IdleStatus;

    // Shop addon AtkValues layout, indexed by item position. Hardcoded - will break
    // on a game patch (see DEVNOTES for how to re-derive).
    private const int    TabIndex        = 0;   // 0 = Current Stock, 1 = Buyback
    private const int    ItemCountIndex  = 2;
    private const int    NameColumn      = 14;
    private const int    PriceColumn     = 75;
    private const int    QuantityColumn  = 380;
    private const int    MaxItems        = 100;

    private readonly List<ShopEntry> ledger = new();
    private List<ShopEntry> lastSnapshot = new();
    private bool wasShown;

    // Set when a read looked stale (count > 0 but nothing resolved) so Refresh keeps
    // the warning status.
    private bool layoutLooksStale;

    // Captured when the ledger gets its first item, NOT at session end - LocalPlayer
    // can be null during the zone change that ends an NPC session. See DEVNOTES.
    private string sessionCharacter    = "";
    private string sessionFreeCompany  = "";

    private bool retainerSession;

    // The "you sell N for X gil" chat line fires for NPC sales too, not just
    // retainer sales. NPC sales are already read from the Shop addon, so if the Shop
    // window is open we skip this to avoid double-counting - chat only handles the
    // retainer case (Markets window, no buyback view).
    public void AddChatSale(string name, uint quantity, uint price)
    {
        if (IsShopWindowOpen())
            return;

        bool freshSession = !retainerSession;
        if (freshSession)
        {
            ClearLedger();
            retainerSession = true;
        }

        ledger.Insert(0, new ShopEntry(name, quantity, price));
        CaptureIdentity();

        RecalculateTotals();
        HasWarning = false;
        Status = $"{ledger.Count} item{(ledger.Count == 1 ? "" : "s")} - {TotalPrice:N0} gil";

        // Fire unconditionally so reselling after a manual close reopens the window.
        wasShown = true;
        ShopOpened?.Invoke();

        // Sound etc. should fire once per session, not per item.
        if (freshSession)
            SessionOpened?.Invoke();
    }

    private unsafe bool IsShopWindowOpen()
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(AddonName).Address;
        return addon is not null && addon->IsVisible;
    }

    public ShopReader()
    {
        // Retainer "Item buyback" reuses the same Shop addon as the NPC vendor, so
        // one set of listeners covers both.
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup,   AddonName, OnUpdate);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, AddonName, OnUpdate);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnClose);

        // RetainerList re-opens (PostSetup) when you back out to the retainer choose
        // list, which is our "this retainer's session is done" signal. It doesn't
        // fire when drilling into Sell/Buyback. Guarded by retainerSession so the
        // initial list appearance before any sale doesn't save. See DEVNOTES for the
        // approaches that didn't work.
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, RetainerListAddon, OnRetainerListReturn);
    }

    // RetainerList re-opened = backed out to the choose list, so end the session.
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

        // Reopen the window when the buyback view becomes active or a new sale grows
        // the ledger (so reselling after a manual close brings it back).
        bool justOpened = OnBuyback && !wasOnBuyback;
        bool grew       = OnBuyback && ledger.Count > before;

        if (justOpened || grew)
        {
            wasShown = true;
            ShopOpened?.Invoke();

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
        // Don't clear the ledger here - the game keeps the buyback list until zone
        // change, so closing the shop mid-session should keep the running total.
        // Saved + cleared on zone change in EndSession.
        OnBuyback = false;
        if (wasShown)
        {
            wasShown = false;
            ShopClosed?.Invoke();
        }
    }

    // Read the current buyback list and fold it into the ledger. NPC vendor and
    // retainer buyback share the Shop addon; view at index 0 distinguishes them
    // (1 = NPC, 2 = retainer).
    public unsafe void Refresh()
    {
        uint view;
        List<ShopEntry>? snapshot = TryReadNpcShop(out view);

        if (snapshot is null)
        {
            OnBuyback        = false;
            layoutLooksStale = false;
            // Keep a warning if one was set (shop open on Current Stock); otherwise
            // the shop's fully closed so show the idle prompt.
            if (!HasWarning)
                Status = DefaultStatus;
            return;
        }

        OnBuyback = true;

        if (view == 2)
        {
            // Retainer buyback is authoritative - replace the chat-built ledger,
            // which drops anything bought back.
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
        // normal item/total summary (and clear any prior warning).
        if (!layoutLooksStale)
        {
            HasWarning = false;
            Status = $"{ledger.Count} item{(ledger.Count == 1 ? "" : "s")} - {TotalPrice:N0} gil";
        }
    }

    // Reads the buyback list from the Shop addon. Null if it's closed or not on the
    // Buyback tab.
    private unsafe List<ShopEntry>? TryReadNpcShop(out uint view)
    {
        view = 0;

        var addon = (AddonShop*)Plugin.GameGui.GetAddonByName("Shop").Address;
        if (addon is null || !addon->AtkUnitBase.IsVisible)
        {
            // Shop fully closed — no warning, caller will reset to the idle status.
            HasWarning = false;
            return null;
        }

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
            Status     = "Switch to the Buyback tab.";
            HasWarning = true;
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
            HasWarning = true;
            Status = "Couldn't read the buyback list - MatheMann may need updating after a game patch.";
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

    // Read a string AtkValue and strip SeString markup so only the name is left.
    private static unsafe string ReadAtkString(AtkValue value)
    {
        if (value.String.Value == null) return "?";
        var raw = value.String.ToString();
        return string.IsNullOrEmpty(raw) ? "?" : CleanItemName(raw);
    }

    // Strip SeString payload markup and control bytes from an item name.
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

    // Stamp character/FC onto the session if not already done. Idempotent, called as
    // soon as the ledger has an item - LocalPlayer can be null at session end (zone change).
    private void CaptureIdentity()
    {
        if (sessionCharacter != "") return;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null) return;

        sessionCharacter   = player.Name.TextValue;
        sessionFreeCompany = player.CompanyTag.TextValue;
    }

    // End the session: return the ledger as a SavedSession (or null if empty), then
    // clear. Called on zone change, when the game wipes the buyback list.
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
        HasWarning         = false;
        Status             = DefaultStatus;
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(OnUpdate);
        Plugin.AddonLifecycle.UnregisterListener(OnClose);
        Plugin.AddonLifecycle.UnregisterListener(OnRetainerListReturn);
    }
}
