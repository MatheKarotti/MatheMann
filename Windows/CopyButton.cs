using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MatheMann;

// "Copy" button that shows "Copied!" (green) for 5s after clicking, then reverts.
// Also reverts early if changeToken changes (so a stale total doesn't keep saying
// Copied). Keyed by id so each button tracks its own state - used for the main total
// and each history row. Always copies on click, never disabled.
public static class CopyButton
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);
    private static readonly Vector4  Green    = new(0.25f, 0.62f, 0.30f, 1f);
    private static readonly Vector4  GreenHover = new(0.30f, 0.72f, 0.35f, 1f);

    private readonly struct State
    {
        public readonly DateTime At;
        public readonly string   Token;
        public State(DateTime at, string token) { At = at; Token = token; }
    }

    private static readonly Dictionary<string, State> states = new();

    // id doubles as the ImGui id. changeToken should change when the data does.
    public static void Draw(string id, string baseLabel, string textToCopy, string changeToken)
    {
        PruneExpired();

        var showCopied = states.TryGetValue(id, out var st)
                         && DateTime.Now - st.At < Duration
                         && st.Token == changeToken;

        if (!showCopied)
            states.Remove(id);   // expired or data changed — forget it

        if (showCopied)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Green);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, GreenHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, GreenHover);
        }

        var label = (showCopied ? "Copied!" : baseLabel) + "##" + id;
        if (ImGui.SmallButton(label))
        {
            ImGui.SetClipboardText(textToCopy);
            states[id] = new State(DateTime.Now, changeToken);
        }

        if (showCopied)
            ImGui.PopStyleColor(3);
    }

    // Drop timed-out entries so collapsed/removed buttons don't leave state behind.
    private static void PruneExpired()
    {
        if (states.Count == 0) return;

        var now = DateTime.Now;
        List<string>? dead = null;
        foreach (var kv in states)
        {
            if (now - kv.Value.At >= Duration)
                (dead ??= new List<string>()).Add(kv.Key);
        }

        if (dead is not null)
            foreach (var key in dead)
                states.Remove(key);
    }
}
