using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace HWZCodeBridge.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}

public sealed record LogEntry(DateTime Time, LogLevel Level, string Category, string Message, string? Detail);

/// <summary>
/// 轻量级分级日志。写入 logs\yyyy-MM-dd.log（UTF-8，带 BOM 以便记事本正确识别中文），
/// 同时通过 <see cref="EntryWritten"/> 推送到界面日志面板。
/// 致命异常额外落一份 crash 报告，便于用户反馈 BUG。
/// </summary>
public static class Logger
{
    private static readonly object FileLock = new();
    private static readonly ConcurrentQueue<LogEntry> Buffer = new();
    private static readonly int MaxBuffer = 2000;

    public static event Action<LogEntry>? EntryWritten;

    public static string CurrentLogFile =>
        Path.Combine(AppPaths.LogDir, $"{DateTime.Now:yyyy-MM-dd}.log");

    public static IReadOnlyList<LogEntry> Recent => Buffer.ToArray();

    public static void Debug(string category, string message, string? detail = null) => Write(LogLevel.Debug, category, message, detail);
    public static void Info(string category, string message, string? detail = null) => Write(LogLevel.Info, category, message, detail);
    public static void Warn(string category, string message, string? detail = null) => Write(LogLevel.Warn, category, message, detail);
    public static void Error(string category, string message, string? detail = null) => Write(LogLevel.Error, category, message, detail);

    public static void Fatal(string category, string message, Exception ex)
    {
        Write(LogLevel.Fatal, category, message, ex.ToString());
        TryWriteCrashReport(category, message, ex);
    }

    private static void Write(LogLevel level, string category, string message, string? detail)
    {
        var entry = new LogEntry(DateTime.Now, level, category, message, detail);
        Buffer.Enqueue(entry);
        while (Buffer.Count > MaxBuffer && Buffer.TryDequeue(out _)) { }

        try
        {
            var line = new StringBuilder()
                .Append(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level.ToString().ToUpperInvariant().PadRight(5)).Append("] ")
                .Append('[').Append(category).Append("] ")
                .Append(message);

            if (!string.IsNullOrWhiteSpace(detail))
            {
                line.AppendLine().Append("        ↳ ").Append(detail!.Replace("\r\n", "\n        ").Replace("\n", "\n        "));
            }

            Directory.CreateDirectory(AppPaths.LogDir);
            lock (FileLock)
            {
                File.AppendAllText(CurrentLogFile, line.ToString() + Environment.NewLine, new UTF8Encoding(true));
            }
        }
        catch
        {
            // 日志系统自身永不抛出，避免二次崩溃。
        }

        try { EntryWritten?.Invoke(entry); } catch { }
    }

    private static void TryWriteCrashReport(string category, string message, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.CrashDir);
            var path = Path.Combine(AppPaths.CrashDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var sb = new StringBuilder();
            sb.AppendLine("HWZ代码互换工坊 — 崩溃报告");
            sb.AppendLine("签名：HWZ工作室");
            sb.AppendLine(new string('=', 60));
            sb.AppendLine($"时间      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"类别      : {category}");
            sb.AppendLine($"摘要      : {message}");
            sb.AppendLine($"操作系统  : {Environment.OSVersion}");
            sb.AppendLine($"运行时    : {Environment.Version}");
            sb.AppendLine($"程序版本  : {typeof(Logger).Assembly.GetName().Version}");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine(ex.ToString());
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
        catch { }
    }

    /// <summary>把当前日志目录打包成一个便于反馈的文本快照。</summary>
    public static string ExportDiagnostics()
    {
        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"HWZ诊断报告-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        var sb = new StringBuilder();
        sb.AppendLine("HWZ代码互换工坊 — 诊断报告");
        sb.AppendLine("签名：HWZ工作室");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"生成时间 : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"操作系统 : {Environment.OSVersion}");
        sb.AppendLine($"运行时   : .NET {Environment.Version}");
        sb.AppendLine($"程序版本 : {typeof(Logger).Assembly.GetName().Version}");
        sb.AppendLine($"数据目录 : {AppPaths.RootDir}");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine();
        sb.AppendLine("【本次会话日志】");
        foreach (var e in Recent)
        {
            sb.AppendLine($"{e.Time:HH:mm:ss.fff} [{e.Level}] [{e.Category}] {e.Message}");
            if (!string.IsNullOrWhiteSpace(e.Detail))
            {
                sb.AppendLine("    " + e.Detail!.Replace("\n", "\n    "));
            }
        }

        File.WriteAllText(target, sb.ToString(), new UTF8Encoding(true));
        Info("日志", "诊断报告已导出", target);
        return target;
    }
}
