using PoeHotFilter.Core.Filter;
using PoeHotFilter.Core.Game;
using PoeHotFilter.Core.Models;
using PoeHotFilter.Core.Parsing;
using PoeHotFilter.Core.Storage;

namespace PoeHotFilter.Core;

/// <summary>
/// The single entry point the UI talks to. Holds the in-memory rule list,
/// keeps the JSON store and the .filter file in sync, and triggers reloads.
/// </summary>
public sealed class LiveFilterService
{
    private readonly RuleStore _store;
    private readonly FilterFileManager _fileManager;
    private readonly IGameController _game;

    private List<FilterRule> _rules = new();

    /// <summary>Appearance presets harvested from the active filter, plus built-ins.</summary>
    public IReadOnlyList<StylePreset> Presets { get; private set; } = Array.Empty<StylePreset>();

    public LiveFilterService(RuleStore store, FilterFileManager fileManager, IGameController game)
    {
        _store = store;
        _fileManager = fileManager;
        _game = game;
    }

    public IReadOnlyList<FilterRule> Rules => _rules;

    /// <summary>
    /// Loads rules + presets for a filter, but does NOT modify the filter file.
    /// Injection is a separate, explicit step (see <see cref="InjectImportAsync"/>) so the UI
    /// can ask the user before the first time we touch their filter.
    /// </summary>
    public async Task InitializeAsync(string activeFilterPath, CancellationToken ct = default)
    {
        ActiveFilterPath = activeFilterPath;
        _rules = await _store.LoadAsync(ct);
        await RegenerateAsync(ct);
        LoadPresets(activeFilterPath);
    }

    /// <summary>True if the active filter already contains our managed Import line.</summary>
    public bool IsImportInjected =>
        ActiveFilterPath is not null && _fileManager.IsImportPresent(ActiveFilterPath);

    /// <summary>
    /// Writes the Import line into the active filter (idempotent) and reloads in-game.
    /// Call only after the user has approved touching this filter.
    /// </summary>
    public async Task InjectImportAsync(bool reload = true, CancellationToken ct = default)
    {
        if (ActiveFilterPath is null) return;
        await _fileManager.EnsureImportInjectedAsync(ActiveFilterPath, ct);
        await RegenerateAsync(ct);
        if (reload) await _game.ReloadFilterAsync(ct);
    }

    /// <summary>Parses the active filter for reusable styles (Divine Orb etc.), prepends built-ins.</summary>
    private void LoadPresets(string activeFilterPath)
    {
        string? text = null;
        try
        {
            if (File.Exists(activeFilterPath))
                text = File.ReadAllText(activeFilterPath);
        }
        catch { /* a malformed/locked filter just means we fall back to built-ins */ }
        Presets = FilterStyleExtractor.CuratedPresets(text);
    }

    /// <summary>The filter we're currently importing into / harvesting presets from.</summary>
    public string? ActiveFilterPath { get; private set; }

    /// <summary>
    /// Point the app at a different active filter (e.g. the user switched filters in-game).
    /// Refreshes presets and regenerates, but does NOT inject — the UI handles approval/injection.
    /// </summary>
    public async Task RetargetActiveFilterAsync(string newActiveFilterPath, CancellationToken ct = default)
    {
        ActiveFilterPath = newActiveFilterPath;
        await RegenerateAsync(ct);
        LoadPresets(newActiveFilterPath);
    }

    /// <summary>Re-harvest presets from the active filter without touching anything else.</summary>
    public void RefreshPresets()
    {
        if (ActiveFilterPath is not null)
            LoadPresets(ActiveFilterPath);
    }

    /// <summary>Regenerate the managed file from current rules and optionally reload in-game.</summary>
    public async Task ReapplyAsync(bool reload = true, CancellationToken ct = default)
    {
        await RegenerateAsync(ct);
        if (reload)
            await _game.ReloadFilterAsync(ct);
    }

    /// <summary>
    /// Parses the current clipboard into a ParsedItem so the popup can pre-fill.
    /// Returns null if the clipboard isn't a PoE item.
    /// </summary>
    public ParsedItem? CaptureHoveredItem() => ItemParser.TryParse(_game.GetClipboardText());

    /// <summary>
    /// The appearance the given item currently has under the active filter, used to pre-fill the
    /// editor. Reads the active filter file (the managed Import line is opaque, so our own rules are
    /// not considered). Falls back to a rarity-default look if the filter is missing/locked.
    /// </summary>
    public StyleMatch CurrentStyleFor(ParsedItem item)
    {
        string? text = null;
        try
        {
            if (ActiveFilterPath is not null && File.Exists(ActiveFilterPath))
                text = File.ReadAllText(ActiveFilterPath);
        }
        catch { /* locked/malformed filter -> rarity fallback */ }
        return ActiveStyleResolver.Resolve(text, item);
    }

    /// <summary>Adds a rule from a parsed item plus the user's popup choices, regenerates and reloads.</summary>
    public async Task<FilterRule> AddRuleAsync(
        ParsedItem item,
        RarityFilter rarity,
        IlvlMatchMode ilvlMode,
        int ilvlValue,
        FilterColor text,
        FilterColor border,
        FilterColor background,
        int fontSize = 45,
        IconShape iconShape = IconShape.None,
        IconColor iconColor = IconColor.White,
        int iconSize = 1,
        CorruptedMode corrupted = CorruptedMode.Any,
        int alertSound = 0,
        int alertVolume = 100,
        BlockAction action = BlockAction.Show,
        int stackMin = 0,
        int stackMax = 0,
        IlvlMatchMode qualityMode = IlvlMatchMode.Any,
        int qualityValue = 0,
        IlvlMatchMode gemLevelMode = IlvlMatchMode.Any,
        int gemLevelValue = 0,
        string? enchantNode = null,
        IlvlMatchMode passiveNumMode = IlvlMatchMode.Any,
        int passiveNumValue = 0,
        bool reload = true,
        CancellationToken ct = default)
    {
        var rule = new FilterRule
        {
            BaseType = item.BaseType,
            ItemClass = item.ItemClass,
            Stackable = item.IsStackable,
            IsGem = item.IsGem,
            Rarity = rarity,
            Corrupted = corrupted,
            Action = action,
            StackMin = stackMin,
            StackMax = stackMax,
            IlvlMode = ilvlMode,
            IlvlValue = ilvlValue,
            QualityMode = qualityMode,
            QualityValue = qualityValue,
            GemLevelMode = gemLevelMode,
            GemLevelValue = gemLevelValue,
            EnchantNode = string.IsNullOrWhiteSpace(enchantNode) ? null : enchantNode,
            PassiveNumMode = passiveNumMode,
            PassiveNumValue = passiveNumValue,
            TextColor = text,
            BorderColor = border,
            BackgroundColor = background,
            FontSize = fontSize,
            IconShape = iconShape,
            IconColor = iconColor,
            IconSize = iconSize,
            AlertSound = alertSound,
            AlertVolume = alertVolume,
            Label = item.Name ?? item.BaseType
        };

        // De-dup on the match signature (base + rarity + corrupted + ilvl).
        _rules.RemoveAll(r =>
            r.BaseType.Equals(rule.BaseType, StringComparison.OrdinalIgnoreCase) &&
            r.Rarity == rule.Rarity &&
            r.Corrupted == rule.Corrupted &&
            r.IlvlMode == rule.IlvlMode &&
            r.IlvlValue == rule.IlvlValue);

        _rules.Insert(0, rule);
        await PersistAndApplyAsync(reload, ct);
        return rule;
    }

    public async Task UpdateRuleAsync(FilterRule rule, bool reload = true, CancellationToken ct = default)
    {
        // rule is a reference into _rules already; just persist + reapply.
        await PersistAndApplyAsync(reload, ct);
    }

    public async Task DeleteRuleAsync(Guid id, bool reload = true, CancellationToken ct = default)
    {
        _rules.RemoveAll(r => r.Id == id);
        await PersistAndApplyAsync(reload, ct);
    }

    public async Task ToggleRuleAsync(Guid id, bool reload = true, CancellationToken ct = default)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule is null) return;
        rule.Enabled = !rule.Enabled;
        await PersistAndApplyAsync(reload, ct);
    }

    /// <summary>Writes JSON + regenerates the .filter, then optionally triggers an in-game reload.</summary>
    private async Task PersistAndApplyAsync(bool reload, CancellationToken ct)
    {
        await _store.SaveAsync(_rules, ct);
        await RegenerateAsync(ct);
        if (reload)
            await _game.ReloadFilterAsync(ct);
    }

    private Task RegenerateAsync(CancellationToken ct) => _fileManager.WriteManagedFileAsync(_rules, ct);
}
