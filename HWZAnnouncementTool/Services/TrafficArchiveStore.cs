using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HWZAnnouncementTool.Models;

namespace HWZAnnouncementTool.Services;

/// <summary>
/// 本机累计访问存档。
///
/// GitHub Traffic API 只返回最近 14 天，无法回答「一共多少人看过」。
/// 因此每次成功拉取后把每日数据合并进本地档案，用「按日去重后累加」的方式
/// 得到自首次统计以来的累计浏览量与累计独立访客数。
/// 口径局限：同一访客若在多天访问会被重复计数，因此该值是可解释的下界近似。
/// </summary>
public static class TrafficArchiveStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object Gate = new();

    public static string ArchivePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HWZ工作室", "公告编辑器", "traffic-history.json");

    /// <summary>合并最新 14 天数据并返回累计结果。</summary>
    public static ArchiveSummary Merge(string repo, IEnumerable<TrafficDay> days)
    {
        var fresh = days.Where(d => !string.IsNullOrWhiteSpace(d.Date)).ToList();

        lock (Gate)
        {
            var archive = Load();
            var entry = GetOrCreate(archive, repo);

            var now = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
            if (string.IsNullOrWhiteSpace(entry.FirstRecordedAt)) entry.FirstRecordedAt = now;
            entry.LastRecordedAt = now;

            foreach (var day in fresh)
            {
                entry.Days[day.Date] = new TrafficDay
                {
                    Date = day.Date,
                    Views = day.Views,
                    Uniques = day.Uniques
                };
            }

            Save(archive);
            return Summarize(entry);
        }
    }

    /// <summary>只读取本地档案，不发起网络请求。用于网络失败时仍展示已有累计。</summary>
    public static ArchiveSummary Peek(string repo)
    {
        lock (Gate)
        {
            var archive = Load();
            return archive.Repos.TryGetValue(Key(repo), out var entry)
                ? Summarize(entry)
                : new ArchiveSummary(0, 0, 0, "");
        }
    }

    /// <summary>清空某个仓库的累计档案。</summary>
    public static void Clear(string repo)
    {
        lock (Gate)
        {
            var archive = Load();
            if (archive.Repos.Remove(Key(repo))) Save(archive);
        }
    }

    private static ArchiveSummary Summarize(RepoArchive entry)
    {
        var sorted = entry.Days.Values
            .Where(d => !string.IsNullOrWhiteSpace(d.Date))
            .OrderBy(d => d.Date, StringComparer.Ordinal)
            .ToList();

        return new ArchiveSummary(
            sorted.Sum(d => d.Views),
            sorted.Sum(d => d.Uniques),
            sorted.Count,
            sorted.Count > 0 ? sorted[0].Date : "");
    }

    private static string Key(string repo) => (repo ?? "").Trim().Trim('/').ToLowerInvariant();

    private static RepoArchive GetOrCreate(TrafficArchive archive, string repo)
    {
        var key = Key(repo);
        if (!archive.Repos.TryGetValue(key, out var entry))
        {
            entry = new RepoArchive();
            archive.Repos[key] = entry;
        }

        return entry;
    }

    private static TrafficArchive Load()
    {
        try
        {
            if (!File.Exists(ArchivePath)) return new TrafficArchive();

            var json = File.ReadAllText(ArchivePath, Encoding.UTF8);
            var archive = JsonSerializer.Deserialize<TrafficArchive>(json, JsonOptions);
            if (archive is null) return new TrafficArchive();

            archive.Repos ??= new Dictionary<string, RepoArchive>();
            foreach (var entry in archive.Repos.Values)
            {
                entry.Days ??= new Dictionary<string, TrafficDay>();
            }

            return archive;
        }
        catch
        {
            // 存档损坏时按空档案处理，不影响统计流程
            return new TrafficArchive();
        }
    }

    private static void Save(TrafficArchive archive)
    {
        try
        {
            var dir = Path.GetDirectoryName(ArchivePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ArchivePath, JsonSerializer.Serialize(archive, JsonOptions), new UTF8Encoding(false));
        }
        catch
        {
            // 写入失败不阻断界面展示
        }
    }
}
