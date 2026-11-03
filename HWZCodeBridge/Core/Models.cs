using System.Text.Json.Serialization;

namespace HWZCodeBridge.Core;

/// <summary>可持久化的用户设置。字段全部给默认值，保证旧版本配置可平滑升级。</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Light";                 // Light | Dark
    public string OutputDirectory { get; set; } = AppPaths.DefaultOutputDir;
    public bool SaveHistory { get; set; } = true;
    public bool RememberLanguages { get; set; } = true;
    public bool CopyResultAfterConvert { get; set; } = false;
    public bool WordWrap { get; set; } = false;
    public double FontSize { get; set; } = 13.0;
    public string LastSourceLang { get; set; } = "python";
    public string LastTargetLang { get; set; } = "csharp";
    public bool ConfirmBeforeBuild { get; set; } = true;
    public string BuildArchitecture { get; set; } = "win-x64";

    // ── 自动更新 ──────────────────────────────
    /// <summary>启动时自动检查 GitHub 是否有新版本。</summary>
    public bool AutoCheckUpdate { get; set; } = true;

    /// <summary>用户选择"跳过此版本"时记录，避免反复提示同一版本。</summary>
    public string SkippedVersion { get; set; } = "";

    /// <summary>上次检查时间的 ISO 字符串，用于限制自动检查频率。</summary>
    public string LastUpdateCheck { get; set; } = "";

    /// <summary>更新源仓库（owner/repo），留空则使用内置默认值。</summary>
    public string UpdateRepo { get; set; } = "HWZ-ch/hwz-code-bridge";

    [JsonIgnore] public string ThemeDisplay => Theme == "Dark" ? "深色" : "浅色";
}

public enum RecordKind
{
    Convert,
    Build
}

/// <summary>一条转换 / 编译记录，保存在 history.json。</summary>
public sealed class HistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Time { get; set; } = DateTime.Now;
    public RecordKind Kind { get; set; }
    public string SourceName { get; set; } = "";
    public string SourceLang { get; set; } = "";
    public string TargetLang { get; set; } = "";
    public string Status { get; set; } = "成功";
    public long DurationMs { get; set; }
    public string OutputPath { get; set; } = "";
    public string Summary { get; set; } = "";

    [JsonIgnore]
    public string KindText => Kind == RecordKind.Convert ? "代码互换" : "编译产物";

    [JsonIgnore]
    public string TimeText => Time.ToString("MM-dd HH:mm:ss");

    [JsonIgnore]
    public string RouteText => Kind == RecordKind.Convert
        ? $"{SourceLang} → {TargetLang}"
        : $"{SourceLang} → EXE";

    [JsonIgnore]
    public string DurationText => DurationMs < 1000 ? $"{DurationMs} ms" : $"{DurationMs / 1000.0:0.0} s";
}

/// <summary>
/// 第三方工具描述。来源（Homepage / License / SourceNote）为必填项，
/// 用于满足"引入现成第三方工具必须标明来源"的要求。
/// </summary>
public sealed class ToolDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Purpose { get; set; } = "";          // 用途说明
    public string Category { get; set; } = "";         // 打包 / 编译 / 辅助
    public string Homepage { get; set; } = "";         // 官方主页
    public string License { get; set; } = "";          // 开源许可证
    public string SourceNote { get; set; } = "";       // 来源标注（作者/组织）
    public string DetectCommand { get; set; } = "";    // 用于检测安装状态的可执行文件名
    public string VersionArgs { get; set; } = "--version";
    public bool IsBuiltIn { get; set; } = true;
    public string CustomPath { get; set; } = "";       // 用户手动指定的可执行文件路径

    /// <summary>运行期检测结果，不参与序列化。</summary>
    [JsonIgnore] public bool Detected { get; set; }
    [JsonIgnore] public string Version { get; set; } = "";
    [JsonIgnore] public string StatusText => Detected ? $"已就绪 {Version}".Trim() : "未检测到";

    public override string ToString() => Name;
}
