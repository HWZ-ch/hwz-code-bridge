using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HWZCodeBridge.Core;

namespace HWZCodeBridge.Announcements;

/// <summary>
/// 从 GitHub 仓库读取公告。
///
/// 用 Contents API 而不是 raw 域名：raw.githubusercontent.com 在部分网络环境下解析异常，
/// 而 API 返回 base64 内容，稳定且可携带正确的内容类型。
/// </summary>
public sealed class AnnouncementService
{
    private const string FileName = "announcement.json";
    private static readonly HttpClient Client = CreateClient();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HWZCodeBridge-Announcement/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// 拉取公告。返回 null 表示"没有可显示的公告"（文件不存在、未启用、解析失败或网络异常）。
    /// 所有异常都静默处理，只写日志——公告失败绝不能影响程序正常启动。
    /// </summary>
    public async Task<Announcement?> FetchAsync(string? repo, CancellationToken ct)
    {
        var target = string.IsNullOrWhiteSpace(repo) ? "HWZ-ch/hwz-code-bridge" : repo.Trim();
        var url = $"https://api.github.com/repos/{target}/contents/{FileName}?ref=main";

        try
        {
            using var response = await Client.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Debug("公告", "仓库中还没有公告文件，本次不显示公告");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.Debug("公告", $"拉取公告失败：HTTP {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.String)
            {
                Logger.Debug("公告", "公告文件缺少 content 字段");
                return null;
            }

            var base64 = (contentElement.GetString() ?? "").Replace("\n", "").Replace("\r", "").Trim();

            if (base64.Length == 0) return null;

            // 注意：本项目存在 HWZCodeBridge.Convert 命名空间，会遮蔽 System.Convert
            var bytes = System.Convert.FromBase64String(base64);
            var text = Encoding.UTF8.GetString(bytes);

            var announcement = JsonSerializer.Deserialize<Announcement>(text, Options);

            if (announcement is null || !announcement.Enabled) return null;
            if (string.IsNullOrWhiteSpace(announcement.Title) && string.IsNullOrWhiteSpace(announcement.Content))
            {
                return null;
            }

            Logger.Info("公告", $"已获取公告：{announcement.Title}");
            return announcement;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.Debug("公告", "获取公告异常：" + ex.Message);
            return null;
        }
    }
}
