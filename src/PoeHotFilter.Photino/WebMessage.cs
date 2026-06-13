using System.Text.Json.Serialization;

namespace PoeHotFilter.Photino;

/// <summary>
/// Messages exchanged with the web UI over Photino's web-message channel.
/// JS sends a JSON {type, payload}; C# replies by posting JSON back to window.external.
/// </summary>
public sealed class WebMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("payload")] public System.Text.Json.JsonElement Payload { get; set; }
}

/// <summary>Payload the UI sends when adding (no Id) or editing (Id set) a rule.</summary>
public sealed class AddRulePayload
{
    /// <summary>Set when editing an existing rule; null/empty when adding a new one.</summary>
    public string? Id { get; set; }
    public string BaseType { get; set; } = "";
    public string? ItemClass { get; set; }
    public bool Stackable { get; set; }               // carried from capture: drives currency handling
    public bool IsGem { get; set; }                   // carried from capture: drives gem handling
    public string Rarity { get; set; } = "Any";       // Any|Normal|Magic|Rare|Unique|AnyNonUnique
    public string Corrupted { get; set; } = "Any";    // Any|Yes|No
    public string Action { get; set; } = "Show";      // Show|Hide
    public string IlvlMode { get; set; } = "GreaterOrEqual"; // Any|GreaterOrEqual|Exact
    public int IlvlValue { get; set; }
    public string QualityMode { get; set; } = "Any";  // Any|GreaterOrEqual|Exact
    public int QualityValue { get; set; }
    public string GemLevelMode { get; set; } = "Any"; // Any|GreaterOrEqual|Exact
    public int GemLevelValue { get; set; }
    public int StackMin { get; set; }                 // 0 = no lower bound
    public int StackMax { get; set; }                 // 0 = no upper bound
    public string? EnchantNode { get; set; }          // cluster jewels: passive node name; null/"" = any
    public string PassiveNumMode { get; set; } = "Any"; // Any|GreaterOrEqual|Exact
    public int PassiveNumValue { get; set; }
    public int[] TextColor { get; set; } = { 255, 255, 255, 255 };
    public int[] BorderColor { get; set; } = { 255, 255, 255, 255 };
    public int[] BackgroundColor { get; set; } = { 0, 0, 0, 255 };
    public int FontSize { get; set; } = 45;
    public string IconShape { get; set; } = "None";
    public string IconColor { get; set; } = "White";
    public int IconSize { get; set; } = 1;
    public int AlertSound { get; set; } = 0;     // 0 = none, 1-16 built-in
    public int AlertVolume { get; set; } = 100;  // 0-300
}

/// <summary>Sent to the UI when an item is captured, to pre-fill the popup.</summary>
public sealed class CapturedItemMsg
{
    public string BaseType { get; set; } = "";
    public string ItemClass { get; set; } = "";
    public string? Name { get; set; }
    public string? Rarity { get; set; }
    public int? ItemLevel { get; set; }
    public bool Stackable { get; set; }
    public bool IsGem { get; set; }
    public int? Quality { get; set; }
    public int? GemLevel { get; set; }
    /// <summary>Current look under the active filter (pre-fills the editor). Null if unavailable.</summary>
    public CurrentStyleDto? Current { get; set; }
}

/// <summary>The item's current appearance under the active filter, used to pre-fill the editor.</summary>
public sealed class CurrentStyleDto
{
    // Colours as int[4] (R,G,B,A). NOT byte[] — System.Text.Json serializes byte[] as base64,
    // which breaks the JS side (see PushPresets and the photino-bridge-serialization-quirks note).
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
