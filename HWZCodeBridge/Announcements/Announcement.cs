using System.Text.Json.Serialization;

namespace HWZCodeBridge.Announcements;

/// <summary>
/// 公告数据结构。与仓库根目录 announcement.json 一一对应，
/// 桌面版与网页版共用同一份文件、同一套字段。
/// </summary>
public sealed class Announcement
{
    /// <summary>公告标识，修改内容时建议更换，便于排查。</summary>
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>正文，支持 \n 换行。</summary>
    public string Content { get; set; } = "";

    /// <summary>级别：info / warn / important，决定界面配色。</summary>
    public string Level { get; set; } = "info";

    public bool Enabled { get; set; } = true;

    /// <summary>发布时间，ISO 8601 字符串；为空时界面不显示时间。</summary>
    public string PublishedAt { get; set; } = "";

    /// <summary>可选的跳转按钮文字。</summary>
    public string ActionText { get; set; } = "";

    /// <summary>可选的跳转地址，必须是 http/https。</summary>
    public string ActionUrl { get; set; } = "";

    [JsonIgnore]
    public bool HasAction =>
        !string.IsNullOrWhiteSpace(ActionText) &&
        !string.IsNullOrWhiteSpace(ActionUrl) &&
        (ActionUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         ActionUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public string LevelText => Level switch
    {
        "warn" => "注意",
        "important" => "重要",
        _ => "公告"
    };

    [JsonIgnore]
    public string PublishedText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PublishedAt)) return "";
            return DateTimeOffset.TryParse(PublishedAt, out var time)
                ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "";
        }
    }
}
