using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace SnapDescribe.App.Models;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}

public readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public bool IsValid => VirtualKey != 0;
}

public class HotkeySetting
{
    private const string Separator = "+";

    public HotkeySetting()
    {
    }

    public HotkeySetting(string shortcut)
    {
        Shortcut = shortcut;
    }

    public string Shortcut { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayText => string.IsNullOrWhiteSpace(Shortcut) ? "未绑定" : Shortcut;

    public static HotkeySetting ParseOrDefault(string shortcut) => new(shortcut);

    public bool TryGetBinding(out HotkeyBinding binding) => TryParse(Shortcut, out binding);

    public static bool TryParse(string? shortcut, out HotkeyBinding binding)
    {
        binding = default;

        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return false;
        }

        var tokens = shortcut.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;

        foreach (var rawToken in tokens)
        {
            var token = rawToken.ToUpperInvariant();
            switch (token)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                case "NOREPEAT":
                    modifiers |= HotkeyModifiers.NoRepeat;
                    break;
                default:
                    virtualKey = ParseKey(token);
                    break;
            }
        }

        if (virtualKey == 0)
        {
            return false;
        }

        binding = new HotkeyBinding(modifiers, virtualKey);
        return true;
    }

    private static uint ParseKey(string token)
    {
        if (token.Length == 1)
        {
            var ch = token[0];
            if (ch is >= '0' and <= '9')
            {
                return (uint)ch;
            }

            if (ch is >= 'A' and <= 'Z')
            {
                return (uint)ch;
            }
        }

        if (token.StartsWith("F", StringComparison.Ordinal))
        {
            if (int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fnIndex) &&
                fnIndex is >= 1 and <= 24)
            {
                return (uint)(0x70 + (fnIndex - 1));
            }
        }

        return token.ToUpperInvariant() switch
        {
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" or "SPACEBAR" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "PRINTSCREEN" => 0x2C,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PRIOR" => 0x21,
            "PAGEDOWN" or "NEXT" => 0x22,
            "UP" or "UPARROW" => 0x26,
            "DOWN" or "DOWNARROW" => 0x28,
            "LEFT" or "LEFTARROW" => 0x25,
            "RIGHT" or "RIGHTARROW" => 0x27,
            _ => 0
        };
    }
}
