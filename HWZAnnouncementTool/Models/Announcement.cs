using System.Text.Json.Serialization;

namespace HWZAnnouncementTool.Models;

/// <summary>
/// 公告数据。字段与仓库根目录 announcement.json 完全对应，
/// 桌面版与网页版都按这个结构读取。
/// </summary>
public sealed class Announcement
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>info / warn / important</summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = "";

    [JsonPropertyName("actionText")]
    public string ActionText { get; set; } = "";

    [JsonPropertyName("actionUrl")]
    public string ActionUrl { get; set; } = "";

    public Announcement Clone() => new()
    {
        Id = Id,
        Title = Title,
        Content = Content,
        Level = Level,
        Enabled = Enabled,
        PublishedAt = PublishedAt,
        ActionText = ActionText,
        ActionUrl = ActionUrl
    };
}

/// <summary>本机保存的编辑器设置。</summary>
public sealed class ToolSettings
{
    public string Repo { get; set; } = "HWZ-ch/hwz-code-bridge";
    public string Branch { get; set; } = "main";
    public string TokenCipher { get; set; } = "";
    public string AuthorName { get; set; } = "HWZ工作室";

    /// <summary>Base64 只是防止肩窥，并非加密；界面会如实说明。</summary>
    public string Token
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TokenCipher)) return "";
            try { return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(TokenCipher)); }
            catch { return ""; }
        }
        set => TokenCipher = string.IsNullOrEmpty(value)
            ? ""
            : System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
    }
}
