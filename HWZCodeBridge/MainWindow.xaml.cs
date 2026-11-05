using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

        // 侧栏版本号跟随程序集版本，避免硬编码后忘记同步
        VersionLabel.Text = "v" + UpdateService.CurrentVersionText;

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

    private Announcement? _noticeAnnouncement;

    /// <summary>拉取并展示公告。任何异常都不影响启动。</summary>
    private async Task ShowAnnouncementAsync()
    {
        try
        {
            var announcement = await new AnnouncementService()
                .FetchAsync(SettingsStore.Current.UpdateRepo, CancellationToken.None);

            if (announcement is null) return;

            // 重要级：模态弹窗，必须让用户完整看到
            if (string.Equals(announcement.Level, "important", StringComparison.OrdinalIgnoreCase))
            {
                var window = new AnnouncementWindow(announcement) { Owner = this };
                window.ShowDialog();
                return;
            }

            // 普通 / 注意级：顶部悬浮条，不打断用户操作
            ShowNoticeBar(announcement);
        }
        catch (Exception ex)
        {
            Logger.Debug("公告", "展示公告失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 顶部公告条。普通级只显示标题；注意级额外展示部分正文，并提供「详情」入口。
    /// </summary>
    private void ShowNoticeBar(Announcement announcement)
    {
        _noticeAnnouncement = announcement;

        var isWarn = string.Equals(announcement.Level, "warn", StringComparison.OrdinalIgnoreCase);

        NoticeTitle.Text = string.IsNullOrWhiteSpace(announcement.Title) ? "公告" : announcement.Title;

        if (TryFindResource(isWarn ? "WarnBrush" : "AccentBrush") is Brush dotBrush)
        {
            NoticeDot.Fill = dotBrush;
        }

        var hasBody = isWarn && !string.IsNullOrWhiteSpace(announcement.Content);

        if (hasBody)
        {
            NoticeSummary.Text = announcement.Content;
            NoticeSummary.Visibility = Visibility.Visible;
            NoticeDetailBtn.Visibility = Visibility.Visible;
        }
        else
        {
            NoticeSummary.Visibility = Visibility.Collapsed;
            NoticeDetailBtn.Visibility = Visibility.Collapsed;
        }

        NoticeBar.Visibility = Visibility.Visible;

        var slide = new DoubleAnimation
        {
            From = -90,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(340),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        NoticeTransform.BeginAnimation(TranslateTransform.YProperty, slide);

        Logger.Info("公告", $"以顶部提示条展示（{announcement.Level}）：{announcement.Title}");
    }

    private void HideNoticeBar()
    {
        var slide = new DoubleAnimation
        {
            From = NoticeTransform.Y,
            To = -90,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        slide.Completed += (_, _) => NoticeBar.Visibility = Visibility.Collapsed;
        NoticeTransform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void NoticeClose_Click(object sender, RoutedEventArgs e) => HideNoticeBar();

    private void NoticeDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_noticeAnnouncement is null) return;

        HideNoticeBar();

        var window = new AnnouncementWindow(_noticeAnnouncement) { Owner = this };
        window.ShowDialog();
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
