# Design — Pré-remplir l'éditeur avec le look actuel de l'item

Date : 2026-06-05
Projet : HotFilter (`PoeHotFilter`)

## Problème

Quand un item est capturé (hotkey → Ctrl+C → parse), le popup overlay ouvre l'éditeur
avec les **valeurs par défaut** des color pickers / icône. La section **IN-GAME PREVIEW**
(fonction `render()` dans `wwwroot/app.js`) affiche donc l'aspect **cible** en cours
d'édition, jamais l'aspect réel que l'item a actuellement en jeu.

L'utilisateur veut que l'éditeur soit **pré-rempli avec le look actuel de l'item**, tel que
le filtre de loot chargé l'affiche réellement, pour éditer à partir de là. La preview continue
de suivre l'édition comme aujourd'hui.

### Décisions de cadrage (validées)

- **Source du look** : le **filtre actif** (et non la couleur de rareté par défaut ni le
  preset le plus proche). Le plus fidèle à ce que voit le joueur.
- **Matching conservateur** : face aux conditions non-évaluables (sockets, corrupted,
  influence…), un bloc ne matche que si **toutes** ses conditions sont évaluables et passent.
- **Cas Hide** : si le bloc gagnant est un `Hide`, pré-remplir avec la couleur de rareté
  par défaut **et signaler** que l'item est actuellement caché (badge à côté de l'ilvl).
- **Tests** : ajouter un projet de tests xUnit pour le Core.

### Point clé sur l'Import géré

Le filtre actif ne contient qu'une **ligne `Import`** vers notre fichier géré
`_PoeHotFilter.filter` — pas son contenu inline. Un évaluateur qui parse le texte du filtre
actif **ne voit donc PAS nos propres règles injectées**, seulement le filtre de base de
l'utilisateur (FilterBlade…). C'est le comportement voulu pour un pré-remplissage : on reflète
le look **sous-jacent**, pas notre édition précédente. La ligne `Import` est traitée comme
opaque (on ne la déréférence pas).

## Architecture

### 1. Nouveau composant Core : `src/PoeHotFilter.Core/Filter/ActiveStyleResolver.cs`

Évaluateur **pur**, sans état, sans I/O :

```csharp
public static StyleMatch Resolve(string? filterText, ParsedItem item)
```

- Parse les blocs `Show`/`Hide` dans l'ordre du fichier.
- Réutilise le parsing de blocs déjà présent dans `FilterStyleExtractor` : on **extrait le
  parser de blocs dans un type interne partagé** (ou on le factorise) et on **élargit les
  conditions captées** au-delà des `BaseType`/`Rarity`/`LinkedSockets` actuels.
- Conditions **évaluables** (avec les données de `ParsedItem`) :
  - `Class` — `item.ItemClass`
  - `BaseType` — `item.BaseType`
  - `Rarity` — `item.Rarity`
  - `ItemLevel` — `item.ItemLevel`
  - `Quality` — `item.Quality`
  - `GemLevel` — `item.GemLevel`
  - marqueur `Show` / `Hide`
- **Matching conservateur** : un bloc matche ssi **toutes** ses lignes-conditions sont d'un
  type évaluable ci-dessus **et** passent pour l'item. Toute condition d'un type **non géré**
  (`Sockets`, `LinkedSockets`, `Corrupted`, `HasInfluence`/Shaper/Elder…, `StackSize`,
  `MapTier`, `AreaLevel`, `Width`, `Height`, `SocketGroup`, `Identified`, `Mirrored`, etc.)
  → le bloc est **ignoré** (jamais retenu).
- **Premier bloc survivant gagne** (top du fichier = priorité, comme PoE).

#### Sémantique de matching (fidèle à PoE)

- `Class` et `BaseType` : match **substring** (insensible à la casse) par défaut ;
  **exact** si l'opérateur `==` est présent. Plusieurs valeurs entre guillemets = OU.
  Ex. `BaseType "Vaal Regalia"` matche un item « Vaal Regalia » ; `Class "Maps"` matche
  une classe contenant « Maps ».
- `Rarity` : supporte la **forme liste** (`Rarity Normal Magic`) **et** la **forme opérateur**
  (`Rarity <= Magic`, `Rarity >= Rare`, `= < >`). Ordre PoE : Normal < Magic < Rare < Unique.
- `ItemLevel` / `Quality` / `GemLevel` : numériques avec opérateurs `<= >= = < >`
  (défaut `=` si opérateur absent). Si l'item n'a pas la donnée (ex. pas d'ilvl pour une
  currency) et qu'un bloc porte cette condition → non-évaluable → bloc ignoré.

### 2. Résultat : `StyleMatch`

Record dans le même fichier (ou `Models/`), avec couleurs en `FilterColor` :

```csharp
public sealed record StyleMatch(
    bool Matched,           // un bloc Show/Hide a matché
    bool Hidden,            // le bloc gagnant est un Hide
    FilterColor Text,
    FilterColor Border,
    FilterColor Background,
    int FontSize,
    IconShape IconShape,
    IconColor IconColor,
    int IconSize,
    string Source);         // "Active filter" | "Hidden in filter" | "Default (rarity)"
```

Construction du résultat :

- **`Show` matché** → couleurs / `SetFontSize` / `MinimapIcon` du bloc. Bordure absente →
  retombe sur la couleur texte (comme `FilterStyleExtractor.ToPreset`). Fond absent → noir.
  `Source = "Active filter"`.
- **`Hide` matché** → `Hidden = true`, couleurs = **défaut de rareté** (un `Hide` n'a pas de
  style affichable), pas d'icône, `Source = "Hidden in filter"`.
- **Rien ne matche** → fallback **couleur de rareté PoE**, pas d'icône,
  `Source = "Default (rarity)"`.

#### Couleurs de rareté par défaut (fallback)

| Cas | Texte (RGB) |
| --- | --- |
| Normal | 200, 200, 200 |
| Magic | 136, 136, 255 |
| Rare | 255, 255, 119 |
| Unique | 175, 96, 37 |
| Gem (`IsGem`) | 27, 162, 155 |
| Currency (`IsStackable`, pas de rareté) | 170, 158, 130 |
| inconnu | 200, 200, 200 |

Bordure = transparente (alpha 0) ; fond = noir opaque ; `FontSize = 32` ; pas d'icône.
(Valeurs à ajuster au besoin pour coller au rendu jeu — détail mineur.)

### 3. Exposition via `LiveFilterService`

Nouvelle méthode, cohérente avec `LoadPresets` (try/catch lecture fichier) :

```csharp
public StyleMatch CurrentStyleFor(ParsedItem item)
{
    string? text = null;
    try { if (ActiveFilterPath is not null && File.Exists(ActiveFilterPath))
              text = File.ReadAllText(ActiveFilterPath); }
    catch { /* filtre verrouillé/malformé → fallback rareté */ }
    return ActiveStyleResolver.Resolve(text, item);
}
```

Le Core reste UI-agnostique : toute la logique est dans `ActiveStyleResolver`.

### 4. Transport vers l'UI

`src/PoeHotFilter.Photino/WebMessage.cs` — ajout d'un type imbriqué et d'un champ sur
`CapturedItemMsg` :

```csharp
public sealed class CurrentStyleDto
{
    public int[] Text { get; set; } = { 255, 255, 255, 255 };
    public int[] Border { get; set; } = { 255, 255, 255, 255 };
    public int[] Background { get; set; } = { 0, 0, 0, 255 };
    public int FontSize { get; set; } = 45;
    public string IconShape { get; set; } = "None";
    public string IconColor { get; set; } = "White";
    public int IconSize { get; set; } = 1;
    public bool Hidden { get; set; }
    public string Source { get; set; } = "";
}
// sur CapturedItemMsg :
public CurrentStyleDto? Current { get; set; }
```

`Program.cs`, au moment de la capture (juste avant `Send("itemCaptured", msg)`) :
calcule `_service.CurrentStyleFor(item)` et mappe vers `CurrentStyleDto`. Couleurs en
**`int[4]`** (pas `byte[]`) — voir la note base64 de `PushPresets` et la mémoire
[[photino-bridge-serialization-quirks]].

### 5. Frontend (`src/PoeHotFilter.Photino/wwwroot/app.js` — Photino uniquement)

- `openPopup(item)` : après le bloc de défauts existant, **si `item.current` est présent**,
  pré-remplir depuis lui :
  - `textColor/borderColor/bgColor` (`toHex`) + `textA/borderA/bgA`
  - `fontSize`
  - `iconShape/iconColor/iconSize`
  - puis `render()` (déjà appelé en fin de `openPopup`).
- Si `item.current.hidden` → afficher un **petit badge discret** « currently hidden »
  **à côté de `DETECTED ILVL`** (élément `hDet`). Sinon badge masqué.
- `render()` est **inchangé** : la preview suit l'édition comme aujourd'hui ; le pré-fill
  ne fait que définir l'état initial des contrôles.
- Robustesse : si `item.current` est absent (ex. playground), comportement actuel inchangé.

### 6. Tests : nouveau projet `tests/PoeHotFilter.Core.Tests` (xUnit, `net8.0`)

Ajouté à `PoeHotFilter.sln`. Couvre `ActiveStyleResolver.Resolve` (pur, sans I/O) :

- BaseType substring vs `==` exact.
- Class substring.
- Rarity forme liste et forme opérateur (`<=`, `>=`).
- ItemLevel / Quality / GemLevel avec opérateurs ; donnée absente → bloc ignoré.
- Condition non gérée présente (`Corrupted`, `Sockets`) → bloc **ignoré** même si les autres
  conditions passeraient.
- Premier-match l'emporte (bloc spécialisé en haut vs générique en bas).
- Bloc gagnant `Hide` → `Hidden=true` + couleur de rareté + `Source="Hidden in filter"`.
- Aucun match → fallback rareté + `Source="Default (rarity)"`.
- `filterText` null/vide → fallback rareté.

## Portée / non-touché

- **Playground (`playground.html`)** : inchangé — pas de filtre actif à lire (prototype
  hors-jeu). Le pré-fill est une donnée Photino-only ; le JS reste sans effet si `current`
  est absent.
- **Cohérence icônes/preview (CLAUDE.md §10)** : aucun changement de rendu d'icône ou de
  preview, donc rien à resynchroniser entre playground et Photino.
- **`FilterStyleExtractor`** : on factorise/extrait son parser de blocs pour le partager,
  sans changer son comportement public (`CuratedPresets`, `BuiltIns`).

## Risques / limites assumées

- Le matching conservateur peut **tomber sur le bloc générique** plutôt que sur un bloc
  spécialisé (à condition exotique) que l'item touche réellement en jeu. C'est le tradeoff
  accepté : pas de faux positif, look prévisible.
- On ne déréférence pas l'`Import` : nos propres règles ne sont pas prises en compte (voulu).
- Classification des lignes d'un bloc (sûr par construction, pas besoin d'énumérer toutes
  les conditions PoE) :
  - **ignorées** : la ligne `Show`/`Hide`/`Minimal`, les lignes vides, les commentaires
    (`#…`), et les lignes de **style** (`SetTextColor`, `SetBorderColor`, `SetBackgroundColor`,
    `SetFontSize`, `MinimapIcon`, `PlayAlertSound`, `SetFontSize`, `CustomAlertSound`,
    `PlayEffect`, `DisableDropSound`, etc.) — elles n'affectent pas le match.
  - **évaluées** : les 6 conditions gérées (`Class`, `BaseType`, `Rarity`, `ItemLevel`,
    `Quality`, `GemLevel`).
  - **disqualifiantes** : toute **autre** ligne de condition (mot-clé inconnu en tête de
    ligne, non-style) → le bloc est ignoré.
