using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HWZCodeBridge.Core;
using HWZCodeBridge.Themes;
using HWZCodeBridge.Updating;
using Microsoft.Win32;

namespace HWZCodeBridge.Views;

public partial class SettingsView : UserControl
{
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;

        var s = SettingsStore.Current;

        ThemeBox.SelectedIndex = s.Theme == "Dark" ? 1 : 0;
        FontSlider.Value = s.FontSize;
        FontValue.Text = $"{s.FontSize:0} px";
        WrapCheck.IsChecked = s.WordWrap;
        HistoryCheck.IsChecked = s.SaveHistory;
        RememberCheck.IsChecked = s.RememberLanguages;
        CopyCheck.IsChecked = s.CopyResultAfterConvert;
        ConfirmCheck.IsChecked = s.ConfirmBeforeBuild;
        OutputBox.Text = s.OutputDirectory;

        ArchBox.SelectedIndex = s.BuildArchitecture switch
        {
            "win-x86" => 1,
            "win-arm64" => 2,
            _ => 0
        };

        DataPathText.Text = $"配置文件、转换记录与日志都保存在：\n{AppPaths.RootDir}";

        UpdateVersionText.Text = "v" + UpdateService.CurrentVersionText;
        AutoUpdateCheck.IsChecked = s.AutoCheckUpdate;
        UpdateStatusText.Text = BuildUpdateStatusText(s);

        _loading = false;
    }

    private static string BuildUpdateStatusText(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.LastUpdateCheck)) return "尚未检查过更新";

        return DateTime.TryParse(s.LastUpdateCheck, out var time)
            ? $"上次检查：{time:yyyy-MM-dd HH:mm}"
            : "尚未检查过更新";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        UpdateStatusText.Text = "正在检查…";

        try
        {
            var message = await UpdateUi.CheckAndPromptAsync(Window.GetWindow(this));
            UpdateStatusText.Text = message + " · " + DateTime.Now.ToString("HH:mm");
        }
        catch (Exception ex)
        {
            Logger.Error("更新", "手动检查更新异常", ex.ToString());
            UpdateStatusText.Text = "检查失败";
        }
        finally
        {
            CheckUpdateBtn.IsEnabled = true;
        }
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem item) return;

        var theme = item.Tag as string ?? "Light";

        SettingsStore.Current.Theme = theme;
        SettingsStore.Save();
        ThemeManager.Apply(theme);
    }

    private void FontSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;

        SettingsStore.Current.FontSize = e.NewValue;
        SettingsStore.Save();
        if (FontValue is not null) FontValue.Text = $"{e.NewValue:0} px";
    }

    private void AnySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var s = SettingsStore.Current;
        s.WordWrap = WrapCheck.IsChecked == true;
        s.SaveHistory = HistoryCheck.IsChecked == true;
        s.RememberLanguages = RememberCheck.IsChecked == true;
        s.CopyResultAfterConvert = CopyCheck.IsChecked == true;
        s.ConfirmBeforeBuild = ConfirmCheck.IsChecked == true;
        s.AutoCheckUpdate = AutoUpdateCheck.IsChecked == true;
        SettingsStore.Save();
    }

    private void ArchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        SettingsStore.Current.BuildArchitecture =
            (ArchBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "win-x64";
        SettingsStore.Save();
    }

    private void OutputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var path = OutputBox.Text.Trim();
        if (path.Length == 0) return;

        SettingsStore.Current.OutputDirectory = path;
        SettingsStore.Save();
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择默认输出目录" };

        if (dialog.ShowDialog() != true) return;

        OutputBox.Text = dialog.FolderName;
        SettingsStore.Current.OutputDirectory = dialog.FolderName;
        SettingsStore.Save();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.RootDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppPaths.RootDir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Warn("设置", "打开数据目录失败", ex.Message);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "将恢复所有设置为默认值（不会删除转换记录与日志）。\n\n继续吗？",
            "恢复默认设置", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        var fresh = new AppSettings();
        var current = SettingsStore.Current;

        current.Theme = fresh.Theme;
        current.OutputDirectory = fresh.OutputDirectory;
        current.SaveHistory = fresh.SaveHistory;
        current.RememberLanguages = fresh.RememberLanguages;
        current.CopyResultAfterConvert = fresh.CopyResultAfterConvert;
        current.WordWrap = fresh.WordWrap;
        current.FontSize = fresh.FontSize;
        current.LastSourceLang = fresh.LastSourceLang;
        current.LastTargetLang = fresh.LastTargetLang;
        current.ConfirmBeforeBuild = fresh.ConfirmBeforeBuild;
        current.BuildArchitecture = fresh.BuildArchitecture;

        SettingsStore.Save();
        ThemeManager.Apply(current.Theme);

        OnLoaded(this, e);
        Logger.Info("设置", "设置已恢复默认值");
    }
}
