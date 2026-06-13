namespace PoeHotFilter.Core.Game;

/// <summary>
/// Drives the running PoE client: focuses it and issues the chat command that
/// reloads the active item filter in place (/reloaditemfilter), confirmed working
/// without a game restart.
/// </summary>
public interface IGameController
{
    /// <summary>True if a Path of Exile client window is currently found.</summary>
    bool IsGameRunning();

    /// <summary>True if the PoE client window is currently the foreground (focused) window.</summary>
    bool IsGameForeground();

    /// <summary>
    /// Focuses PoE and sends the reload command via the chat box.
    /// Returns false if the game window couldn't be found/focused.
    /// </summary>
    Task<bool> ReloadFilterAsync(CancellationToken ct = default);

    /// <summary>Reads the current clipboard text (used to capture the Ctrl+C item dump).</summary>
    string? GetClipboardText();
}
