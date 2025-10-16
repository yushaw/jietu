using CommunityToolkit.Mvvm.ComponentModel;
using SnapDescribe.App.Services;

namespace SnapDescribe.App.Models;

public partial class ChatMessage : ObservableObject
{
    public ChatMessage(string role, string content, bool includeImage = false)
    {
        Role = role;
        Content = content;
        IncludeImage = includeImage;
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(DisplayRole));
    }

    public string Role { get; }

    public bool IncludeImage { get; set; }

    public string DisplayRole
    {
        get
        {
            var localization = LocalizationService.Instance;
            return Role switch
            {
                "assistant" => localization.GetString("ChatRole.Assistant"),
                "user" => localization.GetString("ChatRole.User"),
                "system" => localization.GetString("ChatRole.System"),
                "tool" => localization.GetString("ChatRole.Tool"),
                _ => Role
            };
        }
    }

    [ObservableProperty]
    private string content;
}
