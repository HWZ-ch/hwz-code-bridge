using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HWZCodeBridge.Core;

/// <summary>JSON 持久化的公共实现：原子写入（先写 .tmp 再替换），避免写入中断导致配置损坏。</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return fallback();
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        catch (Exception ex)
        {
            Logger.Warn("存储", $"读取 {Path.GetFileName(path)} 失败，已回退默认值", ex.Message);
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error("存储", $"写入 {Path.GetFileName(path)} 失败", ex.Message);
        }
    }
}

/// <summary>设置读写。所有改动即时落盘，做到"设置保留"。</summary>
public static class SettingsStore
{
    private static AppSettings? _current;

    public static AppSettings Current => _current ??= JsonStore.Load(AppPaths.SettingsFile, () => new AppSettings());

    public static void Save()
    {
        JsonStore.Save(AppPaths.SettingsFile, Current);
        Logger.Debug("设置", "设置已保存");
    }
}

/// <summary>转换 / 编译记录，最多保留 500 条。</summary>
public static class HistoryStore
{
    private const int MaxRecords = 500;
    private static List<HistoryRecord>? _cache;

    public static event Action? Changed;

    public static List<HistoryRecord> All
    {
        get
        {
            if (_cache is null)
            {
                _cache = JsonStore.Load(AppPaths.HistoryFile, () => new List<HistoryRecord>());
            }
            return _cache;
        }
    }

    public static void Add(HistoryRecord record)
    {
        if (!SettingsStore.Current.SaveHistory) return;

        All.Insert(0, record);
        if (All.Count > MaxRecords) All.RemoveRange(MaxRecords, All.Count - MaxRecords);

        JsonStore.Save(AppPaths.HistoryFile, All);
        Logger.Debug("记录", $"新增{record.KindText}记录：{record.RouteText} · {record.Status}");
        try { Changed?.Invoke(); } catch { }
    }

    public static void Remove(string id)
    {
        var hit = All.FirstOrDefault(r => r.Id == id);
        if (hit is null) return;
        All.Remove(hit);
        JsonStore.Save(AppPaths.HistoryFile, All);
        try { Changed?.Invoke(); } catch { }
    }

    public static void Clear()
    {
        All.Clear();
        JsonStore.Save(AppPaths.HistoryFile, All);
        Logger.Info("记录", "全部转换记录已清空");
        try { Changed?.Invoke(); } catch { }
    }

    public static int Export(string targetPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("时间,类型,来源,转换路径,状态,耗时(ms),输出位置,说明");
        foreach (var r in All)
        {
            sb.AppendLine(string.Join(",",
                Escape(r.Time.ToString("yyyy-MM-dd HH:mm:ss")),
                Escape(r.KindText),
                Escape(r.SourceName),
                Escape(r.RouteText),
                Escape(r.Status),
                r.DurationMs.ToString(),
                Escape(r.OutputPath),
                Escape(r.Summary)));
        }

        File.WriteAllText(targetPath, sb.ToString(), new UTF8Encoding(true));
        Logger.Info("记录", $"转换记录已导出：{targetPath}");
        return All.Count;
    }

    private static string Escape(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
