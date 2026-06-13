using System.Drawing;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Photino.NET;
using PoeHotFilter.Core;
using PoeHotFilter.Core.Filter;
using PoeHotFilter.Core.Models;
using PoeHotFilter.Core.Storage;
using PoeHotFilter.Photino.Services;

namespace PoeHotFilter.Photino;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static PhotinoWindow _window = null!;
    private static LiveFilterService _service = null!;
    private static WindowsGameController _game = null!;
    private static HotkeyService _hotkeys = null!;
    private static ActiveFilterWatcher? _watcher;
    private static SettingsStore _settingsStore = null!;
    private static AppSettings _settings = null!;
    private static NotifyIcon _tray = null!;
    private static SynchronizationContext? _trayCtx;
    private static volatile bool _windowReady;
    private static bool _rulesDirty; // a rule was deleted in the list; reload on close

    private const int PopupWidth = 480;
    private const int PopupHeight = 820;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PoeHotFilter", "log.txt");

    private static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private static void Balloon(string text)
    {
        if (_trayCtx is null) return;
        _trayCtx.Post(_ =>
        {
            try { _tray?.ShowBalloonTip(1500, "PoeHotFilter", text, ToolTipIcon.None); }
            catch (Exception ex) { Log($"Balloon failed: {ex.Message}"); }
        }, null);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log($"UNHANDLED: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { Log($"TASK UNHANDLED: {e.Exception}"); e.SetObserved(); };
        Log("=== App starting ===");

        _settingsStore = SettingsStore.Default();
        _settings = _settingsStore.Load();

        var poeFolder = _settings.PoeFolderOverride ?? PoePaths.DefaultFilterFolder()
                        ?? Environment.CurrentDirectory;

        var store = RuleStore.Default();
        var fileManager = new FilterFileManager(poeFolder);
        _game = new WindowsGameController();
        WindowsGameController.Logger = Log;
        _service = new LiveFilterService(store, fileManager, _game);

        _hotkeys = new HotkeyService();
        _hotkeys.UpdateHotkey(_settings.Hotkey);
        _hotkeys.UpdateListHotkey(_settings.ListHotkey);
        _hotkeys.ItemCaptureRequested += OnHotkeyCaptureAsync;
        _hotkeys.NoItemUnderCursor += OnManualPickAsync;
        _hotkeys.ShowRulesRequested += OnShowRulesAsync;
        // Only fire hotkeys while PoE is focused; otherwise let the keys pass through to other apps.
        _hotkeys.ShouldHandle = () => _game.IsGameForeground();
        _hotkeys.Start();
        Log("Hotkey hook started");

        StartTrayThread();

        // Small popup window (NOT fullscreen). Created at its real on-screen position (WebView2 won't
        // paint correctly when created off-screen), then hidden via WindowCreated handler.
        var initX = Math.Max(0, (GetScreenWidth() - PopupWidth) / 2);
        var initY = Math.Max(0, (GetScreenHeight() - PopupHeight) / 2);
        _window = new PhotinoWindow()
            .SetTitle("PoeHotFilter")
            .SetUseOsDefaultSize(false)
            .SetSize(PopupWidth, PopupHeight)
            .SetUseOsDefaultLocation(false)
            .SetLeft(initX).SetTop(initY)
            .SetChromeless(true)
            .SetTopMost(true)
            .SetResizable(false)
            .SetDevToolsEnabled(true)  // F12 to open DevTools when popup is shown
            .RegisterWindowCreatedHandler((_, _) =>
            {
                var h = _window?.WindowHandle ?? IntPtr.Zero;
                Log($"WindowCreated event fired. handle=0x{h.ToInt64():x}");
                // Apply WS_EX_TOOLWINDOW immediately (no taskbar/AltTab) but DO NOT hide yet —
                // WebView2 needs to stay visible for its first paint, otherwise it renders white forever.
                if (h != IntPtr.Zero) DetachFromTaskbarOnly(h);
            })
            .RegisterFocusOutHandler(OnFocusOut)
            .RegisterWebMessageReceivedHandler(OnWebMessage)
            .Load(ResolveIndexHtml());

        Log("Photino window built");

        // Resolve the active filter and prepare (without injecting yet).
        InitializeFilterAsync(poeFolder).GetAwaiter().GetResult();

        _window.WaitForClose();

        _hotkeys.Dispose();
        _watcher?.Dispose();
        _tray.Dispose();
    }

    private static void StartTrayThread()
    {
        var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                _trayCtx = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(_trayCtx);
                _tray = CreateTray();
                Log("Tray ready");
                ready.Set();
                Application.Run();
                Log("Tray Application.Run returned");
            }
            catch (Exception ex)
            {
                Log($"Tray thread crashed: {ex}");
                ready.Set();
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        ready.Wait();
    }

    private static string ResolveIndexHtml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        Log($"Loading HTML: {path} (exists={File.Exists(path)})");
        return path;
    }

    private static NotifyIcon CreateTray()
    {
        var menu = new ContextMenuStrip();
        var hotkeyLabel = _settings.Hotkey?.ToDisplayString() ?? "Ctrl+A";
        var header = (ToolStripMenuItem)menu.Items.Add($"PoeHotFilter — {hotkeyLabel} on a hovered item (or empty ground to pick one)");
        header.Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = IsStartupEnabled() };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add("Quit", null, (_, _) => Quit());

        return new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "PoeHotFilter",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    // ---- "Start with Windows": HKCU Run key (per-user, no admin needed) ----
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "PoeHotFilter";

    private static bool IsStartupEnabled()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return k?.GetValue(RunValueName) is string;
        }
        catch { return false; }
    }

    private static void SetStartup(bool enable)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enable)
            {
                var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PoeHotFilter.exe");
                k.SetValue(RunValueName, $"\"{exe}\"");
            }
            else
            {
                k.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            Log($"Startup with Windows {(enable ? "enabled" : "disabled")}");
        }
        catch (Exception ex) { Log($"SetStartup failed: {ex.Message}"); }
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var p = Path.Combine(AppContext.BaseDirectory, "wwwroot", "icon.ico");
            if (File.Exists(p)) return new Icon(p);
        }
        catch (Exception ex) { Log($"icon load failed: {ex.Message}"); }
        return SystemIcons.Application;
    }

    private static void Quit()
    {
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        try { _hotkeys.Dispose(); } catch { }
        try { _watcher?.Dispose(); } catch { }
        Environment.Exit(0);
    }

    private static async Task InitializeFilterAsync(string poeFolder)
    {
        var active = PoeConfigReader.GetActiveFilterPath(poeFolder)
                     ?? _settings.ActiveFilterPath;
        if (active is null || !File.Exists(active))
        {
            active = PoePaths.ListUserFilters(poeFolder, "_PoeHotFilter.filter")
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        if (active is null) return;

        _settings.ActiveFilterPath = active;
        _settingsStore.Save(_settings);
        await _service.InitializeAsync(active);

        // Watch for in-game filter switches / re-exports.
        _watcher = new ActiveFilterWatcher(poeFolder);
        _watcher.OnActiveFilterChanged += async p => { await _service.RetargetActiveFilterAsync(p); PushPresets(); };
        _watcher.OnActiveFilterEdited += async p => { await _service.RetargetActiveFilterAsync(p); PushPresets(); };
        _watcher.Start();
    }

    // ---- hotkey → capture → tell the UI to open the popup ----
    private static async Task OnHotkeyCaptureAsync()
    {
        Log("Hotkey fired");
        try
        {
            if (!_windowReady) { Log("  -> window not ready, skipping"); return; }

            var item = _service.CaptureHoveredItem();
            Log($"  -> captured: {(item is null ? "null" : $"{item.BaseType} | class={item.ItemClass} | ilvl={item.ItemLevel}")}");
            if (item is null)
            {
                Balloon("No item under cursor.");
                return;
            }

            ShowOverlay();
            Log("  -> ShowOverlay called");

            var msg = new CapturedItemMsg
            {
                BaseType = item.BaseType,
                ItemClass = item.ItemClass,
                Name = item.Name,
                Rarity = item.Rarity,
                ItemLevel = item.ItemLevel,
                Stackable = item.IsStackable,
                IsGem = item.IsGem,
                Quality = item.Quality,
                GemLevel = item.GemLevel,
                Current = ToStyleDto(_service.CurrentStyleFor(item))
            };
            Send("itemCaptured", msg);
            PushPresets();
        }
        catch (Exception ex)
        {
            Log($"  -> EXCEPTION: {ex}");
        }
        await Task.CompletedTask;
    }

    // ---- hotkey on empty ground → open the popup with the manual item picker ----
    private static async Task OnManualPickAsync()
    {
        Log("Hotkey fired with no item under cursor -> manual pick");
        try
        {
            if (!_windowReady) { Log("  -> window not ready, skipping"); return; }
            ShowOverlay();
            Send("manualPick", new { });
            PushPresets();
        }
        catch (Exception ex) { Log($"  -> EXCEPTION: {ex}"); }
        await Task.CompletedTask;
    }

    private static CurrentStyleDto ToStyleDto(StyleMatch cur)
    {
        static int[] Rgba(FilterColor c) => new[] { (int)c.R, c.G, c.B, c.A };
        return new CurrentStyleDto
        {
            Text = Rgba(cur.Text),
            Border = Rgba(cur.Border),
            Background = Rgba(cur.Background),
            FontSize = cur.FontSize,
            IconShape = cur.IconShape.ToString(),
            IconColor = cur.IconColor.ToString(),
            IconSize = cur.IconSize,
            Hidden = cur.Hidden,
            Source = cur.Source
        };
    }

    // ---- hotkey → show the list of custom filters the user has added ----
    private static async Task OnShowRulesAsync()
    {
        Log("Show-rules hotkey fired");
        try
        {
            if (!_windowReady) return;
            _rulesDirty = false;
            ShowOverlay();
            Send("showRules", BuildRulesPayload());
        }
        catch (Exception ex) { Log($"  -> EXCEPTION: {ex}"); }
        await Task.CompletedTask;
    }

    private static object BuildRulesPayload() =>
        _service.Rules.Select(r => new
        {
            id = r.Id.ToString(),
            label = r.DisplayLabel,
            baseType = r.BaseType,
            itemClass = r.ItemClass ?? "",
            stackable = r.Stackable,
            isGem = r.IsGem,
            rarity = r.Rarity.ToString(),
            corrupted = r.Corrupted.ToString(),
            action = r.Action.ToString(),
            stackMin = r.StackMin,
            stackMax = r.StackMax,
            ilvlMode = r.IlvlMode.ToString(),
            ilvlValue = r.IlvlValue,
            qualityMode = r.QualityMode.ToString(),
            qualityValue = r.QualityValue,
            gemLevelMode = r.GemLevelMode.ToString(),
            gemLevelValue = r.GemLevelValue,
            enchantNode = r.EnchantNode ?? "",
            passiveNumMode = r.PassiveNumMode.ToString(),
            passiveNumValue = r.PassiveNumValue,
            text = new[] { (int)r.TextColor.R, r.TextColor.G, r.TextColor.B, r.TextColor.A },
            border = new[] { (int)r.BorderColor.R, r.BorderColor.G, r.BorderColor.B, r.BorderColor.A },
            bg = new[] { (int)r.BackgroundColor.R, r.BackgroundColor.G, r.BackgroundColor.B, r.BackgroundColor.A },
            fontSize = r.FontSize,
            iconShape = r.IconShape.ToString(),
            iconColor = r.IconColor.ToString(),
            iconSize = r.IconSize,
            alertSound = r.AlertSound,
            alertVolume = r.AlertVolume
        }).ToArray();

    // ---- messages from the web UI ----
    private static async void OnWebMessage(object? sender, string raw)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<WebMessage>(raw, Json);
            if (msg is null) return;
            Log($"Web msg in: {msg.Type}");

            switch (msg.Type)
            {
                case "ready":
                    // First paint confirmed by JS. Now safe to hide the window without breaking WebView2 rendering.
                    var h = _window?.WindowHandle ?? IntPtr.Zero;
                    if (h != IntPtr.Zero && !_windowReady)
                    {
                        ShowWindow(h, SW_HIDE);
                        _windowReady = true;
                        Log("Window hidden after ready");
                    }
                    PushPresets();
                    break;

                case "addRule":
                    await HandleAddRuleAsync(msg.Payload);
                    break;

                case "updateRule":
                    await HandleUpdateRuleAsync(msg.Payload);
                    break;

                case "confirmInjection": // {approved:bool}
                    var approved = msg.Payload.TryGetProperty("approved", out var a) && a.GetBoolean();
                    _rulesDirty = false;
                    if (approved)
                    {
                        await _service.InjectImportAsync(reload: false); // write Import line, reload after hide
                        Send("ruleAdded", new { label = "" });
                        await HideThenReloadAsync();
                    }
                    else
                    {
                        Send("ruleAdded", new { label = "" }); // just close the popup
                    }
                    break;

                case "cancel":
                    // popup dismissed; nothing to do server-side
                    break;

                case "queryStyle": // manual pick: {baseType, itemClass, stackable, isGem} → current look under the active filter
                    var probe = new ParsedItem
                    {
                        BaseType = msg.Payload.TryGetProperty("baseType", out var bt) ? bt.GetString() ?? "" : "",
                        ItemClass = msg.Payload.TryGetProperty("itemClass", out var ic) ? ic.GetString() ?? "" : "",
                        IsStackable = msg.Payload.TryGetProperty("stackable", out var st) && st.GetBoolean(),
                        IsGem = msg.Payload.TryGetProperty("isGem", out var ig) && ig.GetBoolean()
                    };
                    Send("itemStyle", ToStyleDto(_service.CurrentStyleFor(probe)));
                    break;

                case "deleteRule": // {id:"guid"}
                    if (msg.Payload.TryGetProperty("id", out var idEl) &&
                        Guid.TryParse(idEl.GetString(), out var ruleId))
                    {
                        await _service.DeleteRuleAsync(ruleId, reload: false);
                        _rulesDirty = true;
                        Send("rules", BuildRulesPayload()); // refresh the list in place
                    }
                    break;

                case "closeRules":
                    // Leaving the management list. If our managed Import isn't in the active filter,
                    // ask to add it (otherwise none of these rules actually apply in-game).
                    if (_service.Rules.Count > 0 && !_service.IsImportInjected)
                    {
                        Send("needInjection", new { filter = Path.GetFileName(_service.ActiveFilterPath ?? "") });
                    }
                    else if (_rulesDirty)
                    {
                        _rulesDirty = false;
                        await HideThenReloadAsync();
                    }
                    else
                    {
                        HideOverlay();
                    }
                    break;

                case "popupClosed":
                    HideOverlay();
                    break;
            }
        }
        catch (Exception ex)
        {
            Send("error", new { message = ex.Message });
        }
    }

    private static async Task HandleAddRuleAsync(JsonElement payloadEl)
    {
        var p = payloadEl.Deserialize<AddRulePayload>(Json)!;

        var item = new ParsedItem
        {
            BaseType = p.BaseType,
            ItemClass = p.ItemClass ?? "",
            Rarity = (p.Rarity is "Any" or "AnyNonUnique") ? null : p.Rarity,
            IsStackable = p.Stackable,
            IsGem = p.IsGem
        };

        // Write the rule + regenerate the managed file, but DON'T reload yet: our overlay still holds
        // foreground, so SendInput to PoE would be blocked by Windows' focus-stealing prevention.
        await _service.AddRuleAsync(
            item,
            Enum.Parse<RarityFilter>(p.Rarity),
            Enum.Parse<IlvlMatchMode>(p.IlvlMode),
            p.IlvlValue,
            ToColor(p.TextColor), ToColor(p.BorderColor), ToColor(p.BackgroundColor),
            p.FontSize,
            Enum.Parse<IconShape>(p.IconShape),
            Enum.Parse<IconColor>(p.IconColor),
            p.IconSize,
            Enum.Parse<CorruptedMode>(p.Corrupted),
            p.AlertSound,
            p.AlertVolume,
            Enum.Parse<BlockAction>(p.Action),
            p.StackMin,
            p.StackMax,
            Enum.Parse<IlvlMatchMode>(p.QualityMode),
            p.QualityValue,
            Enum.Parse<IlvlMatchMode>(p.GemLevelMode),
            p.GemLevelValue,
            p.EnchantNode,
            Enum.Parse<IlvlMatchMode>(p.PassiveNumMode),
            p.PassiveNumValue,
            reload: false);

        // If our Import isn't present yet, ask the UI to confirm before we touch the active filter.
        // The reload happens in the confirmInjection handler (after the overlay hides).
        if (!_service.IsImportInjected)
        {
            Send("needInjection", new { filter = Path.GetFileName(_service.ActiveFilterPath ?? "") });
            return;
        }

        // Import already present: close the popup, let PoE regain focus, then reload.
        Send("ruleAdded", new { label = item.Name ?? item.BaseType });
        await HideThenReloadAsync();
    }

    /// <summary>Applies edits to an existing rule (from the "edit a custom filter" form), stays in the list.</summary>
    private static async Task HandleUpdateRuleAsync(JsonElement payloadEl)
    {
        var p = payloadEl.Deserialize<AddRulePayload>(Json)!;
        if (!Guid.TryParse(p.Id, out var id)) return;

        var rule = _service.Rules.FirstOrDefault(r => r.Id == id);
        if (rule is null) return;

        rule.Rarity = Enum.Parse<RarityFilter>(p.Rarity);
        rule.Corrupted = Enum.Parse<CorruptedMode>(p.Corrupted);
        rule.Action = Enum.Parse<BlockAction>(p.Action);
        rule.StackMin = p.StackMin;
        rule.StackMax = p.StackMax;
        rule.IlvlMode = Enum.Parse<IlvlMatchMode>(p.IlvlMode);
        rule.IlvlValue = p.IlvlValue;
        rule.QualityMode = Enum.Parse<IlvlMatchMode>(p.QualityMode);
        rule.QualityValue = p.QualityValue;
        rule.GemLevelMode = Enum.Parse<IlvlMatchMode>(p.GemLevelMode);
        rule.GemLevelValue = p.GemLevelValue;
        rule.EnchantNode = string.IsNullOrWhiteSpace(p.EnchantNode) ? null : p.EnchantNode;
        rule.PassiveNumMode = Enum.Parse<IlvlMatchMode>(p.PassiveNumMode);
        rule.PassiveNumValue = p.PassiveNumValue;
        rule.TextColor = ToColor(p.TextColor);
        rule.BorderColor = ToColor(p.BorderColor);
        rule.BackgroundColor = ToColor(p.BackgroundColor);
        rule.FontSize = p.FontSize;
        rule.IconShape = Enum.Parse<IconShape>(p.IconShape);
        rule.IconColor = Enum.Parse<IconColor>(p.IconColor);
        rule.IconSize = p.IconSize;
        rule.AlertSound = p.AlertSound;
        rule.AlertVolume = p.AlertVolume;

        await _service.UpdateRuleAsync(rule, reload: false);
        _rulesDirty = true;                       // applied in-game when the list is closed
        Send("showRules", BuildRulesPayload());   // back to the (updated) list
    }

    /// <summary>Hide our overlay so PoE regains foreground, then send /reloaditemfilter to it.</summary>
    private static async Task HideThenReloadAsync()
    {
        HideOverlay();
        await Task.Delay(50); // let PoE settle back into the foreground before we SendInput to it
        if (!_settings.AutoReload) { Log("Reload skipped (AutoReload off)"); return; }
        var ok = await _game.ReloadFilterAsync();
        Log($"Reload sent: {ok}");
    }

    private static void PushPresets()
    {
        // NOTE: cast to int. A byte[] would be serialized by System.Text.Json as a base64 string,
        // not a JSON number array, which breaks the JS side (p.text[i] / toHex).
        var presets = _service.Presets.Select(p => new
        {
            name = p.Name,
            text = new[] { (int)p.TextColor.R, p.TextColor.G, p.TextColor.B, p.TextColor.A },
            border = new[] { (int)p.BorderColor.R, p.BorderColor.G, p.BorderColor.B, p.BorderColor.A },
            bg = new[] { (int)p.BackgroundColor.R, p.BackgroundColor.G, p.BackgroundColor.B, p.BackgroundColor.A },
            fontSize = p.FontSize,
            iconShape = p.IconShape.ToString(),
            iconColor = p.IconColor.ToString(),
            iconSize = p.IconSize
        });
        Send("presets", presets);
    }

    private static FilterColor ToColor(int[] c) =>
        new((byte)c[0], (byte)c[1], (byte)c[2], (byte)(c.Length > 3 ? c[3] : 255));

    private static void Send(string type, object payload)
    {
        var json = JsonSerializer.Serialize(new { type, payload }, Json);
        // Photino/WebView2 on Windows corrupts C#->JS strings (UTF-8 bytes read as UTF-16) and
        // appends trailing buffer garbage. We frame as "<utf8ByteLength>:<json>" so the JS side can
        // byte-unpack and slice exactly the real content. See app.js handleMessage().
        var byteLen = Encoding.UTF8.GetByteCount(json);
        _window.SendWebMessage($"{byteLen}:{json}");
    }

    // ---- Win32: screen size + show/hide + remove from taskbar ----
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private static int GetScreenWidth() => GetSystemMetrics(0);  // SM_CXSCREEN
    private static int GetScreenHeight() => GetSystemMetrics(1); // SM_CYSCREEN

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOZORDER   = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW  = 0x00040000;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private static IntPtr GetExStyle(IntPtr h) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(h, GWL_EXSTYLE) : new IntPtr(GetWindowLong32(h, GWL_EXSTYLE));
    private static void SetExStyle(IntPtr h, long ex)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWL_EXSTYLE, new IntPtr(ex));
        else SetWindowLong32(h, GWL_EXSTYLE, (int)ex);
    }

    private static void DetachFromTaskbarOnly(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var ex = (long)GetExStyle(hwnd);
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetExStyle(hwnd, ex);
    }

    private static void ShowOverlay()
    {
        var h = _window.WindowHandle;
        if (h == IntPtr.Zero) return;
        // Re-center each show in case screen geometry changed.
        var x = Math.Max(0, (GetScreenWidth() - PopupWidth) / 2);
        var y = Math.Max(0, (GetScreenHeight() - PopupHeight) / 2);
        SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        _shownAtTick = Environment.TickCount64;
        ShowWindow(h, SW_SHOW);
        ForceForeground(h);
    }

    /// <summary>
    /// Reliably brings our window to the foreground. Windows blocks SetForegroundWindow from a
    /// background process (so the rules hotkey, which doesn't inject input, would otherwise fail);
    /// attaching our input queue to the current foreground thread lifts that restriction.
    /// </summary>
    private static void ForceForeground(IntPtr h)
    {
        var fg = GetForegroundWindow();
        uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        uint thisThread = GetCurrentThreadId();
        bool attached = false;
        if (fgThread != 0 && fgThread != thisThread)
            attached = AttachThreadInput(thisThread, fgThread, true);
        try
        {
            BringWindowToTop(h);
            SetForegroundWindow(h);
        }
        finally
        {
            if (attached) AttachThreadInput(thisThread, fgThread, false);
        }
    }

    private static void HideOverlay()
    {
        var h = _window.WindowHandle;
        if (h == IntPtr.Zero) return;
        ShowWindow(h, SW_HIDE);
    }

    private static long _shownAtTick;

    /// <summary>
    /// Our popup lost focus. If focus went to ANOTHER process (the user clicked PoE / the desktop),
    /// dismiss the popup. Focus moving to our OWN child windows (native colour picker, &lt;select&gt;
    /// dropdown) is ignored so editing isn't interrupted.
    /// </summary>
    private static void OnFocusOut(object? sender, EventArgs e)
    {
        if (!_windowReady) return;
        // Ignore the focus flicker that can happen right as we show + force-foreground the window.
        if (Environment.TickCount64 - _shownAtTick < 300) return;

        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) { HideOverlay(); return; }
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid != (uint)Environment.ProcessId) HideOverlay();
    }
}
