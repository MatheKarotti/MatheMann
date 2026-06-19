using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MatheMann;

/// <summary>All plugin settings, grouped into Behaviour / Appearance / Numbers / History.</summary>
public sealed class SettingsWindow : Window, IDisposable
{
    private readonly SessionHistory config;

    private static readonly Vector4 Muted = new(0.55f, 0.55f, 0.55f, 1f);

    public SettingsWindow(SessionHistory config) : base("MatheMann Settings##MatheMannSettings")
    {
        this.config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 300),
            MaximumSize = new Vector2(600, 900),
        };
        Size          = new Vector2(400, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
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
        ImGui.TextUnformatted("Window scale");
        if (ImGui.SliderFloat("##scale", ref scale, 0.7f, 2.0f, "%.2fx"))
        {
            config.WindowScale = scale;
            config.Save();
        }
        Hint("Size of the text/UI in the MatheMann windows.");

        var opacity = config.WindowOpacity;
        ImGui.TextUnformatted("Window opacity");
        if (ImGui.SliderFloat("##opacity", ref opacity, 0.3f, 1.0f, "%.2f"))
        {
            config.WindowOpacity = opacity;
            config.Save();
        }
        Hint("Background transparency of the MatheMann windows (0.30 = very see-through).");

        Divider();

        // ── Numbers ──────────────────────────────────────────────────────────────
        Section("Numbers");

        ImGui.TextUnformatted("Display number format");
        var displayIndex = (int)config.DisplayFormat;
        if (ImGui.Combo("##displayFormat", ref displayIndex, GilFormatter.Labels, GilFormatter.Labels.Length))
        {
            config.DisplayFormat = (GilFormat)displayIndex;
            config.Save();
        }
        Hint("How gil numbers are shown in the tables and totals.");

        ImGui.TextUnformatted("Copy number format");
        var copyIndex = (int)config.CopyFormat;
        if (ImGui.Combo("##copyFormat", ref copyIndex, GilFormatter.Labels, GilFormatter.Labels.Length))
        {
            config.CopyFormat = (GilFormat)copyIndex;
            config.Save();
        }
        Hint("How the Copy buttons format numbers on the clipboard.");

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

        var maxSessions = config.MaxSessions;
        ImGui.TextUnformatted("Sessions to keep");
        if (ImGui.SliderInt("##maxSessions", ref maxSessions, 1, 100))
        {
            config.MaxSessions = maxSessions;
            config.Trim();
            config.Save();
        }
        Hint("Older sessions beyond this count are dropped automatically.");
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

    private static void Hint(string text)
    {
        // Wrap to the right edge of the content area so long hints flow onto multiple
        // lines instead of running off the window. PushTextWrapPos(0) wraps at the
        // current window's content width.
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    /// <summary>A checkbox bound through a getter value + setter callback (so it works
    /// with config properties), with a muted hint below, saving on change.</summary>
    private void Toggle(string label, bool current, Action<bool> set, string hint)
    {
        var value = current;
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            config.Save();
        }
        Hint(hint);
    }

    public void Dispose() { }
}
