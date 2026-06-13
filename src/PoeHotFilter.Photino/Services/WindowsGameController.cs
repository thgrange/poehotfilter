using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PoeHotFilter.Core.Game;

namespace PoeHotFilter.Photino.Services;

/// <summary>
/// Win32 implementation. Finds the PoE window, brings it to the foreground, and types
/// the chat sequence:  Enter  →  "/reloaditemfilter"  →  Enter.
///
/// We use SendInput (not SendMessage) because PoE reads raw input and ignores synthetic
/// window messages. A short delay between keystrokes is needed or PoE drops characters.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGameController : IGameController
{
    // PoE's window class is "POEWindowClass" for the standalone/Steam client.
    private const string PoeWindowClass = "POEWindowClass";
    private const string ReloadCommand = "/reloaditemfilter";

    /// <summary>Optional diagnostic sink (wired to Program.Log).</summary>
    public static Action<string>? Logger;
    private static void L(string m) => Logger?.Invoke(m);

    public bool IsGameRunning() => FindPoeWindow() != IntPtr.Zero;

    public bool IsGameForeground()
    {
        var poe = FindPoeWindow();
        return poe != IntPtr.Zero && GetForegroundWindow() == poe;
    }

    public string? GetClipboardText()
    {
        // Avalonia's clipboard is async/UI-thread bound; for a quick synchronous read
        // off the hotkey thread we go straight to Win32. Must run on an STA thread.
        string? result = null;
        var t = new Thread(() =>
        {
            try
            {
                if (!OpenClipboard(IntPtr.Zero)) return;
                try
                {
                    IntPtr h = GetClipboardData(CF_UNICODETEXT);
                    if (h == IntPtr.Zero) return;
                    IntPtr ptr = GlobalLock(h);
                    if (ptr == IntPtr.Zero) return;
                    try { result = Marshal.PtrToStringUni(ptr); }
                    finally { GlobalUnlock(h); }
                }
                finally { CloseClipboard(); }
            }
            catch { /* clipboard busy — caller treats null as "no item" */ }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(500);
        return result;
    }

    public async Task<bool> ReloadFilterAsync(CancellationToken ct = default)
    {
        var hwnd = FindPoeWindow();
        if (hwnd == IntPtr.Zero) { L("  reload: PoE window not found"); return false; }

        if (!SetForegroundWindow(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
        await Task.Delay(40, ct); // brief settle into focus

        // Open chat (Enter as a scancode — PoE ignores VK-only Enter), type the whole command in a
        // single batched SendInput (atomic + fast + reliable; per-char sends are slow and drop chars),
        // then Enter to submit.
        TapScan(SC_RETURN);
        await Task.Delay(30, ct);
        var sent = TypeStringBatched(ReloadCommand);
        await Task.Delay(30, ct);
        TapScan(SC_RETURN);

        L($"  reload: sent {sent} char-events to PoE");
        return true;
    }

    private static IntPtr FindPoeWindow() => FindWindow(PoeWindowClass, null);

    // ---- keystroke helpers via SendInput ----

    /// <summary>Types the whole string in ONE SendInput call (atomic, ordered, fast).</summary>
    private static uint TypeStringBatched(string text)
    {
        var inputs = new INPUT[text.Length * 2];
        int i = 0;
        foreach (char c in text)
        {
            inputs[i++] = MakeUnicodeInput(c, keyUp: false);
            inputs[i++] = MakeUnicodeInput(c, keyUp: true);
        }
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static uint TapScan(ushort scan)
    {
        var down = MakeScanInput(scan, keyUp: false);
        var up = MakeScanInput(scan, keyUp: true);
        var inputs = new[] { down, up };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeScanInput(ushort scan, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = scan,
                dwFlags = KEYEVENTF_SCANCODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    private static INPUT MakeUnicodeInput(char c, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    // ---- P/Invoke ----

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const ushort SC_RETURN = 0x1C; // physical Enter scancode (layout-independent)
    private const uint CF_UNICODETEXT = 13;
    private const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion U; }

    // The union MUST be sized to its largest member (MOUSEINPUT). If it only holds KEYBDINPUT,
    // Marshal.SizeOf<INPUT>() is too small, SendInput gets a bad cbSize and rejects every event
    // (returns 0). This is why our SendInput never delivered anything.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
}
