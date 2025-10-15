using System;
using System.Collections.Generic;

namespace SnapDescribe.App.Models;

public sealed class CapabilityInvocationPlan
{
    public CapabilityInvocationPlan(string capabilityId, IReadOnlyDictionary<string, string> parameters, PromptRule? matchedRule, bool usedFallback)
    {
        CapabilityId = capabilityId ?? throw new ArgumentNullException(nameof(capabilityId));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        MatchedRule = matchedRule;
        UsedFallback = usedFallback;
    }

    public string CapabilityId { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }

    public PromptRule? MatchedRule { get; }

    public bool UsedFallback { get; }

    public string? GetParameter(string key)
    {
        if (Parameters.Count == 0)
        {
            return null;
        }

        return Parameters.TryGetValue(key, out var value) ? value : null;
    }
}
