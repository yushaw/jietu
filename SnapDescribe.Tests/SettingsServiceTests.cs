using System;
using System.Globalization;
using System.IO;
using SnapDescribe.App.Models;
using SnapDescribe.App.Services;
using Xunit;

namespace SnapDescribe.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _appData;
    private readonly string _pictures;
    private readonly CultureInfo _originalCulture;

    public SettingsServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SnapDescribeTests", Guid.NewGuid().ToString("N"));
        _appData = Path.Combine(_root, "AppData");
        _pictures = Path.Combine(_root, "Pictures");
        Directory.CreateDirectory(_appData);
        Directory.CreateDirectory(_pictures);

        _originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
    }

    [Fact]
    public void Constructor_SeedsDefaultsAndCreatesFiles()
    {
        var service = new SettingsService(_appData, _pictures);
        var settings = service.Current;

        Assert.Equal("en-US", settings.Language);
        Assert.True(settings.HasSeededDefaultPromptRules);
        Assert.Contains(settings.PromptRules, rule => rule.ProcessName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase));

        var expectedOutputDirectory = Path.Combine(_pictures, "SnapDescribe");
        Assert.True(Directory.Exists(expectedOutputDirectory));
        Assert.Equal(expectedOutputDirectory, settings.OutputDirectory);

        var settingsFile = Path.Combine(_appData, "SnapDescribe", "settings.json");
        Assert.True(File.Exists(settingsFile));
    }

    [Fact]
    public void EnsureOutputDirectory_CreatesCustomDirectory()
    {
        var service = new SettingsService(_appData, _pictures);
        var customDirectory = Path.Combine(_pictures, "CustomOutput");
        if (Directory.Exists(customDirectory))
        {
            Directory.Delete(customDirectory, recursive: true);
        }

        service.Update(settings => settings.OutputDirectory = customDirectory);
        service.EnsureOutputDirectory();

        Assert.True(Directory.Exists(customDirectory));
    }

    [Fact]
    public void Update_LanguageSwitchesDefaultPrompt()
    {
        var service = new SettingsService(_appData, _pictures);
        service.Update(settings =>
        {
            settings.DefaultPrompt = "Describe the key information shown in this screenshot and provide a concise answer or actionable suggestion.";
            settings.Language = "zh-CN";
        });

        Assert.Equal("zh-CN", service.Current.Language);
        Assert.Equal("请用简洁的语言描述这张截图的关键信息，并给出一个可能的解答或操作建议。", service.Current.DefaultPrompt);
    }

    [Fact]
    public void Update_DoesNotDuplicateDefaultRules()
    {
        var service = new SettingsService(_appData, _pictures);
        var initialCount = service.Current.PromptRules.Count;

        service.Update(settings => { /* no-op */ });

        Assert.Equal(initialCount, service.Current.PromptRules.Count);
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore clean-up failures
        }
    }
}
