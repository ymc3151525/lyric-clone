namespace LyricClone.Core.Settings;

/// <summary>A global shortcut: modifiers + a virtual-key code (Win32 VK_*). Empty / unparsable means "off".</summary>
public readonly record struct HotkeyChord(bool Ctrl, bool Alt, bool Shift, bool Win, int VirtualKey)
{
    public bool IsEmpty => VirtualKey == 0;

    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        bool ctrl = false, alt = false, shift = false, win = false;
        int vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw.ToLowerInvariant();
            if (p is "ctrl" or "control")
            {
                ctrl = true;
            }
            else if (p is "alt")
            {
                alt = true;
            }
            else if (p is "shift")
            {
                shift = true;
            }
            else if (p is "win" or "windows" or "meta")
            {
                win = true;
            }
            else if (vk != 0)
            {
                return false;
            }
            else if (p.Length == 1 && char.IsLetterOrDigit(p[0]))
            {
                vk = char.ToUpperInvariant(p[0]);
            }
            else if (p.StartsWith("f") && int.TryParse(p[1..], out int fn) && fn is >= 1 and <= 24)
            {
                vk = 0x70 + fn - 1;
            }
            else
            {
                return false;
            }
        }

        if (vk == 0 || !(ctrl || alt || win))
        {
            return false;
        }

        chord = new HotkeyChord(ctrl, alt, shift, win, vk);
        return true;
    }

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "";
        }

        var parts = new List<string>();
        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Win)
        {
            parts.Add("Win");
        }

        parts.Add(VirtualKey is >= 0x70 and <= 0x87 ? $"F{VirtualKey - 0x70 + 1}" : ((char)VirtualKey).ToString());
        return string.Join('+', parts);
    }
}
