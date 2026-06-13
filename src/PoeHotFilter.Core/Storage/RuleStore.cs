using System.Text.Json;
using System.Text.Json.Serialization;
using PoeHotFilter.Core.Models;

namespace PoeHotFilter.Core.Storage;

/// <summary>
/// Persists the rule set as JSON. This is the source of truth: the .filter file is a
/// disposable projection regenerated from here. Lets the app list/edit/delete rules.
/// </summary>
public sealed class RuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RuleStore(string storePath) => _path = storePath;

    /// <summary>Default location: %APPDATA%\PoeHotFilter\rules.json</summary>
    public static RuleStore Default()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoeHotFilter");
        Directory.CreateDirectory(dir);
        return new RuleStore(Path.Combine(dir, "rules.json"));
    }

    public async Task<List<FilterRule>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path))
                return new List<FilterRule>();

            await using var stream = File.OpenRead(_path);
            var rules = await JsonSerializer.DeserializeAsync<List<FilterRule>>(stream, JsonOptions, ct);
            return rules ?? new List<FilterRule>();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<FilterRule> rules, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Write to a temp file then move, so a crash mid-write can't corrupt the store.
            var tmp = _path + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, rules.ToList(), JsonOptions, ct);
            }
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
