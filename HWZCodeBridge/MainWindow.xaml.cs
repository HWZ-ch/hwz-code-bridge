using System.Windows;
using System.Windows.Controls;
using HWZCodeBridge.Announcements;
using HWZCodeBridge.Core;
using HWZCodeBridge.Updating;
using HWZCodeBridge.Views;

namespace HWZCodeBridge;

/// <summary>页面可接收文件拖入。</summary>
public interface IFileDropTarget
{
    void OnFilesDropped(string[] paths);
}

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => Logger.Info("退出", "程序正常关闭");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Navigate("convert");

        // 让首屏先完成渲染，再做后台请求，避免启动卡顿
        await Task.Delay(1500);

        // 先展示公告（信息），再检查更新（动作），这样用户不会因为直接更新而漏看公告
        await ShowAnnouncementAsync();
        await RunStartupUpdateCheckAsync();
    }

    /// <summary>拉取并展示公告。任何异常都不影响启动。</summary>
    private async Task ShowAnnouncementAsync()
    {
        try
        {
            var announcement = await new AnnouncementService()
                .FetchAsync(SettingsStore.Current.UpdateRepo, CancellationToken.None);

            if (announcement is null) return;

            var window = new AnnouncementWindow(announcement) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Logger.Debug("公告", "展示公告失败：" + ex.Message);
        }
    }

    /// <summary>启动时的自动更新检查：静默模式，只在发现新版本时打扰用户。</summary>
    private async Task RunStartupUpdateCheckAsync()
    {
        var settings = SettingsStore.Current;

        if (!settings.AutoCheckUpdate) return;

        // 24 小时内不重复自动检查，避免频繁请求 GitHub
        if (DateTime.TryParse(settings.LastUpdateCheck, out var last) &&
            (DateTime.Now - last).TotalHours < 24)
        {
            Logger.Debug("更新", "距上次检查不足 24 小时，跳过自动检查");
            return;
        }

        Logger.Debug("更新", "开始启动自动检查");
        await UpdateUi.CheckAndPromptAsync(this, quiet: true);
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string key }) return;
        if (!IsLoaded) return;
        Navigate(key);
    }

    private void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "build" => new BuildView(),
                "tools" => new ToolsView(),
                "history" => new HistoryView(),
                "log" => new LogView(),
                "settings" => new SettingsView(),
                "about" => new AboutView(),
                _ => new ConvertView()
            };

            _pages[key] = page;
        }

        PageHost.Content = page;
        Logger.Debug("界面", $"切换到页面：{key}");
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (PageHost.Content is not IFileDropTarget target) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            target.OnFilesDropped(paths);
        }

        e.Handled = true;
    }
}
