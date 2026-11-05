using System.Text.Json.Serialization;

namespace HWZAnnouncementTool.Models;

/// <summary>某一天的仓库流量（来自 GitHub Traffic API）。</summary>
public sealed class TrafficDay
{
    /// <summary>本地日期，格式 yyyy-MM-dd。</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("views")]
    public int Views { get; set; }

    [JsonPropertyName("uniques")]
    public int Uniques { get; set; }
}

/// <summary>热门来源或热门入口。</summary>
public sealed class TrafficRef
{
    /// <summary>来源域名（referrer）或页面路径（path）。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>页面标题，仅 popular/paths 有值。</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("uniques")]
    public int Uniques { get; set; }
}

/// <summary>
/// 一次统计请求的完整结果。公开指标（星标等）与流量指标分开保存，
/// 以便在 Token 权限不足时仍能展示公开数据。
/// </summary>
public sealed class RepositorySnapshot
{
    public string Repo { get; set; } = "";
    public string Description { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string PushedAt { get; set; } = "";

    public int Stars { get; set; }
    public int Forks { get; set; }
    public int Watchers { get; set; }
    public int OpenIssues { get; set; }

    /// <summary>GitHub 官方口径：最近 14 天浏览次数。</summary>
    public int Views14 { get; set; }

    /// <summary>GitHub 官方口径：最近 14 天独立访客数。</summary>
    public int ViewUniques14 { get; set; }

    public int Clones14 { get; set; }
    public int CloneUniques14 { get; set; }

    public List<TrafficDay> Days { get; set; } = new();
    public List<TrafficRef> Referrers { get; set; } = new();
    public List<TrafficRef> Paths { get; set; } = new();

    /// <summary>是否成功读取到流量数据（需要 Token 具备仓库 Administration 读权限）。</summary>
    public bool TrafficAvailable { get; set; }

    /// <summary>流量数据不可用时的原因说明。</summary>
    public string TrafficMessage { get; set; } = "";

    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.Now;

    // ── 本机累计口径（由 TrafficArchiveStore 填充）──

    public int TotalViews { get; set; }
    public int TotalUniques { get; set; }
    public int ArchivedDays { get; set; }
    public string ArchiveStart { get; set; } = "";

    /// <summary>累计口径是否包含今天之前的历史存档。</summary>
    public bool HasArchive => ArchivedDays > 1;
}

/// <summary>本机累计档案：按仓库分别记录每日流量，用于计算「共有多少人浏览过」。</summary>
public sealed class TrafficArchive
{
    [JsonPropertyName("repos")]
    public Dictionary<string, RepoArchive> Repos { get; set; } = new();
}

public sealed class RepoArchive
{
    [JsonPropertyName("firstRecordedAt")]
    public string FirstRecordedAt { get; set; } = "";

    [JsonPropertyName("lastRecordedAt")]
    public string LastRecordedAt { get; set; } = "";

    /// <summary>键为 yyyy-MM-dd。</summary>
    [JsonPropertyName("days")]
    public Dictionary<string, TrafficDay> Days { get; set; } = new();
}

/// <summary>累计统计结果。</summary>
public sealed record ArchiveSummary(int TotalViews, int TotalUniques, int Days, string Start);
