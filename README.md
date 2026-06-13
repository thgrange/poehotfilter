# PoeHotFilter

A lightweight Windows overlay for **Path of Exile (PoE 1)**, built with SSF in mind. Highlight any
item in your loot filter **on the fly** — no FilterBlade round-trip, no game restart.

Hover an item in game, press **Ctrl + B**, pick a colour / icon / item-level rule in the popup, and
the app writes a `Show` block into a managed filter file and reloads your filter **live** — it even
re-styles drops already on the ground.

> Not affiliated with or endorsed by Grinding Gear Games. *Path of Exile* is a trademark of Grinding
> Gear Games. This is a free, fan-made tool.

---

## Install

1. Download `PoeHotFilter-vX.Y.Z-win-x64.zip` from the [Releases](../../releases) page.
2. Unzip it anywhere (keep `PoeHotFilter.exe` next to its `wwwroot` folder).
3. Double-click **`PoeHotFilter.exe`**. It runs in the system tray — no installation, nothing to
   configure.

**Requirements**
- Windows 10 / 11 (x64).
- The WebView2 runtime — preinstalled on virtually every Windows 10/11. If the window is blank,
  install the [Evergreen WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).
- Run PoE in **windowed** or **borderless** (not exclusive fullscreen) — standard for any overlay.
- The .NET runtime is **bundled** (self-contained build) — you don't need to install anything else.

You can enable **Start with Windows** from the tray icon's right-click menu.

---

## Usage

- **Ctrl + B on a hovered item** — opens the popup pre-filled with that item (base, class, detected
  item level, current look under your filter). Choose rarity / ilvl / quality / colours / minimap
  icon / drop sound, then **Add & reload**.
- **Ctrl + B on empty ground** — opens the same popup with a **searchable picker**: type to find any
  droppable base (currency, gems, maps, cluster jewels, uniques' bases, …) and filter it without
  having one in hand.
- **Ctrl + S** — opens the **Custom Filters** list to review, edit, or delete the rules you've added.

**Cluster jewels** get an extra section: pick the enchant (the small-passive node name, e.g.
*Aura Effect*) and the number of added passives — emitted as `EnchantmentPassiveNode` /
`EnchantmentPassiveNum`.

---

## How it works

PoE exposes no API for "what am I hovering" or "what filter is active", so the app chains together
the things that *do* work:

1. **Read** — simulating Ctrl + C makes PoE dump the hovered item (item level included) to the
   clipboard; the app parses it.
2. **Write** — your choices become a `Show` block in a separate **`_PoeHotFilter.filter`** file. A
   single `Import "_PoeHotFilter.filter"` line is injected **once at the top** of your active filter,
   so your highlights win over NeverSink's blocks for the same base. Your filter's own rules are
   never edited, and a **`.phf-backup`** is made before that one-time injection.
3. **Apply** — the app sends `/reloaditemfilter` to PoE's chat, reloading the active filter in place.

The **source of truth** is `%APPDATA%\PoeHotFilter\rules.json`; the managed `.filter` is regenerated
from it on every change. The app follows whichever filter you select in game (read from
`production_Config.ini`) and re-asks consent if a FilterBlade re-export wipes the import line.

---

## Build from source

Requires the **.NET 8 SDK** on Windows.

```powershell
dotnet build PoeHotFilter.sln
dotnet run --project src/PoeHotFilter.Photino
```

Project layout:

```
src/PoeHotFilter.Core       net8.0          UI-agnostic logic (parsing, filter generation, config, orchestration)
src/PoeHotFilter.Photino    net8.0-windows  frontend: native WebView2 window hosting the wwwroot web UI + Win32 glue
src/PoeHotFilter.Core.Tests                 xUnit tests for the Core
```

To produce the release artifact (single self-contained, compressed exe + `wwwroot`):

```powershell
dotnet publish src/PoeHotFilter.Photino -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

`playground.html` is a standalone prototype of the popup (open it in any browser) — same design as the
real UI, fully self-contained.

---

## Credits & licensing

- **Minimap icons** are Grinding Gear Games' in-game sprites, used here as game assets.
- **Fontin** font by Jos Buivenga ([exljbris](https://www.exljbris.com)) — free EULA permits program
  embedding; attribution kept in the CSS.
- Item base-type data derived from **RePoE** (extracted game data).
