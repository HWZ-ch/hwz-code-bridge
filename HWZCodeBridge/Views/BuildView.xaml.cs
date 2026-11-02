using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HWZCodeBridge.Build;
using HWZCodeBridge.Convert;
using HWZCodeBridge.Core;
using Microsoft.Win32;

namespace HWZCodeBridge.Views;

public partial class BuildView : UserControl, IFileDropTarget
{
    private static readonly Dictionary<string, string[]> ToolsByLanguage = new()
    {
        ["python"] = new[] { "pyinstaller", "nuitka" },
        ["javascript"] = new[] { "pkg" },
        ["typescript"] = new[] { "pkg" },
        ["csharp"] = new[] { "dotnet" },
        ["cpp"] = new[] { "gcc", "clang" },
        ["java"] = new[] { "jpackage" }
    };

    private readonly ConcurrentQueue<string> _logQueue = new();
    private readonly DispatcherTimer _logTimer;
    private readonly BuildService _buildService = new();

    private CancellationTokenSource? _cts;
    private bool _suppressEvents;

    public BuildView()
    {
        InitializeComponent();

        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _logTimer.Tick += (_, _) => FlushLog();
        _logTimer.Start();

        Loaded += OnLoaded;
        Unloaded += (_, _) => _logTimer.Stop();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressEvents = true;

        LangBox.ItemsSource = LanguageCatalog.All;
        LangBox.SelectedItem = LanguageCatalog.Get("python");

        OutputDirBox.Text = SettingsStore.Current.OutputDirectory;
        ArchBox.SelectedIndex = ArchitectureIndex(SettingsStore.Current.BuildArchitecture);

        _suppressEvents = false;

        RefreshToolList();
        AppendLog("HWZ 代码互换工坊 · 编译为可执行文件");
        AppendLog("提示：本功能调用本机已安装的第三方工具，不会上传任何代码。");
        AppendLog("");

        await ToolRegistry.DetectAllAsync();
        UpdateToolInfo();
    }

    private static int ArchitectureIndex(string rid) => rid switch
    {
        "win-x86" => 1,
        "win-arm64" => 2,
        _ => 0
    };

    private string SelectedArchitecture =>
        (ArchBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "win-x64";

    // ───────────────── 联动 ─────────────────

    private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        RefreshToolList();
    }

    private void RefreshToolList()
    {
        if (LangBox.SelectedItem is not LanguageInfo lang) return;

        var ids = ToolsByLanguage.TryGetValue(lang.Id, out var list) ? list : Array.Empty<string>();
        var tools = ids.Select(ToolRegistry.Find).Where(t => t is not null).Cast<ToolDefinition>().ToList();

        ToolBox.ItemsSource = tools;
        if (tools.Count > 0) ToolBox.SelectedIndex = 0;
        else
        {
            ToolInfo.Text = "该语言暂未内置打包流程。可以在「工具中心」添加自定义工具并查看官方用法。";
        }
    }

    private void ToolBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateToolInfo();

    private void UpdateToolInfo()
    {
        if (ToolBox.SelectedItem is not ToolDefinition tool) return;

        var status = tool.Detected ? $"已就绪（{tool.Version}）" : "未检测到，可点击「工具中心 → 官网」获取";

        ToolInfo.Text = $"{tool.Name} — {tool.Purpose}\n" +
                        $"来源：{tool.SourceNote} · {tool.Homepage} · 许可证：{tool.License}\n" +
                        $"状态：{status}";
    }

    private void SourcePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;

        var path = SourcePathBox.Text.Trim();
        if (!File.Exists(path)) return;

        var guessed = LanguageCatalog.GuessFromPath(path);
        if (guessed is not null)
        {
            _suppressEvents = true;
            LangBox.SelectedItem = guessed;
            _suppressEvents = false;
            RefreshToolList();
        }

        if (string.IsNullOrWhiteSpace(OutputNameBox.Text) || OutputNameBox.Text == Globals.LastOutputName)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            OutputNameBox.Text = name;
            Globals.LastOutputName = name;
        }
    }

    // ───────────────── 浏览 ─────────────────

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择需要编译的源文件",
            Filter = "源代码|*.py;*.js;*.mjs;*.ts;*.cs;*.csproj;*.java;*.cpp;*.cc;*.c|所有文件|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            _suppressEvents = true;
            SourcePathBox.Text = dialog.FileName;
            _suppressEvents = false;
            SourcePathBox_TextChanged(SourcePathBox, null!);
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择输出目录" };

        if (dialog.ShowDialog() == true)
        {
            OutputDirBox.Text = dialog.FolderName;
            SettingsStore.Current.OutputDirectory = dialog.FolderName;
            SettingsStore.Save();
        }
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e) => OpenFolder(OutputDirBox.Text);

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("打包", "打开输出目录失败", ex.Message);
        }
    }

    public void OnFilesDropped(string[] paths)
    {
        var file = paths.FirstOrDefault(File.Exists);
        if (file is null) return;

        _suppressEvents = true;
        SourcePathBox.Text = file;
        _suppressEvents = false;

        SourcePathBox_TextChanged(SourcePathBox, null!);
        SetStatus($"已载入：{Path.GetFileName(file)}");
    }

    // ───────────────── 打包 ─────────────────

    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;

        var sourcePath = SourcePathBox.Text.Trim();

        if (!File.Exists(sourcePath))
        {
            MessageBox.Show("请选择一个存在的源文件。", "缺少源文件", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (LangBox.SelectedItem is not LanguageInfo lang || ToolBox.SelectedItem is not ToolDefinition tool)
        {
            MessageBox.Show("请选择源语言与打包工具。", "配置不完整", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var outputDir = OutputDirBox.Text.Trim();
        if (outputDir.Length == 0)
        {
            outputDir = AppPaths.DefaultOutputDir;
            OutputDirBox.Text = outputDir;
        }

        if (!tool.Detected)
        {
            var answer = MessageBox.Show(
                $"未在本机检测到「{tool.Name}」。\n\n" +
                $"来源：{tool.SourceNote}\n官网：{tool.Homepage}\n许可证：{tool.License}\n\n" +
                "仍然尝试继续打包吗？（若未安装，命令会失败并给出错误信息）",
                $"缺少 {tool.Name}",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.OK) return;
        }

        var request = new BuildRequest
        {
            SourcePath = sourcePath,
            Language = lang,
            ToolId = tool.Id,
            OutputDirectory = outputDir,
            OutputName = OutputNameBox.Text.Trim(),
            Architecture = SelectedArchitecture,
            CompressWithUpx = UpxCheck.IsChecked == true
        };

        if (SettingsStore.Current.ConfirmBeforeBuild)
        {
            var confirm = MessageBox.Show(
                $"即将执行打包：\n\n源文件：{Path.GetFileName(sourcePath)}\n" +
                $"工具：{tool.Name}（{tool.SourceNote}）\n" +
                $"输出：{Path.Combine(outputDir, OutputNameBox.Text.Trim())}\n" +
                $"架构：{SelectedArchitecture}\n\n继续吗？",
                "确认打包", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.OK) return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        ClearLog();
        SetStatus("正在打包…");

        var stopwatch = Stopwatch.StartNew();
        var progress = new Progress<string>(AppendLog);

        try
        {
            var result = await _buildService.BuildAsync(request, progress, _cts.Token);
            stopwatch.Stop();

            FlushLog();
            AppendLog("");
            AppendLog(result.Success ? "════ 打包完成 ════" : "════ 打包失败 ════");
            AppendLog(result.Message);

            HistoryStore.Add(new HistoryRecord
            {
                Kind = RecordKind.Build,
                SourceName = Path.GetFileName(sourcePath),
                SourceLang = lang.Name,
                TargetLang = "EXE",
                Status = result.Success ? "成功" : "失败",
                DurationMs = stopwatch.ElapsedMilliseconds,
                OutputPath = result.OutputPath,
                Summary = result.Message
            });

            SetStatus(result.Success ? "打包成功" : "打包失败");

            if (result.Success)
            {
                var open = MessageBox.Show(
                    result.OutputPath.Length > 0
                        ? $"{result.Message}\n\n位置：{result.OutputPath}\n\n现在打开所在文件夹吗？"
                        : result.Message + "\n\n现在打开输出目录吗？",
                    "打包成功", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (open == MessageBoxResult.Yes)
                {
                    OpenFolder(result.OutputPath.Length > 0
                        ? Path.GetDirectoryName(result.OutputPath) ?? outputDir
                        : outputDir);
                }
            }
            else
            {
                MessageBox.Show(result.Message, "打包失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("打包", "打包流程异常", ex.ToString());
            AppendLog("✗ 异常：" + ex.Message);
            MessageBox.Show("打包过程出现异常：" + ex.Message, "异常", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is null) return;
        _cts.Cancel();
        SetStatus("正在取消…");
    }

    private void SetBusy(bool busy)
    {
        BuildBtn.IsEnabled = !busy;
        CancelBtn.IsEnabled = busy;
        BuildBtn.Content = busy ? "打包中…" : "开始打包";
    }

    private void SetStatus(string text) => BuildStatus.Text = text;

    // ───────────────── 日志 ─────────────────

    private void AppendLog(string line) => _logQueue.Enqueue(line);

    private void FlushLog()
    {
        if (_logQueue.IsEmpty) return;

        var sb = new System.Text.StringBuilder();
        while (_logQueue.TryDequeue(out var line))
        {
            sb.AppendLine(line);
        }

        LogBox.AppendText(sb.ToString());
        LogBox.ScrollToEnd();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => ClearLog();

    private void ClearLog()
    {
        _logQueue.Clear();
        LogBox.Clear();
    }
}

/// <summary>跨页面共享的少量界面状态。</summary>
internal static class Globals
{
    public static string LastOutputName = "";
}
