using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MatheMann;

/// <summary>
/// A small "Copy" button that briefly shows "Copied!" (tinted green) after being
/// clicked, then reverts. Used in both the main window (one button) and the history
/// window (one per session), so the confirmation state is keyed by a caller-supplied
/// string — each distinct key tracks its own timer independently.
///
/// Behaviour (matches the spec for the main Copy button):
///   • Click copies <c>textToCopy</c> to the clipboard and starts the "Copied!" state.
///   • "Copied!" lasts <see cref="Duration"/> (5s), then the label reverts to "Copy".
///   • The state also reverts immediately if <paramref name="changeToken"/> differs
///     from what it was when copied — callers pass a value that changes whenever the
///     underlying data changes (e.g. "total:count"), so a stale "Copied!" never lingers.
///   • The button always copies on click, including while showing "Copied!", and is
///     never disabled.
/// </summary>
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

    // Keyed by the caller's id so each button (main total, each history session)
    // remembers its own "Copied!" moment independently.
    private static readonly Dictionary<string, State> states = new();

    /// <param name="id">Stable unique key for this button (also used as the ImGui id).</param>
    /// <param name="baseLabel">The normal label, e.g. "Copy" or "Copy total".</param>
    /// <param name="textToCopy">What to put on the clipboard when clicked.</param>
    /// <param name="changeToken">A value that changes when the underlying data does;
    /// when it no longer matches the copied token, the "Copied!" state clears.</param>
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

    /// <summary>Drop entries whose 5s window has passed, so buttons that stopped
    /// being drawn (e.g. a collapsed history row) don't leave state behind. Cheap:
    /// the dictionary only ever holds a handful of recently-clicked buttons.</summary>
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
