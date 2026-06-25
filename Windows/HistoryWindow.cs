using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MatheMann;

public sealed class HistoryWindow : Window, IDisposable
{
    private readonly SessionHistory history;

    // True if opened from the in-shop History button (closes with the shop, unless
    // HistoryClosesWithShop is off). False if opened via /mama history (stays open).
    public bool TiedToShop { get; private set; }

    public void OpenFromButton()
    {
        TiedToShop = true;
        IsOpen     = true;
    }

    public void ToggleStandalone()
    {
        if (IsOpen)
        {
            IsOpen = false;
        }
        else
        {
            TiedToShop = false;
            IsOpen     = true;
        }
    }

    public void OnShopClosed()
    {
        if (TiedToShop && history.HistoryClosesWithShop)
            IsOpen = false;
    }

    // Negative so the window overlaps the shop's gold border a bit.
    private const float AnchorGap = -9f;

    private static readonly Vector4 Gold   = new(1.00f, 0.85f, 0.20f, 1f);
    private static readonly Vector4 Muted  = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 Header = new(0.80f, 0.80f, 0.80f, 1f);

    private const float QtyColWidth   = 55f;
    private const float PriceColWidth = 100f;

    public HistoryWindow(SessionHistory history) : base("MatheMann History##MatheMannHistory")
    {
        this.history = history;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 200),
            MaximumSize = new Vector2(750, 900),
        };
        Size          = new Vector2(440, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        WindowStyle.Push();

        BgAlpha = history.WindowOpacity;

        // Dock to the right edge of the shop/retainer window only when opened from
        // the in-shop button AND the user has enabled gluing. The "/mama history" standalone
        // window floats wherever the user puts it.
        if (TiedToShop && history.GlueToGameWindows &&
            MainWindow.TryGetShopRect(out var shopX, out var shopY, out var shopW))
        {
            var pos = new Vector2(shopX + shopW + AnchorGap, shopY);
            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        }
    }

    public override void PostDraw() => WindowStyle.Pop();

    public override void Draw()
    {
        ImGui.SetWindowFontScale(history.WindowScale);

        if (history.Sessions.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Muted, "No past sessions yet.");
            ImGui.TextColored(Muted, "A session is saved when you leave the retainer or change zones.");
            ImGui.SetWindowFontScale(1f);
            return;
        }

        if (ImGui.SmallButton("Clear history"))
            history.Clear();

        ImGui.Separator();

        // Reserve room for the footer (separator + "Total:" + one line per character
        // + "Grand total:") so it stays pinned below the scrolling list. A fixed
        // guess overlaps the list once there's more than a character or two.
        var footerHeight = 0f;
        if (history.ShowGrandTotal)
        {
            var characterCount = CountDistinctCharacters();
            var lineH = ImGui.GetTextLineHeightWithSpacing();
            footerHeight = lineH * (characterCount + 2)   // "Total:" + N + "Grand total:"
                         + ImGui.GetStyle().ItemSpacing.Y
                         + ImGui.GetStyle().FramePadding.Y * 2;
        }

        ImGui.BeginChild("sessions", new Vector2(0, -footerHeight), false);

        for (var i = 0; i < history.Sessions.Count; i++)
        {
            var s = history.Sessions[i];

            // Header line: "2026-06-17 14:32 · 29 items · 4.772 gil"
            var label = $"{s.EndedAt:yyyy-MM-dd HH:mm}  ·  {s.ItemCount} item{(s.ItemCount == 1 ? "" : "s")}  ·  {FormatGil(s.TotalGil)} gil";

            // Unique id per node so identical labels still collapse independently.
            var open = ImGui.CollapsingHeader($"{label}##session{i}");

            // Second line: which character sold, and their Free Company.
            ImGui.Indent(20f);
            ImGui.TextColored(Muted, CharacterKey(s));
            ImGui.Unindent(20f);

            if (open)
            {
                // Inside the expanded section so it can't toggle the header. Keyed
                // by timestamp+total so each row's Copied state is stable across trims.
                CopyButton.Draw(
                    id: $"session{s.EndedAt.Ticks}_{s.TotalGil}",
                    baseLabel: "Copy total",
                    textToCopy: GilFormatter.Format(s.TotalGil, history.CopyFormat),
                    changeToken: s.TotalGil.ToString());

                DrawSessionItems(s, i);
            }
        }

        ImGui.EndChild();

        if (history.ShowGrandTotal)
            DrawGrandTotal();

        ImGui.SetWindowFontScale(1f);
    }

    // Shared so the reserved footer height and the drawn footer agree on line count.
    private static string CharacterKey(SavedSession s) =>
        string.IsNullOrEmpty(s.Character) ? "Unknown character"
        : string.IsNullOrEmpty(s.FreeCompany) ? s.Character
        : $"{s.Character}  «{s.FreeCompany}»";

    private int CountDistinctCharacters()
    {
        var seen = new HashSet<string>();
        foreach (var s in history.Sessions)
            seen.Add(CharacterKey(s));
        return seen.Count;
    }

    // Total across all sessions, by character. Only when ShowGrandTotal is on.
    private void DrawGrandTotal()
    {
        ImGui.Separator();

        var perCharacter = new Dictionary<string, ulong>();
        ulong grand = 0;
        foreach (var s in history.Sessions)
        {
            var who = CharacterKey(s);
            perCharacter.TryGetValue(who, out var cur);
            perCharacter[who] = cur + s.TotalGil;
            grand += s.TotalGil;
        }

        ImGui.TextColored(Header, "Total:");

        foreach (var (who, sum) in perCharacter)
        {
            ImGui.TextUnformatted("   " + who);
            ImGui.SameLine();
            RightAlignedToEnd(FormatGil(sum) + " gil", Gold);
        }

        ImGui.TextColored(Header, "Grand total:");
        ImGui.SameLine();
        RightAlignedToEnd(FormatGil(grand) + " gil", Gold);
    }

    private static void RightAlignedToEnd(string text, Vector4 colour)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var textW = ImGui.CalcTextSize(text).X;
        if (avail > textW) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - textW);
        ImGui.TextColored(colour, text);
    }

    private void DrawSessionItems(SavedSession s, int index)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6, 2));

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders
                                    | ImGuiTableFlags.RowBg
                                    | ImGuiTableFlags.SizingFixedFit;

        if (ImGui.BeginTable($"items{index}", 3, flags))
        {
            ImGui.TableSetupColumn("Item",  ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty",   ImGuiTableColumnFlags.WidthFixed, QtyColWidth);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, PriceColWidth);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            ImGui.TableSetColumnIndex(0); ImGui.TextColored(Header, "Item");
            ImGui.TableSetColumnIndex(1); RightAligned("Qty",   QtyColWidth,   Header);
            ImGui.TableSetColumnIndex(2); RightAligned("Price", PriceColWidth, Header);

            foreach (var item in s.Items)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(item.Name);
                ImGui.TableSetColumnIndex(1); RightAligned(item.Quantity.ToString(), QtyColWidth);
                ImGui.TableSetColumnIndex(2); RightAligned(FormatGil(item.Price), PriceColWidth, Gold);
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar();
        ImGui.Spacing();
    }

    private static void RightAligned(string text, float columnWidth, Vector4? colour = null)
    {
        var offset = columnWidth - ImGui.CalcTextSize(text).X - ImGui.GetStyle().ItemSpacing.X;
        if (offset > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

        if (colour.HasValue) ImGui.TextColored(colour.Value, text);
        else                 ImGui.TextUnformatted(text);
    }

    private string FormatGil(ulong value) => GilFormatter.Format(value, history.DisplayFormat);

    public void Dispose() { }
}
