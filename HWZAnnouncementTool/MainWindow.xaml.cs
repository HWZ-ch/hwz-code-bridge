using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HWZAnnouncementTool.Models;
using HWZAnnouncementTool.Services;
using HWZAnnouncementTool.Views;
using Microsoft.Win32;

namespace HWZAnnouncementTool;

public partial class MainWindow : Window
{
    private ToolSettings _settings = new();
    private string _sha = "";
    private bool _loading = true;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = AnnouncementRepositoryClient.LoadSettings();

        RepoBox.Text = _settings.Repo;
        BranchBox.Text = _settings.Branch;
        if (!string.IsNullOrEmpty(_settings.Token)) TokenBox.Password = _settings.Token;

        RepoText.Text = _settings.Repo + " · " + _settings.Branch;

        TitleBox.Text = "关于本次更新";
        ContentBox.Text = "这里写公告正文。\n\n支持换行，用户在桌面版和网页版打开时都会看到这条公告。\n如需关闭公告，取消勾选左侧的「启用公告」后重新发布。";

        _loading = false;
        UpdatePreview();
        SetStatus("就绪。填写内容后可直接发布，或先「读取线上公告」查看当前线上版本。");
    }

    // ─────────────────────────────────────────────
    // 表单与预览
    // ─────────────────────────────────────────────

    private string GetLevel() => (LevelBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "info";

    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewBar is null) return;

        var level = GetLevel();
        var brushKey = level switch
        {
            "warn" => "WarnBrush",
            "important" => "DangerBrush",
            _ => "AccentBrush"
        };

        if (TryFindResource(brushKey) is Brush brush)
        {
            PreviewBar.Background = brush;
            PreviewBadge.BorderBrush = brush;
            PreviewBadgeText.Foreground = brush;
        }

        PreviewBadgeText.Text = level switch { "warn" => "注意", "important" => "重要", _ => "公告" };

        PreviewTitle.Text = string.IsNullOrWhiteSpace(TitleBox.Text) ? "（未填写标题）" : TitleBox.Text;
        PreviewContent.Text = string.IsNullOrWhiteSpace(ContentBox.Text) ? "（未填写正文）" : ContentBox.Text;
        PreviewTime.Text = "发布于 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        CharCount.Text = ContentBox.Text.Length + " 字";

        var hasAction = !string.IsNullOrWhiteSpace(ActionTextBox.Text) &&
                        (ActionUrlBox.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         ActionUrlBox.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        PreviewAction.Visibility = hasAction ? Visibility.Visible : Visibility.Collapsed;
        PreviewActionText.Text = ActionTextBox.Text;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    // ─────────────────────────────────────────────
    // 发布设置自动保存
    // ─────────────────────────────────────────────

    private void Publish_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.Repo = RepoBox.Text.Trim();
        _settings.Branch = string.IsNullOrWhiteSpace(BranchBox.Text) ? "main" : BranchBox.Text.Trim();
        _settings.Token = TokenBox.Password.Trim();

        AnnouncementRepositoryClient.SaveSettings(_settings);

        if (RepoText is not null)
        {
            RepoText.Text = _settings.Repo + " · " + _settings.Branch;
        }
    }

    private void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        TokenBox.Password = "";
        _settings.Token = "";
        AnnouncementRepositoryClient.SaveSettings(_settings);

        SetStatus("已清除本机保存的 Token。");
        MessageBox.Show("已清除本机保存的 Token。下次发布需要重新输入。", "已清除",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ─────────────────────────────────────────────
    // 读取线上
    // ─────────────────────────────────────────────

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateConnection(out var repo, out var branch, out var token)) return;

        ReadBtn.IsEnabled = false;
        SetStatus("正在读取线上公告…");

        try
        {
            var client = new AnnouncementRepositoryClient(token);
            var result = await client.FetchAsync(repo, branch, CancellationToken.None);

            if (!result.Success || result.Item is null)
            {
                SetStatus(result.Message);
                MessageBox.Show(result.Message, "读取线上公告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _sha = result.Sha;

            _loading = true;
            TitleBox.Text = result.Item.Title;
            ContentBox.Text = result.Item.Content;
            ActionTextBox.Text = result.Item.ActionText;
            ActionUrlBox.Text = result.Item.ActionUrl;
            EnabledCheck.IsChecked = result.Item.Enabled;

            LevelBox.SelectedIndex = result.Item.Level switch
            {
                "warn" => 1,
                "important" => 2,
                _ => 0
            };
            _loading = false;

            UpdatePreview();
            SetStatus($"已读取线上公告（版本 {result.Sha[..Math.Min(8, result.Sha.Length)]}）。");
        }
        catch (Exception ex)
        {
            SetStatus("读取失败：" + ex.Message);
        }
        finally
        {
            ReadBtn.IsEnabled = true;
        }
    }

    // ─────────────────────────────────────────────
    // 仓库访问统计
    // ─────────────────────────────────────────────

    private void Traffic_Click(object sender, RoutedEventArgs e)
    {
        var repo = RepoBox.Text.Trim().Trim('/');

        if (repo.Split('/').Length != 2)
        {
            MessageBox.Show(
                "请先在「发布设置」里填写仓库（owner/repo），例如 HWZ-ch/hwz-code-bridge。",
                "仓库未填写", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new TrafficWindow(repo, TokenBox.Password.Trim()) { Owner = this };
        window.ShowDialog();

        SetStatus("已查看仓库访问统计。流量数据需要 Token 具备该仓库的 Administration 读权限。");
    }

    // ─────────────────────────────────────────────
    // 导出
    // ─────────────────────────────────────────────

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var item = BuildAnnouncement();

        var dialog = new SaveFileDialog
        {
            Title = "导出公告 JSON",
            FileName = "announcement.json",
            DefaultExt = ".json",
            Filter = "JSON 文件|*.json|所有文件|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, AnnouncementRepositoryClient.Serialize(item), new UTF8Encoding(false));
            SetStatus("已导出：" + dialog.FileName);
            MessageBox.Show(
                "已导出到：\n" + dialog.FileName +
                "\n\n把该文件上传到仓库根目录（覆盖原文件）即可生效，无需 Token。",
                "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus("导出失败：" + ex.Message);
            MessageBox.Show("导出失败：" + ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─────────────────────────────────────────────
    // 发布
    // ─────────────────────────────────────────────

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateConnection(out var repo, out var branch, out var token)) return;

        if (string.IsNullOrWhiteSpace(TitleBox.Text) && string.IsNullOrWhiteSpace(ContentBox.Text))
        {
            MessageBox.Show("标题和正文不能都为空。", "内容为空", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var item = BuildAnnouncement();

        var confirm = MessageBox.Show(
            $"即将发布到 {repo}（{branch} 分支）\n\n" +
            $"标题：{item.Title}\n" +
            $"级别：{item.Level}\n" +
            $"状态：{(item.Enabled ? "启用（用户打开软件时会看到）" : "已关闭（用户不会看到）")}\n\n" +
            "发布后所有客户端下次启动即可看到。继续吗？",
            "确认发布", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        PublishBtn.IsEnabled = false;
        ReadBtn.IsEnabled = false;
        SetStatus("正在发布…");

        try
        {
            var client = new AnnouncementRepositoryClient(token);

            // 没有 sha 时先取一次，避免覆盖冲突
            if (string.IsNullOrEmpty(_sha))
            {
                var fetched = await client.FetchAsync(repo, branch, CancellationToken.None);
                if (fetched.Success) _sha = fetched.Sha;
            }

            var result = await client.PublishAsync(repo, branch, item, _sha, CancellationToken.None);

            if (!result.Success)
            {
                SetStatus(result.Message);
                MessageBox.Show(result.Message, "发布失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 重新读取以更新 sha
            var refreshed = await client.FetchAsync(repo, branch, CancellationToken.None);
            if (refreshed.Success) _sha = refreshed.Sha;

            SetStatus("发布成功 · " + DateTime.Now.ToString("HH:mm:ss"));

            MessageBox.Show(
                "发布成功，所有客户端下次启动即可看到这条公告。" +
                (string.IsNullOrEmpty(result.CommitUrl) ? "" : "\n\n提交记录：\n" + result.CommitUrl),
                "发布成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus("发布失败：" + ex.Message);
            MessageBox.Show("发布失败：" + ex.Message, "发布失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            PublishBtn.IsEnabled = true;
            ReadBtn.IsEnabled = true;
        }
    }

    // ─────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────

    private Announcement BuildAnnouncement() => new()
    {
        Id = string.IsNullOrWhiteSpace(_settings.Repo) ? "" : DateTime.Now.ToString("yyyyMMdd-HHmmss"),
        Title = TitleBox.Text.Trim(),
        Content = ContentBox.Text.Replace("\r\n", "\n"),
        Level = GetLevel(),
        Enabled = EnabledCheck.IsChecked == true,
        PublishedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
        ActionText = ActionTextBox.Text.Trim(),
        ActionUrl = ActionUrlBox.Text.Trim()
    };

    private bool ValidateConnection(out string repo, out string branch, out string token)
    {
        repo = RepoBox.Text.Trim();
        branch = string.IsNullOrWhiteSpace(BranchBox.Text) ? "main" : BranchBox.Text.Trim();
        token = TokenBox.Password.Trim();

        if (!repo.Contains('/'))
        {
            MessageBox.Show("仓库格式应为 owner/repo，例如 HWZ-ch/hwz-code-bridge。",
                "仓库格式不正确", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (token.Length == 0)
        {
            MessageBox.Show("请先在「发布设置」里填写 Personal Access Token（需要 Contents: Read and write 权限）。",
                "缺少 Token", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }
}
