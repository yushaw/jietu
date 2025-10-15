namespace SnapDescribe.App.Models;

public sealed class CapabilityOption
{
    public CapabilityOption(string id, string displayName, bool isAvailable)
    {
        Id = id;
        DisplayName = displayName;
        IsAvailable = isAvailable;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public bool IsAvailable { get; }
}
