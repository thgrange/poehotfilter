using PoeHotFilter.Core.Storage;
using SharpHook;
using SharpHook.Native;

namespace PoeHotFilter.Photino.Services;

/// <summary>
/// Listens for a configurable global hotkey, simulates Ctrl+C so PoE dumps the hovered
/// item to the clipboard, then raises <see cref="ItemCaptureRequested"/>.
///
/// The combo (key + Ctrl/Alt/Shift) is supplied via <see cref="UpdateHotkey"/> and can be
/// changed at runtime from settings without restarting the hook.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly SimpleGlobalHook _hook;
    private readonly EventSimulator _simulator = new();

    private KeyCode _triggerKey = KeyCode.VcB;
    private bool _needCtrl = true, _needAlt, _needShift;

    private KeyCode _listKey = KeyCode.VcS;
    private bool _listCtrl = true, _listAlt, _listShift;

    private bool _ctrlDown, _altDown, _shiftDown;

    /// <summary>Raised on a background thread shortly after the user presses the capture combo.</summary>
    public event Func<Task>? ItemCaptureRequested;

    /// <summary>
    /// Raised when the capture combo fired but nothing was under the cursor (the clipboard
    /// stayed untouched). Lets the app open the manual item picker instead.
    /// </summary>
    public event Func<Task>? NoItemUnderCursor;

    /// <summary>Raised when the user presses the "show custom filters" combo.</summary>
    public event Func<Task>? ShowRulesRequested;

    /// <summary>
    /// Gate: return false to ignore hotkeys (and let the keys pass through untouched).
    /// Used to only act while PoE is the foreground window.
    /// </summary>
    public Func<bool>? ShouldHandle;

    public HotkeyService()
    {
        _hook = new SimpleGlobalHook(GlobalHookType.Keyboard);
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
    }

    public void Start() => _hook.RunAsync();

    /// <summary>Applies the capture hotkey config. Safe to call at any time.</summary>
    public void UpdateHotkey(HotkeyConfig cfg)
    {
        _triggerKey = ParseKey(cfg.Key);
        _needCtrl = cfg.Ctrl;
        _needAlt = cfg.Alt;
        _needShift = cfg.Shift;
    }

    /// <summary>Applies the "show custom filters" hotkey config.</summary>
    public void UpdateListHotkey(HotkeyConfig cfg)
    {
        _listKey = ParseKey(cfg.Key);
        _listCtrl = cfg.Ctrl;
        _listAlt = cfg.Alt;
        _listShift = cfg.Shift;
    }

    /// <summary>Maps a config key name (e.g. "A", "F5") to a SharpHook KeyCode.</summary>
    public static KeyCode ParseKey(string name)
    {
        var trimmed = name.Trim();
        var candidate = "Vc" + (trimmed.Length == 1
            ? trimmed.ToUpperInvariant()
            : Capitalize(trimmed));
        return Enum.TryParse<KeyCode>(candidate, ignoreCase: true, out var code)
            ? code
            : KeyCode.VcA;
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        switch (e.Data.KeyCode)
        {
            case KeyCode.VcLeftControl or KeyCode.VcRightControl: _ctrlDown = false; break;
            case KeyCode.VcLeftAlt or KeyCode.VcRightAlt: _altDown = false; break;
            case KeyCode.VcLeftShift or KeyCode.VcRightShift: _shiftDown = false; break;
        }
    }

    private async void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        switch (e.Data.KeyCode)
        {
            case KeyCode.VcLeftControl or KeyCode.VcRightControl: _ctrlDown = true; return;
            case KeyCode.VcLeftAlt or KeyCode.VcRightAlt: _altDown = true; return;
            case KeyCode.VcLeftShift or KeyCode.VcRightShift: _shiftDown = true; return;
        }

        var captureMatch = e.Data.KeyCode == _triggerKey &&
            _ctrlDown == _needCtrl && _altDown == _needAlt && _shiftDown == _needShift;
        var listMatch = e.Data.KeyCode == _listKey &&
            _ctrlDown == _listCtrl && _altDown == _listAlt && _shiftDown == _listShift;

        if (!captureMatch && !listMatch) return;

        // Only act while PoE is focused — otherwise let the keys pass through to whatever app
        // the user is in (don't suppress, e.g. so Ctrl+S still saves in a text editor).
        if (ShouldHandle is not null && !ShouldHandle()) return;

        if (captureMatch)
        {
            e.SuppressEvent = true; // don't leak the trigger into the game
            try { await CaptureAsync(); } catch { /* never crash the hook thread */ }
            return;
        }

        // Show-custom-filters combo (no Ctrl+C; just open the list).
        e.SuppressEvent = true;
        try { if (ShowRulesRequested is not null) await ShowRulesRequested.Invoke(); }
        catch { /* never crash the hook thread */ }
    }

    private async Task CaptureAsync()
    {
        // PoE only writes to the clipboard when an item is actually under the cursor. We snapshot the
        // clipboard sequence number, fire Ctrl+C, and only proceed if it changed — otherwise nothing
        // was copied (no item hovered) and we must NOT open the popup on stale clipboard contents.
        var before = GetClipboardSequenceNumber();

        _simulator.SimulateKeyPress(KeyCode.VcLeftControl);
        _simulator.SimulateKeyPress(KeyCode.VcC);
        _simulator.SimulateKeyRelease(KeyCode.VcC);
        _simulator.SimulateKeyRelease(KeyCode.VcLeftControl);

        await Task.Delay(80);

        if (GetClipboardSequenceNumber() == before)
        {
            // Clipboard untouched -> no item under the cursor: offer the manual picker.
            if (NoItemUnderCursor is not null)
                await NoItemUnderCursor.Invoke();
            return;
        }

        if (ItemCaptureRequested is not null)
            await ItemCaptureRequested.Invoke();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    public void Dispose()
    {
        _hook.KeyPressed -= OnKeyPressed;
        _hook.KeyReleased -= OnKeyReleased;
        _hook.Dispose();
    }
}
