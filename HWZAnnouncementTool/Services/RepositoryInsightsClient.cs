using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using HWZAnnouncementTool.Models;

namespace HWZAnnouncementTool.Services;

public sealed record InsightsResult(bool Success, RepositorySnapshot? Data, string Message);

/// <summary>
/// 读取仓库的公开指标与流量数据。
///
/// 公开指标来自 /repos/{repo}，任何 Token（甚至无 Token）都可读取；
/// 流量指标来自 /traffic/*，GitHub 要求调用者拥有该仓库的写入权限
/// （fine-grained token 需 Administration: Read，classic token 需 repo 范围）。
/// 因此流量读取失败不会中断流程，而是降级为「仅展示公开数据」并给出原因。
/// </summary>
public sealed class RepositoryInsightsClient
{
    private const string ApiRoot = "https://api.github.com";

    private readonly HttpClient _client;
    private readonly bool _hasToken;

    private sealed record GitHubResponse(HttpStatusCode Status, string Body, string RateRemaining);

    public RepositoryInsightsClient(string? token)
    {
        _hasToken = !string.IsNullOrWhiteSpace(token);

        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("HWZAnnouncementTool/1.1");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        if (_hasToken)
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token!.Trim());
        }
    }

    // ─────────────────────────────────────────────
    // 入口
    // ─────────────────────────────────────────────

    public async Task<InsightsResult> LoadAsync(string repo, CancellationToken ct)
    {
        repo = (repo ?? "").Trim().Trim('/');

        if (repo.Split('/').Length != 2 || repo.Contains(' '))
        {
            return new InsightsResult(false, null, "仓库格式应为 owner/repo，例如 HWZ-ch/hwz-code-bridge。");
        }

        var snapshot = new RepositorySnapshot { Repo = repo, FetchedAt = DateTimeOffset.Now };

        // ── 1. 公开指标 ──
        var meta = await TryGetAsync($"{ApiRoot}/repos/{repo}", ct);
        if (meta is null)
        {
            return new InsightsResult(false, null, "网络异常：无法连接 GitHub，请检查网络或代理设置。");
        }

        if (meta.Status != HttpStatusCode.OK)
        {
            return new InsightsResult(false, null, DescribeFailure(meta, "读取仓库信息"));
        }

        try
        {
            using var doc = JsonDocument.Parse(meta.Body);
            var root = doc.RootElement;

            snapshot.Description = ReadString(root, "description");
            snapshot.HtmlUrl = ReadString(root, "html_url");
            snapshot.PushedAt = ReadString(root, "pushed_at");
            snapshot.Stars = ReadInt(root, "stargazers_count");
            snapshot.Forks = ReadInt(root, "forks_count");
            snapshot.Watchers = ReadInt(root, "subscribers_count");
            snapshot.OpenIssues = ReadInt(root, "open_issues_count");
        }
        catch (JsonException)
        {
            return new InsightsResult(false, null, "GitHub 返回的仓库信息无法解析。");
        }

        // ── 2. 流量数据（需要写权限，失败即降级）──
        await ReadTrafficAsync(snapshot, repo, ct);

        return new InsightsResult(true, snapshot, "已更新统计。");
    }

    private async Task ReadTrafficAsync(RepositorySnapshot snapshot, string repo, CancellationToken ct)
    {
        var views = await TryGetAsync($"{ApiRoot}/repos/{repo}/traffic/views", ct);

        if (views is null)
        {
            snapshot.TrafficMessage = "网络异常：未能读取流量数据。";
            return;
        }

        if (views.Status != HttpStatusCode.OK)
        {
            snapshot.TrafficMessage = DescribeTrafficFailure(views);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(views.Body);
            var root = doc.RootElement;

            snapshot.Views14 = ReadInt(root, "count");
            snapshot.ViewUniques14 = ReadInt(root, "uniques");
            snapshot.Days = ReadDays(root, "views");
            snapshot.TrafficAvailable = true;
        }
        catch (JsonException)
        {
            snapshot.TrafficMessage = "流量数据无法解析。";
            return;
        }

        var clones = await TryGetAsync($"{ApiRoot}/repos/{repo}/traffic/clones", ct);
        if (clones is { Status: HttpStatusCode.OK })
        {
            try
            {
                using var doc = JsonDocument.Parse(clones.Body);
                snapshot.Clones14 = ReadInt(doc.RootElement, "count");
                snapshot.CloneUniques14 = ReadInt(doc.RootElement, "uniques");
            }
            catch (JsonException) { /* 克隆数缺失不影响主流程 */ }
        }

        snapshot.Referrers = await ReadRefsAsync($"{ApiRoot}/repos/{repo}/traffic/popular/referrers", ct);
        snapshot.Paths = await ReadRefsAsync($"{ApiRoot}/repos/{repo}/traffic/popular/paths", ct);
    }

    private static List<TrafficDay> ReadDays(JsonElement root, string arrayName)
    {
        var list = new List<TrafficDay>();
        if (!root.TryGetProperty(arrayName, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;

        foreach (var node in arr.EnumerateArray())
        {
            var date = ToLocalDate(ReadString(node, "timestamp"));
            if (date.Length == 0) continue;

            list.Add(new TrafficDay
            {
                Date = date,
                Views = ReadInt(node, "count"),
                Uniques = ReadInt(node, "uniques")
            });
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
        return list;
    }

    private async Task<List<TrafficRef>> ReadRefsAsync(string url, CancellationToken ct)
    {
        var result = new List<TrafficRef>();
        var response = await TryGetAsync(url, ct);
        if (response is not { Status: HttpStatusCode.OK }) return result;

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            foreach (var node in doc.RootElement.EnumerateArray())
            {
                // referrers 用 referrer 字段，paths 用 path 字段
                var name = ReadString(node, "referrer");
                if (name.Length == 0) name = ReadString(node, "path");

                result.Add(new TrafficRef
                {
                    Name = name,
                    Title = ReadString(node, "title"),
                    Count = ReadInt(node, "count"),
                    Uniques = ReadInt(node, "uniques")
                });
            }
        }
        catch (JsonException) { }

        return result.Take(6).ToList();
    }

    // ─────────────────────────────────────────────
    // HTTP
    // ─────────────────────────────────────────────

    private async Task<GitHubResponse?> TryGetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
                ? values.FirstOrDefault() ?? ""
                : "";

            return new GitHubResponse(response.StatusCode, body, remaining);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    // ─────────────────────────────────────────────
    // 提示文案
    // ─────────────────────────────────────────────

    private static string DescribeFailure(GitHubResponse response, string action) => response.Status switch
    {
        HttpStatusCode.Unauthorized => $"{action}失败：Token 无效或已过期，请重新填写。",
        HttpStatusCode.Forbidden => response.RateRemaining == "0"
            ? $"{action}失败：GitHub API 请求次数已达上限，请稍后再试或填写 Token。"
            : $"{action}失败：Token 权限不足。",
        HttpStatusCode.NotFound => $"{action}失败：仓库不存在，或当前 Token 无权访问该仓库。",
        _ => $"{action}失败：HTTP {(int)response.Status} {TryReadMessage(response.Body)}".Trim()
    };

    private string DescribeTrafficFailure(GitHubResponse response)
    {
        switch (response.Status)
        {
            case HttpStatusCode.Unauthorized:
                return _hasToken
                    ? "Token 无效或已过期，无法读取流量数据。"
                    : "未填写 Token。流量数据只能由拥有该仓库写权限的账号读取。";
            case HttpStatusCode.Forbidden when response.RateRemaining == "0":
                return "GitHub API 请求次数已达上限，请稍后再试。";
            case HttpStatusCode.Forbidden:
                return _hasToken
                    ? "Token 权限不足：流量数据要求该仓库的 Administration 读权限（classic token 需 repo 整权）。"
                    : "未填写 Token。流量数据只能由拥有该仓库写权限的账号读取。";
            case HttpStatusCode.NotFound:
                return "仓库不存在，或当前 Token 无权访问该仓库的流量数据。";
            default:
                return $"流量数据读取失败：HTTP {(int)response.Status}";
        }
    }

    private static string TryReadMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    // ─────────────────────────────────────────────
    // JSON 辅助
    // ─────────────────────────────────────────────

    private static string ReadString(JsonElement node, string name) =>
        node.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";

    private static int ReadInt(JsonElement node, string name) =>
        node.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number &&
        el.TryGetInt32(out var value)
            ? value
            : 0;

    private static string ToLocalDate(string timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp)) return "";

        return DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "";
    }
}
