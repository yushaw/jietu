using System;
using System.Collections.Generic;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public class CapabilityResolver
{
    public CapabilityInvocationPlan Resolve(AppSettings settings, string? processName, string? windowTitle)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var fallbackPrompt = settings.DefaultPrompt;
        var fallbackParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt"] = fallbackPrompt
        };
        var fallbackPlan = new CapabilityInvocationPlan(CapabilityIds.LanguageModel, fallbackParameters, null, usedFallback: true);

        if (settings.PromptRules is null || settings.PromptRules.Count == 0)
        {
            return fallbackPlan;
        }

        var normalizedProcess = Normalize(processName);
        var normalizedProcessWithExe = AppendExe(normalizedProcess);
        var normalizedTitle = Normalize(windowTitle);

        foreach (var rule in settings.PromptRules)
        {
            if (rule is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.ProcessName))
            {
                continue;
            }

            var ruleProcess = Normalize(rule.ProcessName);
            if (string.IsNullOrEmpty(ruleProcess))
            {
                continue;
            }

            if (!ContainsNormalized(normalizedProcess, normalizedProcessWithExe, ruleProcess))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rule.WindowTitle))
            {
                var ruleTitle = Normalize(rule.WindowTitle);
                if (string.IsNullOrEmpty(ruleTitle))
                {
                    continue;
                }

                if (!ContainsNormalized(normalizedTitle, null, ruleTitle))
                {
                    continue;
                }
            }

            var capabilityId = string.IsNullOrWhiteSpace(rule.CapabilityId)
                ? CapabilityIds.LanguageModel
                : rule.CapabilityId;

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rule.BuildParameterMap())
            {
                parameters[kv.Key] = kv.Value;
            }

            var usedFallback = false;
            if (string.Equals(capabilityId, CapabilityIds.LanguageModel, StringComparison.OrdinalIgnoreCase))
            {
                if (!parameters.TryGetValue("prompt", out var prompt) || string.IsNullOrWhiteSpace(prompt))
                {
                    parameters["prompt"] = fallbackPrompt;
                    usedFallback = true;
                }
            }

            return new CapabilityInvocationPlan(capabilityId, parameters, rule, usedFallback);
        }

        return fallbackPlan;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string? AppendExe(string? process)
    {
        if (string.IsNullOrEmpty(process))
        {
            return process;
        }

        return process.EndsWith(".exe", StringComparison.Ordinal) ? process : process + ".exe";
    }

    private static bool ContainsNormalized(string? source, string? altSource, string target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(source) && source.Contains(target, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(altSource) && altSource.Contains(target, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
