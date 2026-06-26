using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace MatheMann;

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

[Serializable]
public sealed class SavedItem
{
    public string Name     { get; set; } = "";
    public uint   Quantity { get; set; }
    public uint   Price    { get; set; }
}

// Persisted config + the last N sessions, stored via Dalamud's plugin config.
[Serializable]
public sealed class SessionHistory : IPluginConfiguration
{
    public List<SavedSession> Sessions { get; set; } = new();   // newest first

    public bool GlueToGameWindows { get; set; }
    public bool PlaySound { get; set; }
    public bool GroupItems { get; set; } = true;

    // Display = on-screen, Copy = clipboard. Kept separate because Sheets parses by
    // locale, so the wrong separator on paste breaks totals by 1000x.
    public GilFormat CopyFormat    { get; set; } = GilFormat.German;
    public GilFormat DisplayFormat { get; set; } = GilFormat.German;

    public bool ShowGrandTotal { get; set; } = true;

    // Only applies when history was opened from the in-shop button; /mama history
    // always opens standalone.
    public bool HistoryClosesWithShop { get; set; } = true;

    public float WindowScale   { get; set; } = 1.0f;
    public float WindowOpacity { get; set; } = 1.0f;
    public bool  ShowRawTotal  { get; set; }
    public int   MaxSessions   { get; set; } = 10;

    // Extra "Unit Price" column showing per-item price (total / qty). On by default.
    public bool ShowUnitPrice { get; set; } = true;

    // How the main-window ledger is ordered. Defaults to highest line total first.
    public LedgerSort Sort { get; set; } = LedgerSort.TotalDesc;

    public int Version { get; set; }

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Add(SavedSession session)
    {
        Sessions.Insert(0, session);
        Trim();
        Save();
    }

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
