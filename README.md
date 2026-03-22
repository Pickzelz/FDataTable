# FDataTable

**FDataTable** is a Unity Editor tool that displays your `ScriptableObject` assets as an interactive spreadsheet. Instead of opening each asset in the Inspector one by one, you can view, edit, add, and delete all your data in a single table window — like a lightweight in-Editor database.

> Designed for game designers and developers who manage large amounts of data-driven assets (items, enemies, skills, levels, etc.).

---

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Interface Overview](#interface-overview)
  - [Toolbar](#toolbar)
  - [Tab Bar](#tab-bar)
  - [Per-Tab Settings Bar](#per-tab-settings-bar)
  - [Table](#table)
- [Filtering](#filtering)
  - [Global Filter](#global-filter)
  - [Per-Column Filters](#per-column-filters)
- [Editing Cells](#editing-cells)
- [Complex Fields — Arrays & Structs](#complex-fields--arrays--structs)
- [Managing Columns — Fields… Popup](#managing-columns--fields-popup)
- [Global Settings](#global-settings)
- [Row Order](#row-order)
- [Attributes](#attributes)
- [Supported Field Types](#supported-field-types)
- [File Structure](#file-structure)
- [FAQ](#faq)

---

## Features

| Feature | Description |
|---|---|
| **Multi-tab** | Open multiple ScriptableObject types at once, each in its own tab |
| **Inline editing** | Edit all fields directly inside the table — no Inspector needed |
| **Inline arrays & structs** | `[Serializable]` arrays and structs render as expandable mini sub-tables inside their cell |
| **Auto-expanding columns** | Columns automatically widen to fit their content so nothing is ever clipped |
| **Column resizing** | Drag any column divider to adjust its width manually |
| **Global filter** | Search across all columns at once with a toolbar search field (Ctrl+F) |
| **Per-column filters** | Filter any individual column independently via the filter row below the header |
| **Field visibility control** | Choose which fields appear as columns via the **Fields…** button, per type |
| **Persistent row order** | The order you arrange your rows in is preserved across recompiles and Editor restarts |
| **Add & delete rows** | Add new assets from the ghost row at the bottom; delete via right-click |
| **Rename in place** | Edit the **Name** column to rename the `.asset` file on disk instantly |
| **Per-tab folder & prefix** | Each tab remembers its own save folder and filename prefix independently |

---

## Requirements

- Unity **2021.3 LTS** or newer
- No external dependencies

---

## Installation

1. Import the package from the Unity Asset Store, **or** copy the `FDataTable/` folder into your project's `Assets/` directory.
2. Unity will automatically compile both assembly definitions — no manual setup required.

| Assembly | Role |
|---|---|
| `FDataTable.Editor` | All Editor UI, scanning, and settings (Editor-only) |
| `FDataTable.Runtime` | The `[FDataTableIgnore]` attribute (included in builds) |

---

## Quick Start

1. **Open the window** — go to `Window → FDataTable → Data Table`.
2. **Select a type** — click the **+** button in the tab bar, search for your `ScriptableObject` type, and click it to open a new tab.
3. **Add a row** — click the ghost row at the bottom of the table that reads *“Click to add new…”*. A new `.asset` file is created immediately at the configured folder path.
4. **Edit cells** — click any cell to start editing. Changes are saved to disk automatically.
5. **Rename a row** — edit the **Name** column to rename the asset file on disk.
6. **Delete a row** — right-click any row and choose **Delete**.
7. **Filter rows** — press **Ctrl+F** to jump to the global search field, or type directly into any column’s filter box in the row below the header.

---

## Interface Overview

### Toolbar

```
[ ↺ Refresh ]   [ 🔍 global search…  3/10 ]           [ ⚙ Settings ]
```

| Control | Action |
|---|---|
| **↺ Refresh** | Rescan the asset folder and reload the currently active tab. Use this if you added or moved files outside of the window. |
| **Global search field** | Search across all visible columns at once. Press **Ctrl+F** to focus. Shows a `N/M` match count when active. |
| **⚙ Settings** | Open the Global Settings window to configure the default save folder and filename prefix. |

---

### Tab Bar

Each open type gets its own tab at the top of the window.

- **Switch** between types by clicking their tab.
- **Close** a tab with the `×` button on the right side of the tab label.
- Click the **+** button at the end of the tab bar to open a new type. A searchable picker popup appears.
- You can have one tab open per ScriptableObject type at a time.

---

### Per-Tab Settings Bar

The settings bar appears directly below the tab bar and applies to the currently active tab:

```
[ 📁 ]  [ Assets/Data/Plants          ]   Prefix: [ Plant ]   [ Fields… ]
```

| Control | Action |
|---|---|
| **📁 (folder icon)** | Opens a folder browser to pick the folder where new assets will be saved |
| **Folder path field** | Editable path. You can type a folder path here directly |
| **Prefix field** | The filename prefix for new assets. For example, `Plant` produces `Plant 1.asset`, `Plant 2.asset`, etc. |
| **Fields…** | Opens the [Field Visibility popup](#managing-columns--fields-popup) to show or hide columns for this type |

These settings are saved per-tab and restored automatically the next time you open the same type.

---

### Table

#### Header Row
- Shows column labels: **#** (row number), **Name** (asset filename), and one column per visible field.
- **Drag** the right edge of any column header to resize it.

#### Filter Row
- A compact row directly below the header, one text field per column.
- Type in any field to filter that column. Multiple column filters are combined (AND logic).
- The **#** cell becomes a **✕** button when any column filter is active — click it to clear all column filters at once.

#### Data Rows
- Each row corresponds to one `.asset` file on disk.
- Click a row to **select** it (highlighted in blue).
- **Right-click** a row to open a context menu with the **Delete** option.

#### Ghost Row
- A special row at the very bottom labelled *"Click to add new…"*.
- Clicking it creates a new `.asset` file in the configured folder with the configured prefix.
- The new row is always appended at the bottom and its position is remembered.

---

## Filtering

FDataTable has two independent filter levels that can be used together.

### Global Filter

Located in the toolbar. Searches across **all visible columns and the Name column simultaneously**.

- Press **Ctrl+F** anywhere in the window to jump to it instantly.
- A `N/M` counter (e.g. `3/10`) appears next to the field showing how many rows match.
- Clear the field to show all rows again.

### Per-Column Filters

A thin filter row sits directly below the column headers, inside the scroll view.

```
┌────┬───────────────┬────────────┬────────────┐
│ #  │  Name (file)  │   Damage    │    Rarity   │   ← header row
├────┼───────────────┼────────────┼────────────┤
│    │ filter…       │ filter…    │ filter…    │   ← filter row
├────┼───────────────┼────────────┼────────────┤
│ 1  │ Sword         │  25        │  Rare      │
│ 2  │ Shield        │  0         │  Common    │
└────┴───────────────┴────────────┴────────────┘
```

- Each field supports **case-insensitive substring matching**.
- Filters on different columns are combined with **AND** logic — a row must match all active column filters.
- When any column filter is active, the **#** cell turns into a **✕** button to clear them all at once.
- Column filters and the global filter work **together** — a row must pass both.

> **Filters are not saved** between Editor sessions — they reset each time you open the window.

---

## Editing Cells

Click any cell to edit it. Changes are applied using the standard Unity `SerializedObject` API and are immediately reflected on disk (with undo support).

| Field type | How it looks in the table |
|---|---|
| Numbers (`int`, `float`, …) | Standard number input field |
| `bool` | Checkbox |
| `string` | Text input field |
| `enum` | Dropdown |
| `Color` | Color picker swatch |
| `Vector2`, `Vector3`, … | Compact X/Y/Z input fields |
| `AnimationCurve` | Mini curve preview + click to edit |
| `Gradient` | Gradient swatch + click to edit |
| **Object reference** | Drag-and-drop object field, filtered to the correct declared type |
| **Array / List** | Inline expandable sub-table (see below) |
| **Struct / Class** | Inline stacked label:value pairs (see below) |

The **Name** column holds the asset's filename (without extension). Editing it renames the `.asset` file on disk immediately.

---

## Complex Fields — Arrays & Structs

`[Serializable]` arrays, lists, and structs are displayed **directly inside the table cell** — no popup or separate window.

### Arrays and Lists

An array or `List<T>` column appears as a mini sub-table with its own header and rows:

```
┌────────────────────────────────────────────┐
│  #  │  Stage Name  │  Duration  │  Reward  │
├────────────────────────────────────────────┤
│  0  │  seedling    │   5.0      │  [item]  │
│  1  │  sprout      │  10.0      │  [item]  │
│               [ + Add ]                    │
└────────────────────────────────────────────┘
```

- Each element is a row; each field of the element is a column.
- The column auto-expands if content is wider than the current width.
- Nested arrays inside elements render as their own darker mini sub-table.
- Click `−` on any row to remove that element. Click **+ Add** to append a new one.

### Structs and Classes

A struct or class field renders as stacked `Label : [control]` rows inside its cell, one row per serialized field.

---

## Managing Columns — Fields… Popup

Click **Fields…** in the per-tab settings bar to open the field visibility popup for the active type.

```
┌─────────────────────────────┐
│  Field            │  Show   │
├───────────────────┼─────────┤
│  All              │   ☑    │  ← master toggle
├───────────────────┼─────────┤
│  Plant Id         │   ☑    │
│  Plant Name       │   ☑    │
│  Growth Stages    │   ☑    │
│  Internal Flag    │   ☐    │  ← hidden
├─────────────────────────────┤
│         [ Apply ]           │
└─────────────────────────────┘
```

| Control | Behaviour |
|---|---|
| **All** checkbox | Check to show all fields; uncheck to hide all fields at once |
| Individual **Show** checkbox | Toggle a single field's visibility |
| **Apply** | Save changes and close. The table reloads immediately to reflect the new column layout. |
| *Click outside the popup* | Auto-saves the current state |

By default, all fields are visible. Hidden fields are not deleted — they still exist in the asset data, they just aren't shown as columns.

> **Tip:** Fields marked with `[FDataTableIgnore]` are never shown regardless of this popup's settings.

---

## Global Settings

Open with the **⚙ Settings** button in the toolbar.

| Setting | Description |
|---|---|
| **Default Folder** | The asset folder used when a new tab is opened for the first time (e.g. `Assets/Data`). Each tab can override this independently. |
| **Default Prefix** | The filename prefix used when a new tab is opened for the first time. Each tab can override this independently. |

Settings are stored in `Assets/FDataTable/Editor/Settings/FDataTableSettings.asset` and committed with your project.

---

## Row Order

FDataTable remembers the order of your rows across sessions.

- When you **add** a row, it is always placed at the bottom.
- When you **delete** a row, the remaining rows keep their relative order.
- The order survives recompiles, domain reloads, and Editor restarts.
- Pressing **↺ Refresh** respects the saved order — newly discovered assets (e.g. added from outside the window) are appended at the end.

Row order is stored in `EditorPrefs` and is local to your machine (not committed to version control).

---

## Attributes

### `[FDataTableIgnore]`

Place this attribute on any serialized field to permanently exclude it from the table. The field will never appear as a column, regardless of the Fields… popup settings.

**Namespace:** `FDataTable.Runtime`

```csharp
using FDataTable.Runtime;
using UnityEngine;

[CreateAssetMenu]
public class ItemData : ScriptableObject
{
    public string itemName;
    public int damage;

    [FDataTableIgnore]
    public string internalDebugNote; // hidden in FDataTable, still serialized normally
}
```

---

## Supported Field Types

| Category | Supported Types |
|---|---|
| **Integer types** | `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `char` |
| **Floating point** | `float`, `double` |
| **Boolean** | `bool` |
| **String** | `string` |
| **Enums** | Any `enum` |
| **Unity structs** | `Vector2`, `Vector3`, `Vector4`, `Vector2Int`, `Vector3Int`, `Quaternion`, `Color`, `Color32`, `Rect`, `RectInt`, `Bounds`, `BoundsInt`, `LayerMask` |
| **Curves & gradients** | `AnimationCurve`, `Gradient` |
| **Object references** | Any type derived from `UnityEngine.Object` (e.g. `Sprite`, `AudioClip`, custom ScriptableObjects) |
| **Arrays & Lists** | `T[]` and `List<T>` where `T` is any supported type — rendered as an inline sub-table |
| **Serializable structs/classes** | Any `[Serializable]` class or struct — rendered with stacked inline fields |

---

## File Structure

```
FDataTable/
├── Editor/
│   ├── FDataTable.Editor.asmdef
│   ├── Core/
│   │   ├── FDataTableAssetFactory.cs     ← Creates and deletes .asset files
│   │   ├── FDataTableFieldResolver.cs    ← Determines which fields become columns
│   │   └── FDataTableScanner.cs          ← Finds all ScriptableObject assets of a type
│   ├── Settings/
│   │   ├── FDataTableSettings.cs         ← Settings ScriptableObject (global + per-type config)
│   │   └── FDataTableSettings.asset      ← Auto-generated on first use
│   └── UI/
│       ├── FDataTableWindow.cs           ← Main table window
│       ├── FDataTableSettingsWindow.cs   ← ⚙ Global settings window
│       └── FDataTableFieldsPopup.cs      ← Fields… column visibility popup
└── Runtime/
    ├── FDataTable.Runtime.asmdef
    └── Core/
        └── Attributes/
            └── FDataTableIgnoreAttribute.cs  ← [FDataTableIgnore]
```

---

## FAQ

**Q: My ScriptableObject type doesn't appear in the type picker.**  
A: Make sure your class inherits from `ScriptableObject` and is in a compiled assembly. Try clicking **↺ Refresh** after any script changes.

**Q: I set a column filter but rows are still not appearing.**  
A: Check that the global search field (toolbar) is also clear. Both filters apply together — a row must pass all active filters.

**Q: I edited a file outside of FDataTable and now the row is missing.**  
A: Click **↺ Refresh** to rescan the folder. Newly discovered assets are appended at the end of the table.

**Q: Can I use FDataTable with types from a third-party package?**  
A: Yes, as long as the type inherits from `ScriptableObject` and Unity can serialize its fields. The `[FDataTableIgnore]` attribute requires a reference to `FDataTable.Runtime` but is otherwise optional.

**Q: Does FDataTable work with types that use `[CreateAssetMenu]`?**  
A: Yes. `[CreateAssetMenu]` is just a convenience for the Project window — FDataTable works on any `ScriptableObject` subclass regardless.

**Q: Will my data be safe if I close the window mid-edit?**  
A: Yes. FDataTable uses Unity's standard `SerializedObject` API, so every edit is immediately committed to the asset. There is no unsaved state.

**Q: The row order changed after a recompile.**  
A: Row order is stored per-machine in `EditorPrefs`. If it resets, click **↺ Refresh** — the saved order will be restored. If the issue persists, check that your `EditorPrefs` are not being cleared by another tool.

---

*Made with ❤️ for Unity game developers.*
