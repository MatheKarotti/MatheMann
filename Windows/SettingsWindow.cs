using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MatheMann;

/// <summary>Toggles: glue windows, play sound, and group identical items.</summary>
public sealed class SettingsWindow : Window, IDisposable
{
    private readonly SessionHistory config;

    private static readonly Vector4 Muted = new(0.55f, 0.55f, 0.55f, 1f);

    public SettingsWindow(SessionHistory config) : base("MatheMann Settings##MatheMannSettings")
    {
        this.config = config;
        Size          = new Vector2(380, 300);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var sound = config.PlaySound;
        if (ImGui.Checkbox("Play sound when opening", ref sound))
        {
            config.PlaySound = sound;
            config.Save();
        }
        ImGui.TextColored(Muted, "Plays the MatheMann intro when the main window opens.");

        ImGui.Spacing();

        var glue = config.GlueToGameWindows;
        if (ImGui.Checkbox("Glue windows to the game UI", ref glue))
        {
            config.GlueToGameWindows = glue;
            config.Save();
        }
        ImGui.TextColored(Muted, "Anchors the MatheMann windows to the shop/retainer window.");

        ImGui.Spacing();

        var group = config.GroupItems;
        if (ImGui.Checkbox("Group Items", ref group))
        {
            config.GroupItems = group;
            config.Save();
        }
        ImGui.TextColored(Muted, "Groups together multiple instances of the same items (for example Minions).");

        ImGui.Spacing();

        var showGrandTotal = config.ShowGrandTotal;
        if (ImGui.Checkbox("Show grand total in history", ref showGrandTotal))
        {
            config.ShowGrandTotal = showGrandTotal;
            config.Save();
        }
        ImGui.TextColored(Muted, "Shows the total-across-all-sessions footer at the bottom of the history window.");

        ImGui.Spacing();

        ImGui.TextUnformatted("Copy number format");
        var formatIndex = (int)config.CopyFormat;
        if (ImGui.Combo("##copyFormat", ref formatIndex, GilFormatter.Labels, GilFormatter.Labels.Length))
        {
            config.CopyFormat = (GilFormat)formatIndex;
            config.Save();
        }
        ImGui.TextColored(Muted, "Controls how the Copy buttons format numbers on the clipboard.");
    }

    public void Dispose() { }
}
