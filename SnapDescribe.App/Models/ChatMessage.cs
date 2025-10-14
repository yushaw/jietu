using CommunityToolkit.Mvvm.ComponentModel;

namespace SnapDescribe.App.Models;

public partial class ChatMessage : ObservableObject
{
    public ChatMessage(string role, string content, bool includeImage = false)
    {
        Role = role;
        Content = content;
        IncludeImage = includeImage;
    }

    public string Role { get; }

    public bool IncludeImage { get; set; }

    public string DisplayRole => Role switch
    {
        "assistant" => "模型",
        "user" => "用户",
        "system" => "系统",
        _ => Role
    };

    [ObservableProperty]
    private string content;
}
