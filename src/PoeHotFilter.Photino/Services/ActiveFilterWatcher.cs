using PoeHotFilter.Core.Storage;

namespace PoeHotFilter.Photino.Services;

/// <summary>
/// Watches PoE's production_Config.ini and the active filter file, and fires events when
/// either changes — so the app can follow whatever filter you select in-game:
///   • OnActiveFilterChanged: you picked a different filter in the game's options.
///   • OnActiveFilterEdited:   the current filter's contents changed (e.g. FilterBlade re-export).
///
/// FileSystemWatcher can fire several times per save, so changes are debounced.
/// </summary>
public sealed class ActiveFilterWatcher : IDisposable
{
    private readonly string _poeFolder;
    private readonly FileSystemWatcher _configWatcher;
    private readonly FileSystemWatcher _filterWatcher;
    private readonly System.Timers.Timer _debounce;

    private string? _currentFilterName;
    private string? _pendingKind; // "changed" or "edited"

    public event Action<string>? OnActiveFilterChanged; // arg: full path of new filter
    public event Action<string>? OnActiveFilterEdited;  // arg: full path of edited filter

    public ActiveFilterWatcher(string poeFolder)
    {
        _poeFolder = poeFolder;
        _currentFilterName = PoeConfigReader.GetActiveFilterName(poeFolder);

        _debounce = new System.Timers.Timer(400) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Fire();

        _configWatcher = new FileSystemWatcher(poeFolder, "production_Config.ini")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _configWatcher.Changed += (_, _) => Queue("changed");

        // Watch *.filter for content edits (re-exports). We resolve which one matters on fire.
        _filterWatcher = new FileSystemWatcher(poeFolder, "*.filter")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _filterWatcher.Changed += (_, e) => Queue("edited", e.Name);
    }

    public void Start()
    {
        _configWatcher.EnableRaisingEvents = true;
        _filterWatcher.EnableRaisingEvents = true;
    }

    private string? _editedName;

    private void Queue(string kind, string? editedName = null)
    {
        _pendingKind = kind;
        if (editedName is not null) _editedName = editedName;
        _debounce.Stop();
        _debounce.Start();
    }

    private void Fire()
    {
        if (_pendingKind == "changed")
        {
            var newName = PoeConfigReader.GetActiveFilterName(_poeFolder);
            if (!string.IsNullOrEmpty(newName) &&
                !string.Equals(newName, _currentFilterName, StringComparison.OrdinalIgnoreCase))
            {
                _currentFilterName = newName;
                var path = PoeConfigReader.GetActiveFilterPath(_poeFolder);
                if (path is not null) OnActiveFilterChanged?.Invoke(path);
                return;
            }
        }

        // An edit to the currently active filter (ignore edits to other files, incl. our managed one).
        if (_pendingKind == "edited" && _editedName is not null)
        {
            if (_editedName.Equals(_currentFilterName, StringComparison.OrdinalIgnoreCase))
            {
                var path = Path.Combine(_poeFolder, _editedName);
                if (File.Exists(path)) OnActiveFilterEdited?.Invoke(path);
            }
        }
    }

    public void Dispose()
    {
        _debounce.Dispose();
        _configWatcher.Dispose();
        _filterWatcher.Dispose();
    }
}
