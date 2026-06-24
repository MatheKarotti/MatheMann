using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Interface.Windowing;

namespace MatheMann;

/// <summary>The plugin's only window: a small chart of shop items + a grand total.</summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly ShopReader reader;
    private readonly HistoryWindow historyWindow;
    private readonly SessionHistory config;

    /// <summary>Invoked when the user clicks the cog button to toggle settings.</summary>
    public Action? ToggleSettings { get; set; }

    private static readonly Vector4 Gold   = new(1.00f, 0.85f, 0.20f, 1f);
    private static readonly Vector4 Muted  = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 Header = new(0.80f, 0.80f, 0.80f, 1f);

    private const float QtyColWidth   = 55f;
    private const float PriceColWidth = 100f;

    // Horizontal offset from the Shop window. Negative = overlap into the shop
    // (moves the plugin window right, closer to the shop). Positive = gap.
    private const float AnchorGapX = -90f;
    // Vertical offset below the Shop window's top edge, in pixels.
    private const float AnchorOffsetY = 0f;

    public MainWindow(ShopReader reader, HistoryWindow historyWindow, SessionHistory config) : base("MatheMann##MatheMann")
    {
        this.reader        = reader;
        this.historyWindow = historyWindow;
        this.config        = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 180),
            MaximumSize = new Vector2(750, 900),
        };
        Size          = new Vector2(430, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        WindowStyle.Push();

        // Window background opacity (1.0 = fully opaque, the ImGui default).
        BgAlpha = config.WindowOpacity;

        // Only glue to game windows when the user has enabled it.
        if (!config.GlueToGameWindows) return;

        // Anchor to whichever shop/retainer window is open: sit just left of it.
        if (TryGetAnchorRect(out var ax, out var ay, out var aw))
        {
            var myWidth = ImGui.GetWindowWidth();
            if (myWidth <= 0) myWidth = 430f;

            var pos = new Vector2(ax - myWidth - AnchorGapX, ay + AnchorOffsetY);
            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        }
    }

    public override void PostDraw() => WindowStyle.Pop();

    public override void Draw()
    {
        ImGui.SetWindowFontScale(config.WindowScale);

        DrawTopBar();
        ImGui.Separator();

        if (reader.Entries.Count == 0)
        {
            ImGui.Spacing();

            // Show the friendly prompt normally. Only a genuine warning (patch
            // staleness, or "switch to the Buyback tab") replaces it — the routine
            // idle/"0 items" status does not, so the prompt stays visible.
            if (reader.HasWarning)
                ImGui.TextColored(Muted, reader.Status);
            else
                ImGui.TextColored(Muted, "Sell an item or open a shop's Buyback tab for MatheMann to read and add up prices.");

            ImGui.SetWindowFontScale(1f);
            return;
        }

        DrawTable();
        ImGui.Separator();
        DrawTotalRow();

        ImGui.SetWindowFontScale(1f);
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    private void DrawTopBar()
    {
        // Settings cog (left), using the icon font handle for a proper gear glyph.
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            if (ImGui.SmallButton(FontAwesomeIcon.Cog.ToIconString()))
                ToggleSettings?.Invoke();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("History"))
        {
            if (historyWindow.IsOpen)
                historyWindow.IsOpen = false;
            else
                historyWindow.OpenFromButton();
        }

        // Live counter next to the History button — shown whenever a shop's buyback
        // view is open (even with nothing sold yet) or the ledger has items. When
        // truly idle (no shop), the empty-state body shows the prompt instead, so we
        // keep the top bar clean. A meaningful non-idle status (patch warning, "switch
        // to the Buyback tab") still surfaces here.
        if (reader.OnBuyback || reader.Entries.Count > 0)
        {
            ImGui.SameLine();
            var count = reader.Entries.Count;
            ImGui.TextColored(Muted,
                $"{count} item{(count == 1 ? "" : "s")} / {FormatGil(reader.TotalPrice)} gil");
        }
        else if (reader.HasWarning)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, reader.Status);
        }
    }

    private void DrawTable()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6, 2));

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders
                                    | ImGuiTableFlags.RowBg
                                    | ImGuiTableFlags.SizingFixedFit
                                    | ImGuiTableFlags.ScrollY;

        var height = ImGui.GetContentRegionAvail().Y - 46f;

        if (ImGui.BeginTable("items", 3, flags, new Vector2(-1, height)))
        {
            ImGui.TableSetupColumn("Item",  ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty",   ImGuiTableColumnFlags.WidthFixed, QtyColWidth);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, PriceColWidth);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0); ImGui.TextColored(Header, "Item");
            ImGui.TableSetColumnIndex(1); RightAligned("Qty",   QtyColWidth,   Header);
            ImGui.TableSetColumnIndex(2); RightAligned("Price", PriceColWidth, Header);

            foreach (var row in GetRows())
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(row.Name);
                ImGui.TableSetColumnIndex(1); RightAligned(row.Quantity.ToString(), QtyColWidth);
                ImGui.TableSetColumnIndex(2); RightAligned(FormatGil(row.Price), PriceColWidth, Gold);
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar();
    }

    /// <summary>
    /// The rows to display: either the raw per-sale ledger, or — when grouping is
    /// on — items with the same name collapsed into a single row with summed
    /// quantity and summed gil. Grouping by name only (not price) means large
    /// sales the game splits into multiple capped rows still merge into one line.
    /// HQ items carry the HQ symbol in their name, so they stay separate as
    /// intended. Order follows first appearance.
    /// </summary>
    private IEnumerable<ShopEntry> GetRows()
    {
        if (!config.GroupItems) return reader.Entries;

        var order  = new List<string>();
        var totals = new Dictionary<string, (uint Qty, uint Gil)>();

        foreach (var e in reader.Entries)
        {
            if (!totals.ContainsKey(e.Name))
            {
                order.Add(e.Name);
                totals[e.Name] = (0u, 0u);
            }
            var cur = totals[e.Name];
            totals[e.Name] = (cur.Qty + e.Quantity, cur.Gil + e.Price);
        }

        return order.Select(name =>
        {
            var (qty, gil) = totals[name];
            return new ShopEntry(name, qty, gil);
        });
    }

    private void DrawTotalRow()
    {
        ImGui.Text("Total:");
        ImGui.SameLine();
        ImGui.TextColored(Gold, FormatGil(reader.TotalPrice));
        if (config.ShowRawTotal)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"({reader.TotalPrice})");
        }

        ImGui.SameLine(ImGui.GetWindowWidth() - 72f - ImGui.GetStyle().ItemSpacing.X);
        CopyButton.Draw(
            id: "mainTotal",
            baseLabel: "Copy",
            textToCopy: GilFormatter.Format(reader.TotalPrice, config.CopyFormat),
            changeToken: $"{reader.TotalPrice}:{reader.Entries.Count}");
    }

    // ── Window anchoring ────────────────────────────────────────────────────────

    /// <summary>The in-game windows MatheMann can glue to, in priority order.</summary>
    private static readonly string[] AnchorAddons =
    {
        "Shop",                  // NPC vendor & retainer buyback
        "RetainerSellList",      // the retainer "Markets" sell window
        "RetainerSell",          // the single-item sell price dialog
        "InventoryRetainer",     // the retainer inventory window
        "InventoryRetainerLarge",// the retainer inventory window (large layout)
        "SelectString",          // the retainer option menu (Entrust/Sell/Buyback…)
        "RetainerList",          // the retainer selection list
    };

    /// <summary>
    /// Find the first open shop/retainer window and return its on-screen rect, so
    /// the MatheMann window can glue to it. Returns false if none are open.
    /// </summary>
    public static unsafe bool TryGetAnchorRect(out float x, out float y, out float width)
    {
        foreach (var name in AnchorAddons)
        {
            if (TryGetAddonRect(name, out x, out y, out width))
                return true;
        }
        x = y = width = 0;
        return false;
    }

    /// <summary>Back-compat alias used by the history window to dock to the shop.</summary>
    public static unsafe bool TryGetShopRect(out float x, out float y, out float width)
        => TryGetAnchorRect(out x, out y, out width);

    private static unsafe bool TryGetAddonRect(string name, out float x, out float y, out float width)
    {
        x = y = width = 0;

        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(name).Address;
        if (addon is null || !addon->IsVisible) return false;

        var scale = addon->Scale;
        if (scale <= 0) scale = 1f;

        x     = addon->X;
        y     = addon->Y;
        width = addon->GetScaledWidth(true);
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void RightAligned(string text, float columnWidth, Vector4? colour = null)
    {
        var offset = columnWidth - ImGui.CalcTextSize(text).X - ImGui.GetStyle().ItemSpacing.X;
        if (offset > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

        if (colour.HasValue) ImGui.TextColored(colour.Value, text);
        else                 ImGui.TextUnformatted(text);
    }

    /// <summary>Format a gil value using the user's chosen display format.</summary>
    private string FormatGil(uint value) => GilFormatter.Format(value, config.DisplayFormat);

    public void Dispose() { }
}
