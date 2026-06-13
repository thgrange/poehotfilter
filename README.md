# PoeHotFilter

A lightweight Windows overlay for **Path of Exile (PoE 1)**, built with SSF in mind. Highlight any
item in your loot filter **on the fly** — no FilterBlade round-trip, no game restart.

Hover an item in game, press **Ctrl + B**, pick a colour / icon / item-level rule in the popup, and
the app writes a `Show` block into a managed filter file and reloads your filter **live** — it even
re-styles drops already on the ground.

> Not affiliated with or endorsed by Grinding Gear Games. *Path of Exile* is a trademark of Grinding
> Gear Games. This is a free, fan-made tool.

---

## ⚠️ You need an active loot filter

**PoeHotFilter only works while a loot filter is selected and active in Path of Exile.** Your
highlights are layered on top of whatever filter the game has loaded — if no filter is active, there
is nothing to highlight into and the rules won't show.

Make sure a filter is enabled in game: **Esc → Options → Game → Item Filter → enable it and pick a
filter** (e.g. your NeverSink filter).

If you have no filter at all, PoeHotFilter creates one named **`_PoeHotFilter`** in your filter folder
on first run — just select that one in the same menu and you're set. (If you press the hotkey with no
active filter, the app reminds you of this.)

---

## Install

1. Download `PoeHotFilter-vX.Y.Z-win-x64.zip` from the [Releases](../../releases) page.
2. Unzip it anywhere (keep `PoeHotFilter.exe` next to its `wwwroot` folder).
3. Double-click **`PoeHotFilter.exe`**. It runs in the system tray — no installation, nothing to set up.

**Requirements**
- Windows 10 / 11 (x64). The .NET runtime is **bundled** — nothing else to install.
- The WebView2 runtime (preinstalled on virtually every Windows 10/11). If the popup is blank,
  install the [Evergreen WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).
- **An active item filter in PoE** (see above).
- Run PoE in **windowed** or **borderless** (not exclusive fullscreen) — standard for any overlay.

You can enable **Start with Windows** from the tray icon's right-click menu.

---

## Usage

The app sits in the tray and reacts to global hotkeys **while Path of Exile is focused**:

| Hotkey | When | What it does |
| --- | --- | --- |
| **Ctrl + B** | hovering an item | Opens the popup pre-filled with that item (base, class, detected item level, and its current look under your filter). |
| **Ctrl + B** | pointing at nothing | Opens the popup with a **searchable picker** — type to find any droppable base and tag it without having one in hand. |
| **Ctrl + S** | anytime | Opens the **Custom Filters** list to review, edit, or delete the rules you've added. |

### Add a highlight

1. Make sure your filter is active in PoE (see the warning above).
2. Hover the item you want and press **Ctrl + B** (or press it on empty ground and search for the base).
3. In the popup, choose what to match — rarity, item level (Any / ≥ N / exactly N), quality, stack
   size, corrupted, etc. — and the look: text / border / background colours (with opacity), font size,
   a minimap icon, and a drop sound.
4. Pick a **preset** to match a style your filter already uses, or set colours by hand. The
   **in-game preview** shows the result live.
5. Hit **Add & reload**. The popup closes, PoE regains focus, and your filter reloads — the item lights
   up immediately, including copies already on the ground.

The **first time** the app needs to touch your active filter it asks for confirmation: it adds a single
`Import` line at the very top (your filter's own rules are untouched) and makes a `.phf-backup` first.

### Cluster jewels

When the item is a Small / Medium / Large Cluster Jewel, the popup shows an extra **Cluster Jewel**
section: pick the enchant (the small-passive node name, e.g. *Aura Effect*, searchable) and the number
of added passives. These become `EnchantmentPassiveNode` / `EnchantmentPassiveNum` conditions.

### Manage your rules

Press **Ctrl + S** to see every rule you've added, with a mini preview of its in-game look. Click one to
edit it, or use the **×** to remove it. Changes regenerate the filter and reload on close.

---

## How it works

PoE exposes no API for "what am I hovering" or "what filter is active", so the app chains the things
that *do* work:

1. **Read** — simulating Ctrl + C makes PoE dump the hovered item (item level included) to the
   clipboard; the app parses it.
2. **Write** — your choices become a `Show` block in a separate **`_PoeHotFilter.filter`** file,
   imported once at the **top** of your active filter so your highlights win over NeverSink's blocks
   for the same base. Your filter is never edited beyond that one import line, and a **`.phf-backup`**
   is made first.
3. **Apply** — the app sends `/reloaditemfilter` to PoE's chat, reloading the active filter in place.

The **source of truth** is `%APPDATA%\PoeHotFilter\rules.json`; the managed `.filter` is regenerated
from it on every change. The app follows whichever filter you select in game and re-asks consent if a
FilterBlade re-export wipes the import line.

---

## Build from source

Requires the **.NET 8 SDK** on Windows.

```powershell
dotnet build PoeHotFilter.sln
dotnet run --project src/PoeHotFilter.Photino
```

```
src/PoeHotFilter.Core       net8.0          UI-agnostic logic (parsing, filter generation, config, orchestration)
src/PoeHotFilter.Photino    net8.0-windows  frontend: native WebView2 window hosting the wwwroot web UI + Win32 glue
src/PoeHotFilter.Core.Tests                 xUnit tests for the Core
```

`playground.html` is a standalone prototype of the popup (open it in any browser) — same design as the
real UI, fully self-contained.

---

## Credits & licensing

- **Minimap icons** are Grinding Gear Games' in-game sprites, used here as game assets.
- **Fontin** font by Jos Buivenga ([exljbris](https://www.exljbris.com)) — free EULA permits program
  embedding; attribution kept in the CSS.
- Item base-type data derived from **RePoE** (extracted game data).
