using Dalamud.Bindings.ImGui;

namespace MatheMann;

// Shared rounding style for all MatheMann windows. Push() in PreDraw, Pop() in
// PostDraw. Stateless (always pushes/pops VarCount) so it can't desync when multiple
// windows draw in one frame.
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
