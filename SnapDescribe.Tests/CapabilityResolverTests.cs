using System.Collections.ObjectModel;
using SnapDescribe.App.Models;
using SnapDescribe.App.Services;
using Xunit;

namespace SnapDescribe.Tests;

public class CapabilityResolverTests
{
    [Fact]
    public void Resolve_NoRules_UsesDefaultPrompt()
    {
        var settings = new AppSettings
        {
            DefaultPrompt = "Default prompt"
        };

        var resolver = new CapabilityResolver();
        var plan = resolver.Resolve(settings, "chrome.exe", null);

        Assert.Equal(CapabilityIds.LanguageModel, plan.CapabilityId);
        Assert.Equal("Default prompt", plan.GetParameter("prompt"));
        Assert.True(plan.UsedFallback);
        Assert.Null(plan.MatchedRule);
    }

    [Fact]
    public void Resolve_MatchingLanguageModelRule_ReturnsRulePrompt()
    {
        var rule = new PromptRule
        {
            ProcessName = "chrome.exe",
            Prompt = "Browser summary"
        };

        var settings = new AppSettings
        {
            DefaultPrompt = "Default prompt",
            PromptRules = new ObservableCollection<PromptRule> { rule }
        };

        var resolver = new CapabilityResolver();
        var plan = resolver.Resolve(settings, "chrome.exe", null);

        Assert.Equal(CapabilityIds.LanguageModel, plan.CapabilityId);
        Assert.Equal("Browser summary", plan.GetParameter("prompt"));
        Assert.False(plan.UsedFallback);
        Assert.Same(rule, plan.MatchedRule);
    }

    [Fact]
    public void Resolve_MatchingExternalCapability_ReturnsCapabilityParameters()
    {
        var rule = new PromptRule
        {
            ProcessName = "chrome.exe",
            CapabilityId = CapabilityIds.ExternalTool
        };
        rule.Parameters.Add(new CapabilityParameter
        {
            Key = "tool",
            Value = "seo-analyzer"
        });

        var settings = new AppSettings
        {
            DefaultPrompt = "Default prompt",
            PromptRules = new ObservableCollection<PromptRule> { rule }
        };

        var resolver = new CapabilityResolver();
        var plan = resolver.Resolve(settings, "chrome.exe", null);

        Assert.Equal(CapabilityIds.ExternalTool, plan.CapabilityId);
        Assert.Equal("seo-analyzer", plan.GetParameter("tool"));
        Assert.Null(plan.GetParameter("prompt"));
        Assert.False(plan.UsedFallback);
    }

    [Fact]
    public void Resolve_LanguageModelWithoutPrompt_FallsBackToDefault()
    {
        var rule = new PromptRule
        {
            ProcessName = "chrome.exe",
            Prompt = string.Empty
        };

        var settings = new AppSettings
        {
            DefaultPrompt = "Default prompt",
            PromptRules = new ObservableCollection<PromptRule> { rule }
        };

        var resolver = new CapabilityResolver();
        var plan = resolver.Resolve(settings, "chrome.exe", null);

        Assert.Equal(CapabilityIds.LanguageModel, plan.CapabilityId);
        Assert.Equal("Default prompt", plan.GetParameter("prompt"));
        Assert.True(plan.UsedFallback);
    }

    [Fact]
    public void Resolve_OcrRule_UsesConfiguredLanguage()
    {
        var rule = new PromptRule
        {
            ProcessName = "winword.exe",
            CapabilityId = CapabilityIds.Ocr
        };
        rule.Parameters.Add(new CapabilityParameter
        {
            Key = "language",
            Value = "eng+deu"
        });

        var settings = new AppSettings
        {
            DefaultPrompt = "Default prompt",
            PromptRules = new ObservableCollection<PromptRule> { rule }
        };

        var resolver = new CapabilityResolver();
        var plan = resolver.Resolve(settings, "winword.exe", null);

        Assert.Equal(CapabilityIds.Ocr, plan.CapabilityId);
        Assert.Equal("eng+deu", plan.GetParameter("language"));
        Assert.False(plan.UsedFallback);
    }
}
