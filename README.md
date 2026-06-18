# MatheMann

> And i love... Mathematics

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **FFXIV** that adds up
the value of items you sell, so the total can be copied straight into a spreadsheet.
Works for both **NPC vendors** and **retainers**, and tracks running totals across a
selling session.

## Features

- Reads the vendor **Buyback** list and sums the prices of everything you've sold.
- Tracks retainer sales live (works in **English, German, French, and Japanese** clients).
- Keeps a small **history** of past selling sessions, grouped by character.
- One-click **Copy** button, with selectable number format (German / international / raw).
- Optional sound on opening, window gluing, item grouping, and a grand-total footer.

## Installation

1. In-game, open Dalamud settings by typing `/xlsettings` in chat.
2. Go to the **Experimental** tab.
3. Under **Custom Plugin Repositories**, paste this URL into the empty box:

   ```
   https://raw.githubusercontent.com/MatheKarotti/MatheMann/main/repo.json
   ```

4. Click the **+** button, then **Save and Close**.
5. Open the plugin installer with `/xlplugins`, find **MatheMann**, and click **Install**.

## Commands

| Command          | Action                              |
|------------------|-------------------------------------|
| `/mama`          | Toggle the main window              |
| `/mama history`  | Open the history window             |
| `/mama settings` | Open the settings window            |
