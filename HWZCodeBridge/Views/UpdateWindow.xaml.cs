using System.IO;
using System.Windows;
using HWZCodeBridge.Core;
using HWZCodeBridge.Updating;

namespace HWZCodeBridge.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private readonly UpdateService _service = new();
    private CancellationTokenSource? _cts;
    private bool _busy;

    /// <summary>用户是否选择了"跳过此版本"。</summary>
    public bool Skipped { get; private set; }

    public UpdateWindow(UpdateInfo info)
    {
        InitializeComponent();

        _info = info;

        TitleText.Text = $"发现新版本 v{info.Version}";
        SubtitleText.Text = string.IsNullOrWhiteSpace(info.PublishedText)
            ? "来自 GitHub 的正式发布版本"
            : $"发布于 {info.PublishedText}";

        CurrentVersionText.Text = "v" + UpdateService.CurrentVersionText;
        LatestVersionText.Text = "v" + info.Version;
        SizeText.Text = info.SizeText;

        NotesBox.Text = string.IsNullOrWhiteSpace(info.Notes)
            ? "该版本未提供更新说明。"
            : info.Notes;
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            // 下载过程中关闭窗口 = 取消下载
            _cts?.Cancel();
            return;
        }

        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            $"将跳过 v{_info.Version}，之后不再提示这个版本。\n\n可在「设置 → 更新」中重新检查。继续吗？",
            "跳过此版本", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        SettingsStore.Current.SkippedVersion = _info.Version;
        SettingsStore.Save();

        Skipped = true;
        Logger.Info("更新", $"用户选择跳过版本 v{_info.Version}");
        Close();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        _busy = true;
        UpdateBtn.IsEnabled = false;
        SkipBtn.IsEnabled = false;
        LaterBtn.Content = "取消";

        ProgressPanel.Visibility = Visibility.Visible;
        StatusText.Text = "正在下载…";
        DownloadBar.Value = 0;

        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadBar.Value = p * 100;
                PercentText.Text = $"{p * 100:0}%";
            });

            var file = await _service.DownloadAsync(_info, progress, _cts.Token);

            StatusText.Text = "下载完成，正在重启以完成更新…";
            PercentText.Text = "100%";
            Logger.Info("更新", "更新包已就绪，准备重启替换", file);

            var target = UpdateService.GetExecutablePath();
            UpdateService.ApplyUpdateAndRestart(file, target);

            await Task.Delay(700);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消下载。";
            ResetButtons();
        }
        catch (Exception ex)
        {
            Logger.Error("更新", "下载更新失败", ex.ToString());
            StatusText.Text = "下载失败。";
            MessageBox.Show(
                "更新失败：" + ex.Message + "\n\n你可以稍后再试，或前往 GitHub 手动下载。",
                "更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            ResetButtons();
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ResetButtons()
    {
        _busy = false;
        UpdateBtn.IsEnabled = true;
        SkipBtn.IsEnabled = true;
        LaterBtn.Content = "稍后提醒";
        ProgressPanel.Visibility = Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
