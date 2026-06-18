using System;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace MatheMann;

/// <summary>
/// MatheMann — sums the prices of items shown in an FFXIV vendor's shop window
/// so the total can be copied straight into a spreadsheet. The window opens
/// automatically on the Buyback tab and hides when the shop closes.
///
/// Each completed selling session (ended when the player changes zones, which is
/// when the game wipes the Buyback list) is saved to a small history, viewable
/// from the History button.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle         AddonLifecycle  { get; private set; } = null!;
    [PluginService] internal static IGameGui                GameGui         { get; private set; } = null!;
    [PluginService] internal static IDataManager            DataManager     { get; private set; } = null!;
    [PluginService] internal static IClientState            ClientState     { get; private set; } = null!;
    [PluginService] internal static IObjectTable            ObjectTable     { get; private set; } = null!;
    [PluginService] internal static ICommandManager         CommandManager  { get; private set; } = null!;
    [PluginService] internal static IChatGui                ChatGui         { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;

    private const string MainCommand     = "/mama";
    private const string HistoryCommand  = "/mama history";
    private const string SettingsCommand = "/mama settings";

    private readonly WindowSystem   windowSystem = new("MatheMann");
    private readonly SessionHistory history;
    private readonly ShopReader     shopReader;
    private readonly MainWindow     mainWindow;
    private readonly HistoryWindow  historyWindow;
    private readonly SettingsWindow settingsWindow;

    public Plugin()
    {
        history = PluginInterface.GetPluginConfig() as SessionHistory ?? new SessionHistory();
        history.Initialize(PluginInterface);

        shopReader     = new ShopReader();
        historyWindow  = new HistoryWindow(history);
        settingsWindow = new SettingsWindow(history);
        mainWindow     = new MainWindow(shopReader, historyWindow, history);
        mainWindow.ToggleSettings = () => settingsWindow.Toggle();

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(historyWindow);
        windowSystem.AddWindow(settingsWindow);

        // Auto show/hide the main window with the Buyback tab.
        shopReader.ShopOpened += () => mainWindow.IsOpen = true;
        shopReader.SessionOpened += () =>
        {
            if (history.PlaySound) SoundPlayer.Play();
        };
        shopReader.ShopClosed += () =>
        {
            mainWindow.IsOpen = false;
            historyWindow.OnShopClosed();
        };

        // Retainer sales are read from chat (the retainer sell window has no buyback
        // view), letting the total build live while selling. NPC shop stays on the
        // window reader.
        ChatGui.ChatMessage += OnChatMessage;

        // A selling session ends when the player changes zones (the game clears the
        // Buyback list then). Save whatever was in the ledger to the history.
        ClientState.TerritoryChanged += OnTerritoryChanged;

        // A retainer selling session ends when the player leaves the retainer.
        // Save it and close the windows.
        shopReader.RetainerSessionEnded += OnRetainerSessionEnded;

        CommandManager.AddHandler(MainCommand, new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Toggle the main MatheMann window.",
            ShowInHelp  = true,
        });

        CommandManager.AddHandler(HistoryCommand, new CommandInfo(OnHistoryCommand)
        {
            HelpMessage = "Open the MatheMann history window.",
            ShowInHelp  = true,
        });

        CommandManager.AddHandler(SettingsCommand, new CommandInfo(OnSettingsCommand)
        {
            HelpMessage = "Open the MatheMann settings window.",
            ShowInHelp  = true,
        });

        PluginInterface.UiBuilder.Draw         += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
    }

    /// <summary>
    /// Handles "/mama". A bare "/mama" toggles the main window. "/mama history" and
    /// "/mama settings" are registered as their own commands (so they each show as a
    /// separate line in Dalamud's help list), but Dalamud routes the bare form here
    /// with the rest as args — so we still parse the subcommand here as a fallback,
    /// which also lets abbreviations like "/mama hist" / "/mama set" work.
    /// </summary>
    private void OnMainCommand(string command, string args)
    {
        var word = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts
            ? parts[0].ToLowerInvariant()
            : "";

        if (word.Length == 0)
            mainWindow.Toggle();
        else if ("history".StartsWith(word))
            historyWindow.ToggleStandalone();
        else if ("settings".StartsWith(word))
            settingsWindow.Toggle();
        else
            Plugin.ChatGui.Print($"[MatheMann] Unknown command \"{word}\". Use \"/mama\", \"/mama history\", or \"/mama settings\".");
    }

    private void OnHistoryCommand(string command, string args)  => historyWindow.ToggleStandalone();
    private void OnSettingsCommand(string command, string args) => settingsWindow.Toggle();
    private void OpenSettings() => settingsWindow.IsOpen = true;

    private void OnChatMessage(IHandleableChatMessage chat)
    {
        // Only retainer/vendor sale notices, which arrive as system messages.
        if (chat.LogKind != XivChatType.SystemMessage) return;

        // Parse the quantity and price out of the sale line in whatever language the
        // client is running. Returns null for anything that isn't a sell notice
        // (buybacks use a different verb and won't match), so those are ignored.
        var sale = SellMessageParser.TryParse(chat.Message.TextValue, Plugin.ClientState.ClientLanguage);
        if (sale is null) return;

        // The item itself comes from a structured payload (language-independent).
        uint itemId = 0;
        bool isHq   = false;
        foreach (var payload in chat.Message.Payloads)
        {
            if (payload is ItemPayload ip)
            {
                itemId = ip.ItemId;
                isHq   = ip.IsHQ;
                break;
            }
        }
        if (itemId == 0) return;

        var name = ResolveItemName(itemId, isHq);
        shopReader.AddChatSale(name, sale.Value.Quantity, sale.Value.Price);
    }

    /// <summary>Resolve an item id to its display name via Lumina, adding the HQ mark.</summary>
    private static string ResolveItemName(uint itemId, bool isHq)
    {
        var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (sheet is not null && sheet.TryGetRow(itemId, out var row))
        {
            var name = row.Name.ExtractText();
            return isHq ? name + " \uE03C" : name;   // HQ symbol
        }
        return $"Item #{itemId}";
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        SaveSession();
    }

    private void OnRetainerSessionEnded()
    {
        SaveSession();

        // Close the windows now that the retainer flow is over.
        mainWindow.IsOpen = false;
        historyWindow.OnShopClosed();
    }

    /// <summary>End the current session and store it in the history. The
    /// character + FC are already stamped by ShopReader (captured when the first
    /// item was added, while the player object was guaranteed valid) — don't
    /// re-stamp here, since ObjectTable.LocalPlayer can briefly be null right at
    /// zone-change time, which is exactly when this is called.</summary>
    private void SaveSession()
    {
        var session = shopReader.EndSession();
        if (session is null) return;

        history.Add(session);
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;
        shopReader.RetainerSessionEnded -= OnRetainerSessionEnded;
        CommandManager.RemoveHandler(MainCommand);
        CommandManager.RemoveHandler(HistoryCommand);
        CommandManager.RemoveHandler(SettingsCommand);
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        PluginInterface.UiBuilder.Draw         -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        historyWindow.Dispose();
        settingsWindow.Dispose();
        shopReader.Dispose();
    }
}
