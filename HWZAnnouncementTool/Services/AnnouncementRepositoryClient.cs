using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HWZAnnouncementTool.Models;

namespace HWZAnnouncementTool.Services;

public sealed record FetchResult(bool Success, Announcement? Item, string Sha, string Message);

public sealed record PublishResult(bool Success, string Message, string CommitUrl);

/// <summary>
/// 通过 GitHub Contents API 读取与写入 announcement.json。
/// </summary>
public sealed class AnnouncementRepositoryClient
{
    private const string FilePath = "announcement.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private readonly HttpClient _client;

    public AnnouncementRepositoryClient(string? token)
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("HWZAnnouncementTool/1.0");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        if (!string.IsNullOrWhiteSpace(token))
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }
    }

    public static string Serialize(Announcement item) => JsonSerializer.Serialize(item, JsonOptions);

    // ─────────────────────────────────────────────
    // 读取
    // ─────────────────────────────────────────────

    public async Task<FetchResult> FetchAsync(string repo, string branch, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{repo}/contents/{FilePath}?ref={Uri.EscapeDataString(branch)}";

        try
        {
            using var response = await _client.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new FetchResult(false, null, "", "远程仓库里还没有 announcement.json，可从当前内容新建一份。");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new FetchResult(false, null, "", $"读取失败：HTTP {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var sha = doc.RootElement.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() ?? "" : "";

            if (!doc.RootElement.TryGetProperty("content", out var contentEl))
            {
                return new FetchResult(false, null, sha, "返回内容缺少 content 字段。");
            }

            var base64 = (contentEl.GetString() ?? "").Replace("\n", "").Replace("\r", "").Trim();
            var bytes = System.Convert.FromBase64String(base64);
            var text = Encoding.UTF8.GetString(bytes);

            var item = JsonSerializer.Deserialize<Announcement>(text, JsonOptions);

            if (item is null) return new FetchResult(false, null, sha, "公告内容解析失败。");

            return new FetchResult(true, item, sha, "已读取线上公告。");
        }
        catch (Exception ex)
        {
            return new FetchResult(false, null, "", "读取异常：" + ex.Message);
        }
    }

    // ─────────────────────────────────────────────
    // 发布
    // ─────────────────────────────────────────────

    public async Task<PublishResult> PublishAsync(string repo, string branch, Announcement item, string sha, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{repo}/contents/{FilePath}";

        var content = Serialize(item);
        var base64 = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

        var payload = new Dictionary<string, object>
        {
            ["message"] = $"chore: 更新公告 - {item.Title}",
            ["content"] = base64,
            ["branch"] = branch
        };

        if (!string.IsNullOrWhiteSpace(sha)) payload["sha"] = sha;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var response = await _client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var detail = TryReadMessage(body);
                var hint = (int)response.StatusCode switch
                {
                    401 => "Token 无效或已过期。",
                    403 => "Token 权限不足，需要 Contents: Read and write。",
                    404 => "仓库不存在，或 Token 未被授权访问该仓库。",
                    409 => "远程文件已被他人修改，请先重新读取线上公告再发布。",
                    _ => ""
                };

                return new PublishResult(false, $"发布失败：HTTP {(int)response.StatusCode} {hint} {detail}".Trim(), "");
            }

            var commitUrl = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("commit", out var commit) &&
                    commit.TryGetProperty("html_url", out var html))
                {
                    commitUrl = html.GetString() ?? "";
                }
            }
            catch { }

            return new PublishResult(true, "发布成功，所有客户端下次启动即可看到。", commitUrl);
        }
        catch (Exception ex)
        {
            return new PublishResult(false, "发布异常：" + ex.Message, "");
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
    // 本地设置
    // ─────────────────────────────────────────────

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HWZ工作室", "公告编辑器", "settings.json");

    public static ToolSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new ToolSettings();
            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<ToolSettings>(json, JsonOptions) ?? new ToolSettings();
        }
        catch
        {
            return new ToolSettings();
        }
    }

    public static void SaveSettings(ToolSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        }
        catch
        {
            // 保存失败不影响主流程
        }
    }
}
