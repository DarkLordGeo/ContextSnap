using ContextSnap.Platform;

namespace ContextSnap.Tests;

public class NotificationAreaIconTests
{
    private const int NinSelect = 0x400, NinKeySelect = 0x401, WmMouseMove = 0x200;

    [Theory]
    [InlineData(NinSelect, TrayCommand.Open)]
    [InlineData(NinKeySelect, TrayCommand.Open)]
    [InlineData(WmMouseMove, TrayCommand.None)]
    public void ClickAndKeyboardSelection_OpenTheWindow(int notification, TrayCommand expected)
    {
        using var icon = new NotificationAreaIcon(0, "test");

        var command = icon.HandleMessage(NotificationAreaIcon.CallbackMessage, 0, notification, "Capture", out var handled);

        Assert.True(handled);
        Assert.Equal(expected, command);
    }

    [Fact]
    public void OtherMessages_AreNotHandled()
    {
        using var icon = new NotificationAreaIcon(0, "test");

        Assert.Equal(TrayCommand.None, icon.HandleMessage(0x0010, 0, 0, "Capture", out var handled)); // WM_CLOSE
        Assert.False(handled);
        Assert.False(icon.IsAdded);
    }
}
