using Dalamud.Bindings.ImGui;

namespace MatheMann;

/// <summary>
/// A shared set of ImGui style tweaks that give all MatheMann windows the same
/// soft, rounded look (windows, buttons, child panels, input fields, scrollbars,
/// and the collapsing-header bars in the history). Push() in a window's PreDraw and
/// Pop() in its PostDraw, always paired, so the style doesn't leak into the game's
/// other ImGui windows.
///
/// Stateless on purpose: Push always pushes exactly VarCount style vars and Pop
/// always pops that many, so there's no shared counter that could get out of sync
/// when multiple MatheMann windows draw in the same frame.
/// </summary>
public static class WindowStyle
{
    private const float Rounding = 6f;
    private const int   VarCount = 7;

    public static void Push()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, Rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, Rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, Rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, Rounding);
    }

    public static void Pop() => ImGui.PopStyleVar(VarCount);
}
