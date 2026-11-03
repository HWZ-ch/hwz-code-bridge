using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HWZCodeBridge.Core;

namespace HWZCodeBridge.Updating;

public sealed record UpdateInfo
{
    public required string Version { get; init; }
    public required string TagName { get; init; }
    public string Title { get; init; } = "";
    public string Notes { get; init; } = "";
    public required string DownloadUrl { get; init; }
    public required string AssetName { get; init; }
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = "";
    public DateTime PublishedAt { get; init; }
    public string HtmlUrl { get; init; } = "";

    public string SizeText => SizeBytes <= 0 ? "未知大小" : $"{SizeBytes / 1024.0 / 1024.0:0.0} MB";
    public string PublishedText => PublishedAt == DateTime.MinValue ? "" : PublishedAt.ToLocalTime().ToString("yyyy-MM-dd");
}

public enum UpdateStatus
{
    UpToDate,
    Available,
    Failed,
    Cancelled
}

public sealed record UpdateCheckResult(UpdateStatus Status, UpdateInfo? Info, string Message)
{
    public static UpdateCheckResult Fail(string message) => new(UpdateStatus.Failed, null, message);
}

/// <summary>
/// 基于 GitHub Releases 的更新服务。
///
/// 约定：发布新版本时在 GitHub 创建一个 Release，tag 形如 v1.0.1，
/// 并把单文件 exe 作为 Release 资源上传。程序据此判断版本、下载并自替换。
/// </summary>
public sealed class UpdateService
{
    private const string DefaultRepo = "HWZ-ch/hwz-code-bridge";
    private const string UserAgent = "HWZCodeBridge-Updater/1.0";

    private static readonly HttpClient Client = CreateClient();

    public static Version CurrentVersion { get; } =
        Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0, 0));

    /// <summary>用于界面展示的三段式版本号，例如 1.0.0。</summary>
    public static string CurrentVersionText =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static Version ParseVersion(string tag)
    {
        var text = (tag ?? "").Trim().TrimStart('v', 'V');

        // 兼容 1.2.3 / 1.2.3-beta / 1.2.3beta / 1.2.3-rc.1 等写法：
        // 只取开头连续的「数字与点」，其余后缀（beta、rc 等）一律忽略
        var match = System.Text.RegularExpressions.Regex.Match(text, @"^\d+(?:\.\d+)*");
        if (!match.Success) return new Version(0, 0, 0, 0);

        return Version.TryParse(match.Value, out var parsed)
            ? Normalize(parsed)
            : new Version(0, 0, 0, 0);
    }

    // ─────────────────────────────────────────────────────────
    // 检查
    // ─────────────────────────────────────────────────────────

    public async Task<UpdateCheckResult> CheckAsync(string? repo, CancellationToken ct)
    {
        var target = string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo.Trim();
        var url = $"https://api.github.com/repos/{target}/releases/latest";

        try
        {
            using var response = await Client.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.Fail("更新源还没有发布任何版本（找不到 Release），暂时无法检查更新。");
            }

            if ((int)response.StatusCode == 403)
            {
                return UpdateCheckResult.Fail("GitHub 接口访问频率受限，请稍后再试。");
            }

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Fail($"GitHub 返回 HTTP {(int)response.StatusCode}，检查失败。");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var info = ParseRelease(json);

            if (info is null)
            {
                return UpdateCheckResult.Fail("已找到版本信息，但其中没有可用的 .exe 资源，请检查 Release 附件。");
            }

            var latest = ParseVersion(info.TagName);
            Logger.Info("更新", $"检查完成：本地 {CurrentVersionText}，远程 {info.Version}");

            if (latest <= CurrentVersion)
            {
                return new UpdateCheckResult(UpdateStatus.UpToDate, info, $"当前已是最新版本 v{CurrentVersionText}。");
            }

            return new UpdateCheckResult(UpdateStatus.Available, info, $"发现新版本 v{info.Version}。");
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(UpdateStatus.Cancelled, null, "检查已取消。");
        }
        catch (HttpRequestException ex)
        {
            Logger.Warn("更新", "检查更新失败（网络）", ex.Message);
            return UpdateCheckResult.Fail("无法连接 GitHub，请检查网络后重试。");
        }
        catch (Exception ex)
        {
            Logger.Error("更新", "检查更新异常", ex.ToString());
            return UpdateCheckResult.Fail("检查更新时出现异常：" + ex.Message);
        }
    }

    private static UpdateInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string GetString(string name) =>
            root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() ?? ""
                : "";

        var tag = GetString("tag_name");
        var title = GetString("name");
        var body = GetString("body");
        var htmlUrl = GetString("html_url");

        var publishedAt = root.TryGetProperty("published_at", out var pub) && pub.TryGetDateTime(out var dt)
            ? dt
            : DateTime.MinValue;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        // 同一个 Release 里可能同时挂了主程序和配套工具，
        // 优先选择名称以 HWZCodeBridge 开头的 exe，否则退化为"体积最大的 exe"。
        JsonElement? preferred = null;
        JsonElement? largest = null;
        long largestSize = -1;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() ?? ""
                : "";

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt64()
                : 0;

            if (size > largestSize)
            {
                largest = asset;
                largestSize = size;
            }

            if (preferred is null &&
                name.StartsWith("HWZCodeBridge", StringComparison.OrdinalIgnoreCase))
            {
                preferred = asset;
            }
        }

        var best = preferred ?? largest;
        if (best is null) return null;

        var picked = best.Value;

        string AssetString(string name) =>
            picked.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() ?? ""
                : "";

        var downloadUrl = AssetString("browser_download_url");
        var assetName = AssetString("name");
        var digest = AssetString("digest");

        var sha256 = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest["sha256:".Length..]
            : "";

        if (string.IsNullOrWhiteSpace(downloadUrl)) return null;

        var version = tag.TrimStart('v', 'V');

        var assetSize = picked.TryGetProperty("size", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number
            ? sizeEl.GetInt64()
            : 0;

        return new UpdateInfo
        {
            Version = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version,
            TagName = tag,
            Title = string.IsNullOrWhiteSpace(title) ? tag : title,
            Notes = body,
            DownloadUrl = downloadUrl,
            AssetName = string.IsNullOrWhiteSpace(assetName) ? "HWZCodeBridge.exe" : assetName,
            SizeBytes = assetSize,
            Sha256 = sha256,
            PublishedAt = publishedAt,
            HtmlUrl = htmlUrl
        };
    }

    // ─────────────────────────────────────────────────────────
    // 下载
    // ─────────────────────────────────────────────────────────

    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "HWZUpdate", info.Version);
        Directory.CreateDirectory(dir);

        var target = Path.Combine(dir, info.AssetName);

        try
        {
            using var response = await Client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? info.SizeBytes;

            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;

                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    received += read;

                    if (total > 0) progress?.Report(Math.Min(1.0, (double)received / total));
                }
            }

            progress?.Report(1.0);

            if (!string.IsNullOrWhiteSpace(info.Sha256))
            {
                var actual = ComputeSha256(target);
                if (!actual.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(target);
                    Logger.Error("更新", "下载文件校验失败", $"期望 {info.Sha256}，实际 {actual}");
                    throw new InvalidDataException("下载文件的 SHA256 与发布信息不一致，已中止更新。");
                }

                Logger.Info("更新", "下载完成并通过 SHA256 校验");
            }
            else
            {
                Logger.Info("更新", "下载完成（发布方未提供哈希，已跳过校验）");
            }

            return target;
        }
        catch (OperationCanceledException)
        {
            TryDelete(target);
            throw;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();

        // 必须完全限定：本项目存在 HWZCodeBridge.Convert 命名空间，会遮蔽 System.Convert
        return System.Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    // ─────────────────────────────────────────────────────────
    // 应用更新
    // ─────────────────────────────────────────────────────────

    /// <summary>当前进程对应的可执行文件路径（单文件发布时即为待替换目标）。</summary>
    public static string GetExecutablePath() =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? Path.Combine(AppContext.BaseDirectory, "HWZCodeBridge.exe");

    /// <summary>
    /// 生成并启动替换脚本：等待主进程退出 → 覆盖 exe → 启动新版本 → 自删。
    /// 运行中的 exe 无法自我覆盖，因此必须借助一个独立进程完成。
    /// </summary>
    public static void ApplyUpdateAndRestart(string downloadedExe, string targetExe)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "HWZUpdate");
        Directory.CreateDirectory(workDir);

        var scriptPath = Path.Combine(workDir, "apply-update.bat");

        // 脚本刻意只用英文，避免不同代码页下的乱码
        var script = """
                     @echo off
                     setlocal
                     set "SRC=%~1"
                     set "DST=%~2"
                     for %%F in ("%DST%") do set "EXE=%%~nxF"

                     :wait
                     tasklist /FI "IMAGENAME eq %EXE%" 2>nul | find /I "%EXE%" >nul
                     if not errorlevel 1 (
                       timeout /t 1 /nobreak >nul
                       goto wait
                     )

                     :retry
                     copy /Y "%SRC%" "%DST%" >nul 2>&1
                     if errorlevel 1 (
                       timeout /t 1 /nobreak >nul
                       goto retry
                     )

                     start "" "%DST%"
                     (goto) 2>nul & del "%~f0"
                     """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

        Logger.Info("更新", "正在应用更新并重启", $"脚本 {scriptPath}");

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{scriptPath}\" \"{downloadedExe}\" \"{targetExe}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir
        };

        Process.Start(psi);
    }
}
