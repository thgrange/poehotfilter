# CLAUDE.md — HotFilter (interne : `PoeHotFilter`)

Contexte pour Claude Code. Lis ça en entier avant de toucher au code.

---

## 1. Ce que c'est

Outil desktop Windows pour **Path of Exile (PoE 1)**, pensé **SSF**. Objectif : pouvoir
**surligner un item à la volée** dans le loot filter, sans ouvrir FilterBlade ni relancer le jeu.

Boucle complète :

1. Le joueur survole un item en jeu et appuie sur un **hotkey** (défaut `Ctrl+A`).
2. L'app simule `Ctrl+C` → PoE copie l'item survolé (avec son `Item Level`) dans le presse-papier.
3. On **parse** le presse-papier (base type, classe, rareté, ilvl).
4. Un **popup overlay** s'ouvre : on choisit rareté cible, ilvl (Any / ≥ / Exact), couleurs
   (texte/bordure/fond + alpha), taille de police, et une **icône minimap** (forme/couleur/taille).
5. On valide → l'app **injecte un bloc `Show`** dans un fichier filtre géré, et envoie
   **`/reloaditemfilter`** → le filtre est rechargé **à chaud**, et s'applique **même aux drops
   déjà au sol**.

> Le nom retenu est **PoeHotFilter** (jeu de mots : *hot reload* du filtre + *hotkey*).
> Le renommage est **fait partout** : dossiers projet, `.sln`, namespaces, `AssemblyName`
> (`PoeHotFilter.exe`), titres de fenêtre/tray, `%APPDATA%/PoeHotFilter`, filtre géré
> `_PoeHotFilter.filter`, backup `.phf-backup`, clé de démarrage Windows `PoeHotFilter`.
> ⚠️ Seul le **dossier racine** sur le disque s'appelle encore `PoeLiveFilter` (non renommé
> pour ne pas casser les chemins ; le repo GitHub, lui, est `PoeHotFilter`).

### Contraintes PoE importantes
- **Aucune API** PoE pour l'item survolé ou le filtre actif. Tout passe par : presse-papier,
  I/O fichier, lecture de config, et Win32 (`SendInput`, focus fenêtre).
- PoE doit tourner en **fenêtré / fenêtré sans bordure** (pas exclusive fullscreen) pour tout overlay.
- Dossier PoE : `Documents/My Games/Path of Exile`.

---

## 2. Architecture (logique dans le Core, 1 frontend)

```
PoeHotFilter.sln
├── src/PoeHotFilter.Core/        net8.0          — TOUTE la logique, UI-agnostique
└── src/PoeHotFilter.Photino/     net8.0-windows  — FRONTEND (web UI dans WebView2)
```

- **Core** ne dépend d'aucune UI. Parsing, génération de filtre, lecture config, orchestration.
- **Photino.** Une fenêtre native transparente/chromeless/topmost qui héberge
  l'UI web (`wwwroot/`, HTML/CSS/JS) dans le WebView2 de l'OS. Le C# fait le hotkey, la capture,
  l'I/O ; le JS fait l'UI. Pont via web-messages (voir §6).

> Note historique : il y avait un projet Avalonia (`src/PoeHotFilter.App`) — supprimé.
> Photino réutilise directement le "playground" web (cf. §7) comme vraie UI.

---

## 3. Build & run

> ⚠️ **Ne se compile pas hors Windows** : `net8.0-windows` + P/Invoke Win32 + WebView2.
> Le développement précédent s'est fait sans SDK .NET dispo — donc le code C# **n'a jamais été
> compilé**, seulement relu. Premier `dotnet build` réel = à faire côté Windows ; attends-toi à
> d'éventuels petits ajustements (versions de packages, API Photino — voir §6).

```bash
dotnet build PoeHotFilter.sln
dotnet run --project src/PoeHotFilter.Photino
```

Packages clés : `Photino.NET` 3.1.13 (namespace `Photino.NET`, **pas** `PhotinoNET`),
`SharpHook` 5.3.8 (hook clavier global + simulation de touches ; `EventSimulator` n'est
**pas** IDisposable).

---

## 4. Le Core en détail (`src/PoeHotFilter.Core/`)

- **`Models/ParsedItem.cs`** — résultat du parse presse-papier (BaseType, ItemClass, Rarity, Name, ItemLevel).
- **`Models/FilterRule.cs`** — une règle utilisateur + enums :
  - `RarityFilter` : Any / Normal / Magic / Rare / Unique
  - `IconShape` : None, Circle, Diamond, Hexagon, Square, Star, Triangle, Cross, Moon, Raindrop,
    **Kite**, Pentagon, UpsideDownHouse  *(Kite ET Raindrop sont tous deux des keywords valides du filtre)*
  - `IconColor` : 11 couleurs (Blue, Green, Brown, Red, White, Yellow, Cyan, Grey, Orange, Pink, Purple)
  - champs : IlvlMode/IlvlValue, Text/Border/BackgroundColor, FontSize, IconShape/IconColor/IconSize.
- **`Parsing/ItemParser.cs`** — `TryParse(clipboard)`. Format presse-papier : sections séparées par
  des lignes `--------` ; l'en-tête contient `Item Class:`, `Rarity:`, la/les ligne(s) de nom, et le
  **BaseType = dernière ligne de l'en-tête** ; `Item Level: N` dans sa propre section.
  Rare/Unique ont **2 lignes de nom**.
- **`Filter/FilterBlockBuilder.cs`** — règles → texte `.filter`. Émet `MinimapIcon` si forme ≠ None.
- **`Filter/FilterFileManager.cs`** — écrit le filtre géré `_PoeHotFilter.filter` ; injecte **une**
  ligne `Import` en haut du filtre actif (marqueur `# PoeHotFilter import (managed)`) ; crée un
  `.phf-backup` avant la 1ʳᵉ édition ; `IsImportPresent(path)` ; idempotent.
- **`Filter/FilterStyleExtractor.cs`** — `StylePreset` (Text/Border/BackgroundColor, FontSize,
  **IconShape/IconColor/IconSize**, Source) ; `Extract(filterText)` récolte les styles du filtre
  actif **y compris `MinimapIcon`** (regex `^\s*MinimapIcon\s+(\d+)\s+(\w+)\s+(\w+)`) ; nomme les
  currencies notables (Divine Orb, etc.). `BuiltIns()` renvoie 5 presets, chacun **avec une icône** :
  **T1 / Divine, Currency, Rare, Unique, Flag** (noms épurés, sans parenthèses descriptives).
- **`Storage/PoePaths.cs`**, **`Storage/PoeConfigReader.cs`** — lit le filtre actif depuis
  `production_Config.ini`, section `[UI]`, clés `item_filter=` et `item_filter_loaded_successfully=`
  (on **préfère ce dernier** : c'est celui réellement rendu).
- **`Storage/AppSettings.cs`** — Hotkey, ActiveFilterPath, PoeFolderOverride, AutoReload,
  FollowInGameFilter. *(Pas de liste de filtres "approuvés" — supprimée, voir §5.)*
- **`Storage/RuleStore.cs`** — JSON atomique → `%APPDATA%/PoeHotFilter/rules.json`.
- **`Game/IGameController.cs`** + **`LiveFilterService.cs`** — l'orchestrateur :
  `InitializeAsync` (charge règles/presets, **n'injecte pas** : soumis à consentement),
  `IsImportInjected`, `InjectImportAsync`, `RetargetActiveFilterAsync`, `RefreshPresets`,
  `ReapplyAsync`, `AddRuleAsync(...)`, `Presets`, `ActiveFilterPath`.

### Syntaxe d'un bloc filtre généré
```
Show
    BaseType "Exact Base Name"
    Rarity Rare                 # optionnel
    ItemLevel >= 84             # selon IlvlMode
    GemLevel >= 20              # gems uniquement, selon GemLevelMode
    Quality >= 20              # gems + items en général (jamais currency), selon QualityMode
    EnchantmentPassiveNode "Aura Effect"  # cluster jewels : nom du petit passif (listes par taille dans app.js, source RePoE)
    EnchantmentPassiveNum >= 4  # cluster jewels : nombre de passifs, selon PassiveNumMode
    SetTextColor 255 0 0 255
    SetBorderColor 255 0 0 255
    SetBackgroundColor 255 255 255 255
    SetFontSize 45              # 18..45
    MinimapIcon 0 Red Star      # <size 0|1|2> <Color> <Shape>, si icône
```
`MinimapIcon` : size **0 = large, 1 = medium, 2 = small**.

**Conditions selon le type d'item** (détecté via `Stackable` = a une `Stack Size`, et `IsGem` = rareté `Gem`) :
- **Currency** (tout item empilable) : **pas** de `Rarity`, **pas** de `Corrupted`, **pas** de `Quality`.
- **Gem** : **pas** de `Rarity` (gardé : `Corrupted`) ; émet `GemLevel` + `Quality`.
- **Général** (autre) : `Rarity` + `Corrupted` + `ItemLevel` + `Quality`.

`GemLevelMode`/`QualityMode` réutilisent l'enum `IlvlMatchMode` (Any / ≥ / Exact). `FilterBlockBuilder.AppendThreshold`
factorise l'émission `<keyword> >= N` / `= N`. Le parser lit `Quality: +N%` et le `Level:` des gems (seulement
si gem, sinon collision avec le `Level:` des *Requirements* d'un équipement).

---

## 5. Consentement (important)

Règle unique : **si la ligne `Import` gérée n'est PAS en haut du filtre actif → on demande**
(via `confirm()` web). `IsImportInjected` (qui lit le fichier) est
**la seule source de vérité** — on ne stocke pas de chemins "approuvés".
Conséquence voulue : un re-export FilterBlade efface la ligne → on redemande à la prochaine règle.
`ActiveFilterWatcher` surveille `production_Config.ini` + fichiers `.filter` ; sur changement de
filtre ou re-export, on retarget + refresh presets + redemande le consentement si l'Import manque
(piloté par le réglage `FollowInGameFilter`, activé par défaut).

---

## 6. Pont Photino (`src/PoeHotFilter.Photino/`)

- **`Program.cs`** — `[STAThread]`, fenêtre transparente chromeless topmost plein écran
  (dimensions via `GetSystemMetrics`). Branche hotkey → capture → `Send("itemCaptured")`.
  Handler des messages web : `ready`, `addRule`, `confirmInjection`, `cancel`.
- **`WebMessage.cs`** — DTOs JSON (`WebMessage`, `AddRulePayload`, `CapturedItemMsg`).
- **`Services/`** — `WindowsGameController`, `HotkeyService`, `ActiveFilterWatcher`
  (namespace `PoeHotFilter.Photino.Services`, copiés/adaptés depuis l'App).
- **`wwwroot/index.html` + `wwwroot/app.js`** — la vraie UI (tout en **anglais**).
- **`wwwroot/icons/atlas.png`** — sprite d'icônes (voir §8).
- **`wwwroot/Fontin.ttf` + `wwwroot/FontinSmallCaps.ttf`** — polices embarquées.

**Pont JS ↔ C#** : `window.external.sendMessage(json)` (JS→C#) et `window.external.receiveMessage(cb)`
(C#→JS). ⚠️ Selon la version exacte de Photino, ces noms peuvent différer légèrement — **à vérifier
au premier run**.

Messages : JS envoie `{type, payload}`. Types entrants côté C# : `ready` (→ push presets),
`addRule` (→ `AddRulePayload`), `confirmInjection` (`{approved:bool}`), `cancel`,
`queryStyle` (`{baseType,itemClass,stackable,isGem}` → répond `itemStyle`).
Sortants vers JS : `itemCaptured` (`CapturedItemMsg`), `presets`, `needInjection`, `ruleAdded`, `noItem`,
`manualPick` (hotkey pressé dans le vide → la UI ouvre le popup avec le sélecteur PICK ITEM),
`itemStyle` (`CurrentStyleDto` : look actuel d'une base sous le filtre actif).

**Mode "pick manuel"** : si le hotkey de capture est pressé sans item sous le curseur (le presse-papier
ne change pas après le Ctrl+C simulé), `HotkeyService` lève `NoItemUnderCursor` → le popup s'ouvre avec
une section **PICK ITEM** (Category → Item, mêmes données que le playground, servies par
`wwwroot/items.js` généré depuis `items_min.json`). Chaque sélection envoie `queryStyle` pour
pré-remplir l'apparence actuelle. `stackable`/`isGem` sont déduits de la classe de l'item côté JS.

---

## 7. Le playground (`playground.html`)

Fichier **HTML autonome unique** (≈ 580 Ko) qui sert à **prototyper l'UI hors jeu**. C'est le même
design que l'UI Photino, mais 100 % self-contained : **atlas d'icônes ET police SmallCaps inlinés en
base64**, plus un **dataset d'items inliné**. Aucune dépendance externe → on double-clique, ça marche.

Sections du popup : TEST ITEM (sélecteur, voir plus bas), TARGET (rareté), ITEM LEVEL (Any/≥/Exact),
PRESETS (chips), APPEARANCE (3 color pickers alignés + alphas + size), MINIMAP ICON (forme/couleur/taille),
IN-GAME PREVIEW.

**Sélecteur TEST ITEM (playground uniquement)** : ~2583 bases droppables, **41 catégories**, issues de
**RePoE** (données extraites du jeu). Deux menus (Category → Item) avec `<optgroup>` :
- **General** : Currency, Fragments, Essences, Fossils, Oils, Catalysts, Delirium Orbs,
  Breach/Splinters, Vials, Incubators, Divination Cards, Gems, Jewels, Flasks, Maps, Accessories, Quivers.
- **Armour (by attribute)** : Strength, Dexterity, Intelligence + hybrides Str/Dex, Str/Int, Dex/Int,
  Str/Dex/Int, Ward (déduit des `tags` RePoE : `str_armour`/`dex_armour`/`int_armour`…).
- **Weapons (by type)** : 1H/2H Sword, Thrusting Sword, 1H/2H Axe, 1H/2H Mace, Sceptre, Bow, Claw,
  Dagger, Rune Dagger, Wand, Staff, Warstaff, Fishing Rod.

Choisir un item met à jour titre + sous-titre (classe réelle) + label de preview. Données dans
`items_min.json` (aussi à la racine, pour référence). **Le sélecteur n'existe QUE dans le playground**
(en jeu, l'item vient du presse-papier — inutile côté Photino).

---

## 8. Icônes & polices (fidélité jeu)

### Icônes minimap = vraies icônes GGG
Source : sprite `MiniMapIcon_FullSpriteV2.png` (fourni). Découpé en grille **11 couleurs × 12 formes**,
fond noir rendu transparent, repacké en **`atlas.png`** (cellules **44px**, transparent).
- Ordre des **formes** (lignes) : Circle, Diamond, Hexagon, Square, Star, Triangle, Cross, Moon,
  Raindrop, **Kite**, Pentagon, UpsideDownHouse.
- Ordre des **couleurs** (colonnes) : Blue, Green, Brown, Red, White, Yellow, Cyan, Grey, Orange,
  Pink, Purple.
- Rendu via **CSS sprite** (`background-position`). 3 tailles 0/1/2 = **scaling** de la même cellule
  (1.0 / 0.78 / 0.58) — le sprite n'a qu'une résolution source (limitation mineure assumée).
- Photino charge `wwwroot/icons/atlas.png` ; le playground a l'atlas **inliné en base64**.

### Police = Fontin (look in-game)
PoE utilise **Fontin SmallCaps** pour les labels d'items. Embarquée partout :
- Photino : `wwwroot/FontinSmallCaps.ttf` (label) + `Fontin.ttf` (UI), via `@font-face`.
- Playground : SmallCaps **inlinée en base64**.
- Noms internes : famille `"Fontin"` (Regular) et `"Fontin SmallCaps"`.
- Licence : EULA gratuite exljbris → embedding programme OK (attribution gardée dans le CSS).

### Rendu du label de preview (calé sur FilterBlade)
- **Hauteur max ~33px** ; texte proportionnel à `SetFontSize` ; **padding fixe `0.3125rem` (= 5px)**.
- **Pas de text-shadow.**
- **Icône À L'INTÉRIEUR du cadre**, avant le texte (partage fond + bordure), comme FilterBlade.
- En preview, l'icône **scale avec le label** pour garder une proportion constante — mais le
  **sélecteur de taille 0/1/2 reste fonctionnel** (la taille choisie part bien dans le filtre).
- Les **chips de presets sont des mini-previews** : le nom est stylé avec les couleurs du preset
  (+ icône réelle), pas de carré séparé ni de description.

---

## 9. Décisions en attente / TODO

- **Renommage en `PoeHotFilter`** : ✅ fait (voir §1). Reste seulement le dossier racine disque
  `PoeLiveFilter` (laissé tel quel pour ne pas casser les chemins de session).
- ⚠️ Le nom `PoeHotFilter` contient « Poe » (marque GGG). Choix assumé par l'utilisateur en
  connaissance du risque marque sur un repo/release public. Pour mémoire : la reco habituelle est
  d'éviter « PoE »/« Path of Exile » dans le NOM, et de ne l'employer qu'en description
  (« for Path of Exile », usage nominatif).
- Run réel en jeu à valider (build OK, mais comportement runtime à tester).

---

## 10. Conventions

- **UI 100 % en anglais.** (Les échanges de dev peuvent être en français, mais l'app reste EN.)
- Presets : noms **sans** parenthèses descriptives.
- Filtre : on n'écrit qu'**une** ligne `Import` gérée ; backup `.phf-backup` avant 1ʳᵉ écriture ; idempotent.
- Consentement basé sur la **présence de l'Import**, jamais sur des chemins stockés.
- Garder le **Core UI-agnostique** : toute logique nouvelle va dans Core, pas dans un frontend.
- Toute modif d'icône/preset/preview doit rester **cohérente entre playground et Photino** (mêmes
  constantes d'ordre formes/couleurs, même logique de rendu).

---

## 11. Livrables actuels
- `playground.html` — prototype UI autonome (à ouvrir dans un navigateur).
- `PoeHotFilter.zip` — projet complet (hors `bin/`, `obj/`).
- `items_min.json` — dataset items catégorisé (référence ; déjà inliné dans le playground).
