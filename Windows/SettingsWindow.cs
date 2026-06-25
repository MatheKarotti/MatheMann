using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace MatheMann;

// Settings, grouped Behaviour / Appearance / Numbers / History. Each one has an (i)
// icon with the explanation on hover instead of a permanent subtitle.
public sealed class SettingsWindow : Window, IDisposable
{
    private readonly SessionHistory config;

    private static readonly Vector4 Muted = new(0.55f, 0.55f, 0.55f, 1f);

    public SettingsWindow(SessionHistory config) : base("MatheMann Settings##MatheMannSettings")
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 240),
            MaximumSize = new Vector2(600, 900),
        };
        Size          = new Vector2(380, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        WindowStyle.Push();
    }

    public override void PostDraw()
    {
        WindowStyle.Pop();
    }

    public override void Draw()
    {
        // ── Behaviour ────────────────────────────────────────────────────────────
        Section("Behaviour");

        Toggle("Play sound when opening", config.PlaySound, v => config.PlaySound = v,
            "Plays the MatheMann intro when the main window opens.");

        Toggle("Glue windows to the game UI", config.GlueToGameWindows, v => config.GlueToGameWindows = v,
            "Anchors the MatheMann windows to the shop/retainer window.");

        Toggle("Group Items", config.GroupItems, v => config.GroupItems = v,
            "Groups together multiple instances of the same items (for example Minions).");

        Toggle("Show unit price", config.ShowUnitPrice, v => config.ShowUnitPrice = v,
            "Adds a column showing the price per single item.");

        Toggle("Sort by value", config.SortByValue, v => config.SortByValue = v,
            "Sorts the table by price, most valuable first, instead of the order you sold in.");

        Divider();

        // ── Appearance ───────────────────────────────────────────────────────────
        Section("Appearance");

        var scale = config.WindowScale;
        LabelWithInfo("Window scale", "Size of the text/UI in the MatheMann windows.");
        if (ImGui.SliderFloat("##scale", ref scale, 0.7f, 2.0f, "%.2fx"))
        {
            config.WindowScale = scale;
            config.Save();
        }

        var opacity = config.WindowOpacity;
        LabelWithInfo("Window opacity", "Background transparency of the MatheMann windows (0.30 = very see-through).");
        if (ImGui.SliderFloat("##opacity", ref opacity, 0.3f, 1.0f, "%.2f"))
        {
            config.WindowOpacity = opacity;
            config.Save();
        }

        Divider();

        // ── Numbers ──────────────────────────────────────────────────────────────
        Section("Numbers");

        LabelWithInfo("Display number format", "How gil numbers are shown in the tables and totals.");
        var displayIndex = (int)config.DisplayFormat;
        if (ImGui.Combo("##displayFormat", ref displayIndex, GilFormatter.Labels, GilFormatter.Labels.Length))
        {
            config.DisplayFormat = (GilFormat)displayIndex;
            config.Save();
        }

        LabelWithInfo("Copy number format", "How the Copy buttons format numbers on the clipboard.");
        var copyIndex = (int)config.CopyFormat;
        if (ImGui.Combo("##copyFormat", ref copyIndex, GilFormatter.Labels, GilFormatter.Labels.Length))
        {
            config.CopyFormat = (GilFormat)copyIndex;
            config.Save();
        }

        Toggle("Show raw total value", config.ShowRawTotal, v => config.ShowRawTotal = v,
            "Adds the unformatted number in parentheses next to the main total, e.g. \"6.890 (6890)\".");

        Divider();

        // ── History ──────────────────────────────────────────────────────────────
        Section("History");

        Toggle("Show grand total in history", config.ShowGrandTotal, v => config.ShowGrandTotal = v,
            "Shows the total-across-all-sessions footer at the bottom of the history window.");

        Toggle("History window closes with the shop", config.HistoryClosesWithShop, v => config.HistoryClosesWithShop = v,
            "When opened from the in-shop History button, also close it when the shop closes. " +
            "Turn off to keep it pinned open. (\"/mama history\" always opens it standalone.)");

        LabelWithInfo("Sessions to keep", "Older sessions beyond this count are dropped automatically.");
        var maxSessions = config.MaxSessions;
        if (ImGui.SliderInt("##maxSessions", ref maxSessions, 1, 100))
        {
            config.MaxSessions = maxSessions;
            config.Trim();
            config.Save();
        }
    }

    // ── Small UI helpers ────────────────────────────────────────────────────────

    private static void Section(string title)
    {
        ImGui.TextDisabled(title);
        ImGui.Spacing();
    }

    private static void Divider()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    // Muted (i) icon, tooltip on hover. Icon font via using so it's popped on throw.
    private static void InfoIcon(string tooltip)
    {
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Muted);
            ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20f);
            ImGui.TextUnformatted(tooltip);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    // Label + info icon on one line, for sliders/combos (label sits above the control).
    private static void LabelWithInfo(string label, string tooltip)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        InfoIcon(tooltip);
    }

    // Checkbox via getter/setter (works with config properties), info icon, saves on change.
    private void Toggle(string label, bool current, Action<bool> set, string tooltip)
    {
        var value = current;
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            config.Save();
        }
        ImGui.SameLine();
        InfoIcon(tooltip);
    }

    public void Dispose() { }
}
