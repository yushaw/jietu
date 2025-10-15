using SnapDescribe.App.Services;
using Xunit;

namespace SnapDescribe.Tests;

public class StartupRegistrationServiceTests
{
    [Fact]
    public void IsEnabled_ReturnsFalse_OnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new StartupRegistrationService();
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void Apply_DoesNotThrow_OnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new StartupRegistrationService();
        service.Apply(true);
        service.Apply(false);
    }
}
