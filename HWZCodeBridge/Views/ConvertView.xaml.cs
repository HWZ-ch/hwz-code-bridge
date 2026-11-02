using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HWZCodeBridge.Convert;
using HWZCodeBridge.Core;
using Microsoft.Win32;

namespace HWZCodeBridge.Views;

public partial class ConvertView : UserControl, IFileDropTarget
{
    private LanguageInfo _source = LanguageCatalog.Get("python");
    private LanguageInfo _target = LanguageCatalog.Get("csharp");
    private string _currentFileName = "";
    private bool _loading;

    public ConvertView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;

        SourceLangBox.ItemsSource = LanguageCatalog.All;
        TargetLangBox.ItemsSource = LanguageCatalog.All;

        var s = SettingsStore.Current;
        _source = LanguageCatalog.Get(s.RememberLanguages ? s.LastSourceLang : "python");
        _target = LanguageCatalog.Get(s.RememberLanguages ? s.LastTargetLang : "csharp");

        SourceLangBox.SelectedItem = _source;
        TargetLangBox.SelectedItem = _target;

        ApplyTypography();
        UpdateMeta();

        _loading = false;

        if (SourceBox.Text.Length == 0) ShowDropHint(true);
    }

    private void ApplyTypography()
    {
        var s = SettingsStore.Current;
        SourceBox.FontSize = s.FontSize;
        ResultBox.FontSize = s.FontSize;

        var wrap = s.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        SourceBox.TextWrapping = wrap;
        ResultBox.TextWrapping = wrap;
    }

    // ───────────────── 语言选择 ─────────────────

    private void SourceLangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceLangBox.SelectedItem is not LanguageInfo lang || _loading) return;
        _source = lang;
        PersistLanguageChoice();
    }

    private void TargetLangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetLangBox.SelectedItem is not LanguageInfo lang || _loading) return;
        _target = lang;
        PersistLanguageChoice();
    }

    private void PersistLanguageChoice()
    {
        var s = SettingsStore.Current;
        if (!s.RememberLanguages) return;

        s.LastSourceLang = _source.Id;
        s.LastTargetLang = _target.Id;
        SettingsStore.Save();
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;

        (_source, _target) = (_target, _source);
        SourceLangBox.SelectedItem = _source;
        TargetLangBox.SelectedItem = _target;

        if (!string.IsNullOrWhiteSpace(ResultBox.Text))
        {
            SourceBox.Text = ResultBox.Text;
            ResultBox.Clear();
        }

        _loading = false;
        PersistLanguageChoice();
        UpdateMeta();
        ShowDropHint(SourceBox.Text.Length == 0);
    }

    // ───────────────── 核心动作 ─────────────────

    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceBox.Text))
        {
            MessageBox.Show("请先输入代码，或把代码文件拖入窗口。", "还没有内容",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = CodeConverter.Convert(SourceBox.Text, _source, _target);

        if (!result.Success)
        {
            ResultBox.Text = "";
            ShowNotes(null, result.Message);
            MessageBox.Show(result.Message, "转换失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            Logger.Warn("转换", $"{_source.Name} → {_target.Name} 失败：{result.Message}");
            return;
        }

        ResultBox.Text = result.Code;
        ShowNotes(result, "");
        UpdateMeta(result);

        HistoryStore.Add(new HistoryRecord
        {
            Kind = RecordKind.Convert,
            SourceName = string.IsNullOrEmpty(_currentFileName) ? "编辑器输入" : Path.GetFileName(_currentFileName),
            SourceLang = _source.Name,
            TargetLang = _target.Name,
            Status = result.WarnCount > 0 ? $"成功 · {result.WarnCount} 条提示" : "成功",
            DurationMs = result.ElapsedMs,
            Summary = result.Message
        });

        if (SettingsStore.Current.CopyResultAfterConvert && result.Code.Length > 0)
        {
            TrySetClipboard(result.Code);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SourceBox.Clear();
        ResultBox.Clear();
        _currentFileName = "";
        NotesPanel.Visibility = Visibility.Collapsed;
        UpdateMeta();
        ShowDropHint(true);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ResultBox.Text)) return;
        TrySetClipboard(ResultBox.Text);
    }

    private static void TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            Logger.Warn("剪贴板", "写入剪贴板失败", ex.Message);
        }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ResultBox.Text))
        {
            MessageBox.Show("还没有可保存的转换结果。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "保存转换结果",
            FileName = string.IsNullOrEmpty(_currentFileName)
                ? "converted" + _target.Extension
                : Path.GetFileNameWithoutExtension(_currentFileName) + "_" + _target.Id + _target.Extension,
            DefaultExt = _target.Extension,
            Filter = $"{_target.Name} 文件|*{_target.Extension}|所有文件|*.*",
            InitialDirectory = Directory.Exists(SettingsStore.Current.OutputDirectory)
                ? SettingsStore.Current.OutputDirectory
                : AppPaths.DefaultOutputDir
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, ResultBox.Text, new UTF8Encoding(true));
            Logger.Info("转换", "结果已保存", dialog.FileName);
        }
        catch (Exception ex)
        {
            Logger.Error("转换", "保存结果失败", ex.Message);
            MessageBox.Show("保存失败：" + ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开代码文件",
            Filter = "代码文件|*.py;*.js;*.mjs;*.ts;*.cs;*.java;*.cpp;*.cc;*.h;*.c|所有文件|*.*"
        };

        if (dialog.ShowDialog() == true) LoadFile(dialog.FileName);
    }

    private void LoadSample_Click(object sender, RoutedEventArgs e)
    {
        SourceBox.Text = _source.Sample;
        _currentFileName = "";
        ShowDropHint(false);
        UpdateMeta();
    }

    // ───────────────── 文件载入 ─────────────────

    public void OnFilesDropped(string[] paths)
    {
        var file = paths.FirstOrDefault(File.Exists);
        if (file is null) return;

        LoadFile(file);
    }

    private void LoadFile(string path)
    {
        try
        {
            var text = ReadTextSmart(path);
            SourceBox.Text = text;
            _currentFileName = path;

            var guessed = LanguageCatalog.GuessFromPath(path);
            if (guessed is not null && guessed.Id != _source.Id)
            {
                _loading = true;
                _source = guessed;
                SourceLangBox.SelectedItem = guessed;
                _loading = false;
                PersistLanguageChoice();
            }

            ShowDropHint(false);
            UpdateMeta();
            Logger.Info("转换", $"已载入文件：{path}");

            if (text.Length > 200_000)
            {
                MessageBox.Show("文件较大（超过 200 KB），转换结果的准确性可能下降，建议拆分后分段转换。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("转换", "读取文件失败", ex.Message);
            MessageBox.Show("无法读取该文件：" + ex.Message, "读取失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>优先按 UTF-8 严格解码，失败则回退到 GB18030（中文 Windows 常见编码）。</summary>
    public static string ReadTextSmart(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch
        {
            try
            {
                return Encoding.GetEncoding("GB18030").GetString(bytes);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }

    // ───────────────── 界面状态 ─────────────────

    private void SourceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateMeta();
        ShowDropHint(SourceBox.Text.Length == 0);
    }

    private void ShowDropHint(bool show)
    {
        DropHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMeta(ConversionResult? result = null)
    {
        var srcLines = CodeConverter.CountLines(SourceBox.Text);
        SourceMeta.Text = $"{srcLines} 行";

        if (result is null)
        {
            ResultMeta.Text = string.IsNullOrWhiteSpace(ResultBox.Text) ? "等待转换" : $"{CodeConverter.CountLines(ResultBox.Text)} 行";
            return;
        }

        ResultMeta.Text = $"{result.TargetLines} 行 · {result.ElapsedMs} ms";
    }

    private void ShowNotes(ConversionResult? result, string errorMessage)
    {
        NotesList.Children.Clear();

        if (!string.IsNullOrEmpty(errorMessage))
        {
            NotesPanel.Visibility = Visibility.Visible;
            NotesCount.Text = "转换未完成";
            NotesList.Children.Add(BuildNoteRow(errorMessage, isWarn: true));
            return;
        }

        if (result is null || result.Notes.Count == 0)
        {
            NotesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        NotesPanel.Visibility = Visibility.Visible;
        NotesCount.Text = $"{result.Notes.Count} 条，其中需人工复核 {result.WarnCount} 条";

        foreach (var note in result.Notes.Take(60))
        {
            NotesList.Children.Add(BuildNoteRow(note.ToString(), note.Level == NoteLevel.Warn));
        }
    }

    private FrameworkElement BuildNoteRow(string text, bool isWarn)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

        row.Children.Add(new TextBlock
        {
            Text = isWarn ? "▲" : "·",
            FontSize = 10,
            Margin = new Thickness(0, 2, 7, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = (Brush)FindResource(isWarn ? "WarnBrush" : "TextSecondaryBrush")
        });

        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource(isWarn ? "TextPrimaryBrush" : "TextSecondaryBrush")
        });

        return row;
    }
}
