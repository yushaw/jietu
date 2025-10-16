using SnapDescribe.App.Models;
using SnapDescribe.App.Services;
using SnapDescribe.App.ViewModels;
using Xunit;

namespace SnapDescribe.Tests;

public class AgentSettingsViewModelTests
{
    [Fact]
    public void Constructor_AllowsProfilesWithoutToolCollections()
    {
        var settings = new AppSettings
        {
            Agent = new AgentSettings
            {
                IsEnabled = true,
                Profiles =
                {
                    null!,
                    new AgentProfile
                    {
                        Name = "Legacy Agent",
                        Tools = null!
                    }
                }
            }
        };

        var settingsService = new SettingsServiceStub(settings);
        var localization = new LocalizationService();

        var viewModel = new AgentSettingsViewModel(settingsService, localization);

        Assert.NotNull(viewModel.SelectedProfile);
        Assert.Empty(viewModel.SelectedProfile!.Tools);
    }

    private sealed class SettingsServiceStub : SettingsService
    {
        public SettingsServiceStub(AppSettings initial)
            : base(Path.GetTempPath(), Path.GetTempPath())
        {
            Replace(initial);
        }
    }
}
