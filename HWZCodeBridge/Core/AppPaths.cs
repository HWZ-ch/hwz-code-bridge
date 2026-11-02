using System.IO;

namespace HWZCodeBridge.Core;

/// <summary>
/// 统一的本地存储路径解析。所有用户数据落在 %AppData%\HWZ工作室\HWZ代码互换工坊 下，
/// 便于备份、迁移与卸载清理，不污染程序目录。
/// </summary>
public static class AppPaths
{
    public const string VendorName = "HWZ工作室";
    public const string ProductName = "HWZ代码互换工坊";

    public static string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        VendorName,
        ProductName);

    public static string LogDir => Path.Combine(RootDir, "logs");
    public static string SettingsFile => Path.Combine(RootDir, "settings.json");
    public static string HistoryFile => Path.Combine(RootDir, "history.json");
    public static string ToolsFile => Path.Combine(RootDir, "custom-tools.json");
    public static string CrashDir => Path.Combine(RootDir, "crash");

    /// <summary>默认输出目录：桌面下的 HWZ输出 文件夹。</summary>
    public static string DefaultOutputDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "HWZ输出");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(CrashDir);
        Directory.CreateDirectory(DefaultOutputDir);
    }
}
