using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace MatheMann;

/// <summary>One finished selling session: when it ended, and what was in the ledger.</summary>
[Serializable]
public sealed class SavedSession
{
    public DateTime EndedAt   { get; set; }
    public uint     TotalGil  { get; set; }
    public uint     ItemCount { get; set; }
    public string   Character { get; set; } = "";
    public string   FreeCompany { get; set; } = "";
    public List<SavedItem> Items { get; set; } = new();
}

/// <summary>A single line within a saved session.</summary>
[Serializable]
public sealed class SavedItem
{
    public string Name     { get; set; } = "";
    public uint   Quantity { get; set; }
    public uint   Price    { get; set; }
}

/// <summary>
/// Persisted plugin state: the last N finished selling sessions. Stored through
/// Dalamud's plugin-config mechanism so it survives game restarts.
/// </summary>
[Serializable]
public sealed class SessionHistory : IPluginConfiguration
{
    /// <summary>Most-recent session first.</summary>
    public List<SavedSession> Sessions { get; set; } = new();

    /// <summary>Glue the MatheMann windows to the relevant in-game window. Off by default.</summary>
    public bool GlueToGameWindows { get; set; }

    /// <summary>Play the open.mp3 sound when the buyback view opens. Off by default.</summary>
    public bool PlaySound { get; set; }

    /// <summary>Group identical items into one row. On by default.</summary>
    public bool GroupItems { get; set; } = true;

    /// <summary>
    /// How the Copy buttons format numbers on the clipboard. Defaults to German
    /// (dot thousands separator) to match the on-screen display.
    /// </summary>
    public GilFormat CopyFormat { get; set; } = GilFormat.German;

    /// <summary>
    /// How gil numbers are formatted in the on-screen tables and totals (separate
    /// from <see cref="CopyFormat"/>, which only affects clipboard text). Defaults
    /// to German to match the plugin's original behaviour.
    /// </summary>
    public GilFormat DisplayFormat { get; set; } = GilFormat.German;

    /// <summary>
    /// Show the grand-total footer (broken down by character) at the bottom of the
    /// history window. On by default. Clearing history is the only way to reset
    /// the total now — there's no separate reset button.
    /// </summary>
    public bool ShowGrandTotal { get; set; } = true;

    /// <summary>
    /// When the history window is opened from the in-shop "History" button, close it
    /// again automatically when the shop/main window closes. On by default — turn it
    /// off to keep the history window pinned open across shop visits. (The
    /// "/mama history" command always opens it standalone regardless of this.)
    /// </summary>
    public bool HistoryClosesWithShop { get; set; } = true;

    /// <summary>UI scale for the MatheMann windows (1.0 = normal). 0.7–2.0.</summary>
    public float WindowScale { get; set; } = 1.0f;

    /// <summary>Window background opacity (1.0 = opaque). 0.3–1.0.</summary>
    public float WindowOpacity { get; set; } = 1.0f;

    /// <summary>
    /// Show the raw, unformatted gil value in parentheses next to the main total
    /// (e.g. "6.890 (6890)"). Useful for debugging; off by default for a cleaner look.
    /// </summary>
    public bool ShowRawTotal { get; set; }

    /// <summary>How many finished sessions to keep before the oldest drops off.</summary>
    public int MaxSessions { get; set; } = 10;

    public int Version { get; set; }

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    /// <summary>Add a finished session to the front and trim to the cap.</summary>
    public void Add(SavedSession session)
    {
        Sessions.Insert(0, session);
        Trim();
        Save();
    }

    /// <summary>Drop sessions beyond the configured cap (oldest first).</summary>
    public void Trim()
    {
        var cap = Math.Max(1, MaxSessions);
        if (Sessions.Count > cap)
            Sessions.RemoveRange(cap, Sessions.Count - cap);
    }

    public void Clear()
    {
        Sessions.Clear();
        Save();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
