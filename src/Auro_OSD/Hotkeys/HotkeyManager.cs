using System.Runtime.InteropServices;

namespace Auro_OSD.Hotkeys;

public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;

    [Flags]
    private enum Modifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
        NoRepeat = 0x4000
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Dictionary<int, Action> _actions = new();
    private readonly List<int> _registered = new();
    private int _nextId = 0xA000;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams
        {
            Caption = "Auro_OSD_HotkeyWindow",
            X = -32000,
            Y = -32000,
            Width = 1,
            Height = 1,
            Style = 0
        });
    }

    public void Clear()
    {
        foreach (var id in _registered)
            UnregisterHotKey(Handle, id);
        _registered.Clear();
        _actions.Clear();
    }

    public bool Register(string hotkey, Action action)
    {
        if (!TryParse(hotkey, out var mods, out var vk))
            return false;

        var id = _nextId++;
        if (!RegisterHotKey(Handle, id, (uint)mods | (uint)Modifiers.NoRepeat, vk))
            return false;

        _registered.Add(id);
        _actions[id] = action;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            var id = m.WParam.ToInt32();
            if (_actions.TryGetValue(id, out var action))
                action();
            return;
        }
        base.WndProc(ref m);
    }

    private static bool TryParse(string? hotkey, out Modifiers modifiers, out uint vk)
    {
        modifiers = Modifiers.None;
        vk = 0;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        foreach (var raw in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = raw.Trim();
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control": modifiers |= Modifiers.Control; continue;
                case "alt": modifiers |= Modifiers.Alt; continue;
                case "shift": modifiers |= Modifiers.Shift; continue;
                case "win": modifiers |= Modifiers.Win; continue;
            }

            if (!TryParseKey(token, out vk)) return false;
        }

        return vk != 0 && modifiers != Modifiers.None;
    }

    private static bool TryParseKey(string token, out uint vk)
    {
        vk = 0;
        var t = token.ToUpperInvariant();

        if (t.Length == 1)
        {
            var c = t[0];
            if (c >= 'A' && c <= 'Z') { vk = c; return true; }
            if (c >= '0' && c <= '9') { vk = (uint)('0' + (c - '0')); return true; }
        }

        if (t.Length is 2 or 3 && t[0] == 'F' && int.TryParse(t[1..], out var n) && n is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + n - 1);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        Clear();
        DestroyHandle();
    }
}
