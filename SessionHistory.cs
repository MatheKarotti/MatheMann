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
    /// Show the grand-total footer (broken down by character) at the bottom of the
    /// history window. On by default. Clearing history is the only way to reset
    /// the total now — there's no separate reset button.
    /// </summary>
    public bool ShowGrandTotal { get; set; } = true;

    public int Version { get; set; }

    /// <summary>How many sessions to keep before the oldest drops off.</summary>
    private const int MaxSessions = 10;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    /// <summary>Add a finished session to the front and trim to the cap.</summary>
    public void Add(SavedSession session)
    {
        Sessions.Insert(0, session);
        if (Sessions.Count > MaxSessions)
            Sessions.RemoveRange(MaxSessions, Sessions.Count - MaxSessions);
        Save();
    }

    public void Clear()
    {
        Sessions.Clear();
        Save();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
