using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using SnapDescribe.App.Models;
using SnapDescribe.App.Services;
using Xunit;

namespace SnapDescribe.Tests;

public class ChatMessageTests
{
    [Fact]
    public void DisplayRole_UsesLocalization()
    {
        var localization = new LocalizationService();
        SeedLocalization(localization);
        localization.ApplyLanguage("en-US");

        var message = new ChatMessage("assistant", "content");

        Assert.Equal("Assistant", message.DisplayRole);
    }

    [Fact]
    public void DisplayRole_RaisesPropertyChanged_OnLanguageSwitch()
    {
        var localization = new LocalizationService();
        SeedLocalization(localization);
        localization.ApplyLanguage("en-US");

        var message = new ChatMessage("assistant", "content");
        var raised = false;
        message.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChatMessage.DisplayRole))
            {
                raised = true;
            }
        };

        localization.ApplyLanguage("zh-CN");

        Assert.True(raised);
        Assert.Equal("模型", message.DisplayRole);
    }

    private static void SeedLocalization(LocalizationService service)
    {
        var cacheField = typeof(LocalizationService).GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic);
        if (cacheField?.GetValue(service) is Dictionary<string, ResourceDictionary> cache)
        {
            cache["en-US"] = new ResourceDictionary
            {
                { "ChatRole.Assistant", "Assistant" }
            };
            cache["zh-CN"] = new ResourceDictionary
            {
                { "ChatRole.Assistant", "模型" }
            };

            var currentField = typeof(LocalizationService).GetField("_currentDictionary", BindingFlags.Instance | BindingFlags.NonPublic);
            currentField?.SetValue(service, cache["en-US"]);
        }

        var instanceField = typeof(LocalizationService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        instanceField?.SetValue(null, service);
    }
}
