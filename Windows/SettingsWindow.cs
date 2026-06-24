using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace MatheMann;

/// <summary>
/// All plugin settings, grouped into Behaviour / Appearance / Numbers / History.
/// Each setting shows a small (i) info icon next to its label; hovering it reveals
/// the explanation as a tooltip, keeping the window clean instead of printing a
/// permanent grey subtitle under every control.
/// </summary>
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

    /// <summary>
    /// Draws a muted (i) info-circle icon. Hovering it shows <paramref name="tooltip"/>.
    /// Uses Dalamud's icon font for the glyph (same mechanism as the main window cog),
    /// wrapped in a using-block so the font is popped even if drawing throws.
    /// </summary>
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

    /// <summary>A plain text label followed by an info icon on the same line.
    /// Used for non-checkbox controls (sliders, combos) where the label sits above
    /// the control.</summary>
    private static void LabelWithInfo(string label, string tooltip)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        InfoIcon(tooltip);
    }

    /// <summary>A checkbox bound through a getter value + setter callback (so it works
    /// with config properties), with an info icon after the label, saving on change.</summary>
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
