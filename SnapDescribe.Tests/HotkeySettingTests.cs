using SnapDescribe.App.Models;
using Xunit;

namespace SnapDescribe.Tests;

public class HotkeySettingTests
{
    [Fact]
    public void TryParse_ReturnsBinding_ForValidShortcut()
    {
        var success = HotkeySetting.TryParse("Ctrl+Alt+T", out var binding);

        Assert.True(success);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, binding.Modifiers);
        Assert.Equal((uint)'T', binding.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("UnknownKey")]
    public void TryParse_ReturnsFalse_ForInvalidShortcut(string shortcut)
    {
        var success = HotkeySetting.TryParse(shortcut, out var binding);

        Assert.False(success);
        Assert.Equal(default, binding);
    }
    [Theory]
    [InlineData("Shift+F5", HotkeyModifiers.Shift, 0x74)]
    [InlineData("Alt+1", HotkeyModifiers.Alt, (uint)'1')]
    [InlineData("Win+Space", HotkeyModifiers.Win, 0x20)]
    public void TryParse_SupportsSpecialKeys(string shortcut, HotkeyModifiers expectedModifiers, uint expectedKey)
    {
        var success = HotkeySetting.TryParse(shortcut, out var binding);

        Assert.True(success);
        Assert.Equal(expectedModifiers, binding.Modifiers);
        Assert.Equal(expectedKey, binding.VirtualKey);
    }
}
