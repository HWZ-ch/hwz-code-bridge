using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using HWZCodeBridge.Core;

namespace HWZCodeBridge.Views;

public partial class LogView : UserControl
{
    private readonly StringBuilder _buffer = new();
    private bool _subscribed;

    public LogView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Unsubscribe();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            Logger.EntryWritten += OnEntry;
            _subscribed = true;
        }

        Render();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        Logger.EntryWritten -= OnEntry;
        _subscribed = false;
    }

    private void OnEntry(LogEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnEntry(entry)));
            return;
        }

        if (!Passes(entry.Level)) return;

        LogText.AppendText(Format(entry) + Environment.NewLine);
        LogText.ScrollToEnd();
        UpdateStats();
    }

    private bool Passes(LogLevel level)
    {
        var filter = (LevelBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        return filter switch
        {
            "warn" => level >= LogLevel.Warn,
            "error" => level >= LogLevel.Error,
            _ => true
        };
    }

    private static string Format(LogEntry e)
    {
        var detail = string.IsNullOrWhiteSpace(e.Detail) ? "" : Environment.NewLine + "    " + e.Detail!.Replace("\n", "\n    ");
        return $"{e.Time:HH:mm:ss.fff} [{e.Level.ToString().ToUpperInvariant(),-5}] [{e.Category}] {e.Message}{detail}";
    }

    private void Render()
    {
        var entries = Logger.Recent.Where(e => Passes(e.Level)).ToList();

        _buffer.Clear();
        foreach (var e in entries) _buffer.AppendLine(Format(e));

        LogText.Text = _buffer.ToString();
        LogText.ScrollToEnd();
        UpdateStats();
    }

    private void UpdateStats()
    {
        var all = Logger.Recent;
        var errors = all.Count(e => e.Level >= LogLevel.Error);
        var warns = all.Count(e => e.Level == LogLevel.Warn);

        StatText.Text = $"本次会话共 {all.Count} 条日志 · 警告 {warns} 条 · 错误 {errors} 条 · 日志文件：{Logger.CurrentLogFile}";
    }

    private void LevelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogText is not null) Render();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Render();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppPaths.LogDir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Warn("日志", "打开日志目录失败", ex.Message);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Logger.ExportDiagnostics();
            var open = MessageBox.Show(
                $"诊断报告已导出到：\n{path}\n\n现在打开它吗？",
                "导出完成", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (open == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("日志", "导出诊断报告失败", ex.Message);
            MessageBox.Show("导出失败：" + ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
