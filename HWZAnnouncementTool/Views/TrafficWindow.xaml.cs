using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HWZAnnouncementTool.Models;
using HWZAnnouncementTool.Services;

namespace HWZAnnouncementTool.Views;

/// <summary>
/// 仓库访问统计窗口：把 GitHub Traffic（近 14 天）与本机累计存档合并展示，
/// 回答「一共多少人浏览过这个仓库」。
/// </summary>
public partial class TrafficWindow : Window
{
    private readonly string _token;
    private bool _busy;

    public TrafficWindow(string repo, string token)
    {
        InitializeComponent();

        _token = token ?? "";
        RepoInput.Text = repo ?? "";

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.ToolTip = "本机累计存档：" + TrafficArchiveStore.ArchivePath;
        await LoadAsync();
    }

    // ─────────────────────────────────────────────
    // 数据加载
    // ─────────────────────────────────────────────

    private async Task LoadAsync()
    {
        if (_busy) return;

        var repo = RepoInput.Text.Trim().Trim('/');
        if (repo.Split('/').Length != 2)
        {
            ShowHint("仓库格式应为 owner/repo，例如 HWZ-ch/hwz-code-bridge。");
            SetStatus("仓库格式不正确。");
            return;
        }

        _busy = true;
        SetBusy(true);
        SetStatus("正在读取统计数据…");

        try
        {
            var client = new RepositoryInsightsClient(_token);
            var result = await client.LoadAsync(repo, CancellationToken.None);

            if (!result.Success || result.Data is null)
            {
                ApplyArchiveOnly(repo, TrafficArchiveStore.Peek(repo));
                ShowHint(result.Message);
                SetStatus("读取失败：" + result.Message);
                return;
            }

            var data = result.Data;
            var summary = data.TrafficAvailable
                ? TrafficArchiveStore.Merge(repo, data.Days)
                : TrafficArchiveStore.Peek(repo);

            ApplySnapshot(data, summary);

            if (data.TrafficAvailable)
            {
                HideHint();
                SetStatus($"已更新 · {DateTime.Now:HH:mm:ss} · 本机存档覆盖 {summary.Days} 天。");
            }
            else
            {
                ShowHint(data.TrafficMessage + " 星标、Fork 等公开指标不受影响，本机累计存档仍可查看。",
                    data.TrafficNeedsPermission);
                SetStatus("仅显示公开数据，流量统计不可用。");
            }
        }
        catch (Exception ex)
        {
            ShowHint("读取异常：" + ex.Message);
            SetStatus("读取异常：" + ex.Message);
        }
        finally
        {
            _busy = false;
            SetBusy(false);
        }
    }

    private void ApplySnapshot(RepositorySnapshot data, ArchiveSummary summary)
    {
        RepoTitle.Text = data.Repo;
        RepoTitle.ToolTip = string.IsNullOrWhiteSpace(data.Description) ? null : data.Description;

        PublicStats.Text = $"★ {data.Stars:N0} · Fork {data.Forks:N0} · 关注 {data.Watchers:N0} · Issue {data.OpenIssues:N0}";

        var hasArchive = summary.Days > 0;

        UniqueValue.Text = hasArchive ? summary.TotalUniques.ToString("N0") : "—";
        UniqueNote.Text = hasArchive
            ? $"自 {summary.Start} 起 · 覆盖 {summary.Days} 天"
            : "暂无历史存档";

        TotalViewValue.Text = hasArchive ? summary.TotalViews.ToString("N0") : "—";
        TotalViewNote.Text = hasArchive
            ? (summary.Days > 1 ? "按日汇总，含重复访问" : "仅今日数据，明日可累积")
            : "流量数据不可用";

        RecentUniqueValue.Text = data.TrafficAvailable ? data.ViewUniques14.ToString("N0") : "—";
        RecentUniqueNote.Text = data.TrafficAvailable
            ? $"共 {data.Views14:N0} 次浏览（官方口径）"
            : "流量数据不可用";

        CloneValue.Text = data.TrafficAvailable ? data.Clones14.ToString("N0") : "—";
        CloneNote.Text = data.TrafficAvailable
            ? $"{data.CloneUniques14:N0} 个独立克隆源"
            : "需要仓库写入权限";

        ChartHint.Text = data.Days.Count > 0
            ? "更新于 " + data.FetchedAt.ToString("MM-dd HH:mm")
            : "";

        RenderChart(data.Days);
        RenderRefs(ReferrersPanel, data.Referrers, "暂无来源数据");
        RenderRefs(PathsPanel, data.Paths, "暂无入口数据");
    }

    /// <summary>网络或权限失败时，至少把本机已累积的数据显示出来。</summary>
    private void ApplyArchiveOnly(string repo, ArchiveSummary summary)
    {
        RepoTitle.Text = repo;
        PublicStats.Text = "—";

        var hasArchive = summary.Days > 0;

        UniqueValue.Text = hasArchive ? summary.TotalUniques.ToString("N0") : "—";
        UniqueNote.Text = hasArchive
            ? $"来自本机存档 · 覆盖 {summary.Days} 天"
            : "暂无历史存档";

        TotalViewValue.Text = hasArchive ? summary.TotalViews.ToString("N0") : "—";
        TotalViewNote.Text = hasArchive ? "按日汇总，含重复访问" : "暂无数据";

        RecentUniqueValue.Text = "—";
        RecentUniqueNote.Text = "读取失败";

        CloneValue.Text = "—";
        CloneNote.Text = "读取失败";

        ChartHint.Text = "";
        ChartGrid.Children.Clear();
        ChartGrid.ColumnDefinitions.Clear();
        ChartGrid.Visibility = Visibility.Collapsed;
        ChartEmpty.Visibility = Visibility.Visible;

        ReferrersPanel.Children.Clear();
        ReferrersPanel.Children.Add(EmptyLine("读取失败"));
        PathsPanel.Children.Clear();
        PathsPanel.Children.Add(EmptyLine("读取失败"));
    }

    // ─────────────────────────────────────────────
    // 图表
    // ─────────────────────────────────────────────

    private void RenderChart(IReadOnlyList<TrafficDay> days)
    {
        ChartGrid.Children.Clear();
        ChartGrid.ColumnDefinitions.Clear();

        var visible = days
            .Where(d => !string.IsNullOrWhiteSpace(d.Date))
            .OrderBy(d => d.Date, StringComparer.Ordinal)
            .ToList();

        if (visible.Count == 0)
        {
            ChartGrid.Visibility = Visibility.Collapsed;
            ChartEmpty.Visibility = Visibility.Visible;
            return;
        }

        ChartGrid.Visibility = Visibility.Visible;
        ChartEmpty.Visibility = Visibility.Collapsed;

        var max = Math.Max(1, visible.Max(d => d.Views));
        var utcToday = DateTime.UtcNow.ToString("yyyy-MM-dd");

        for (var i = 0; i < visible.Count; i++)
        {
            var day = visible[i];
            var isLatest = i == visible.Count - 1;

            ChartGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var column = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ToolTip = $"{day.Date}　浏览 {day.Views:N0} 次 · 独立访客 {day.Uniques:N0} 位"
            };

            column.Children.Add(new TextBlock
            {
                Text = day.Views > 0 ? day.Views.ToString("N0") : "",
                FontSize = 10,
                Foreground = Res("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var height = day.Views <= 0 ? 3 : Math.Max(6, day.Views * 100.0 / max);

            column.Children.Add(new Border
            {
                Height = height,
                Margin = new Thickness(4, 0, 4, 0),
                CornerRadius = new CornerRadius(3, 3, 0, 0),
                Background = Res("AccentBrush"),
                Opacity = isLatest ? 1.0 : 0.45,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });

            column.Children.Add(new TextBlock
            {
                Text = isLatest && day.Date == utcToday
                    ? "今天"
                    : day.Date.Length >= 10 ? day.Date[5..] : day.Date,
                FontSize = 10,
                Foreground = Res(isLatest ? "TextPrimaryBrush" : "TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            });

            Grid.SetColumn(column, i);
            ChartGrid.Children.Add(column);
        }
    }

    private void RenderRefs(StackPanel host, IReadOnlyList<TrafficRef> items, string emptyText)
    {
        host.Children.Clear();

        var list = items
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.Title))
            .OrderByDescending(x => x.Count)
            .Take(4)
            .ToList();

        if (list.Count == 0)
        {
            host.Children.Add(EmptyLine(emptyText));
            return;
        }

        var max = Math.Max(1, list.Max(x => x.Count));

        foreach (var item in list)
        {
            var label = string.IsNullOrWhiteSpace(item.Title) ? item.Name : item.Title;
            label = Shorten(label, 28);

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 5) };

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = Res("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = string.IsNullOrWhiteSpace(item.Title) ? item.Name : item.Title + "\n" + item.Name
            };

            var valueText = new TextBlock
            {
                Text = $"{item.Count:N0} 次 · {item.Uniques:N0} 人",
                FontSize = 11,
                Foreground = Res("TextSecondaryBrush"),
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(valueText, 1);

            head.Children.Add(nameText);
            head.Children.Add(valueText);
            row.Children.Add(head);

            // 比例条：底轨铺满，填充宽度按占比分配
            var ratio = Math.Clamp((double)item.Count / max, 0.03, 1.0);

            var track = new Grid { Height = 4, Margin = new Thickness(0, 4, 0, 0) };
            track.Children.Add(new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = Res("SurfaceAltBrush")
            });

            var fillGrid = new Grid();
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
            fillGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
            fillGrid.Children.Add(new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = Res("AccentBrush"),
                Opacity = 0.65,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });

            track.Children.Add(fillGrid);
            row.Children.Add(track);
            host.Children.Add(row);
        }
    }

    // ─────────────────────────────────────────────
    // 交互
    // ─────────────────────────────────────────────

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void RepoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await LoadAsync();
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e)
    {
        var repo = RepoInput.Text.Trim().Trim('/');
        if (repo.Split('/').Length != 2)
        {
            ShowHint("仓库格式应为 owner/repo。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/" + repo) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("打开浏览器失败：" + ex.Message);
        }
    }

    private void ClearArchive_Click(object sender, RoutedEventArgs e)
    {
        var repo = RepoInput.Text.Trim().Trim('/');

        var confirm = MessageBox.Show(
            $"将删除本机关于 {repo} 的累计浏览存档。\n\n" +
            "只影响本机记录，不会改动 GitHub 上的任何数据。\n" +
            "清空后再次刷新，会以 GitHub 当前提供的最近 14 天作为新的累计起点。\n\n" +
            "存档位置：\n" + TrafficArchiveStore.ArchivePath + "\n\n继续吗？",
            "清空本机累计", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.OK) return;

        TrafficArchiveStore.Clear(repo);

        UniqueValue.Text = "0";
        UniqueNote.Text = "已清空，刷新后重新累积";
        TotalViewValue.Text = "0";
        TotalViewNote.Text = "已清空";

        SetStatus("已清空本机累计存档。");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ─────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────

    private void SetBusy(bool busy)
    {
        RefreshBtn.IsEnabled = !busy;
        RepoInput.IsEnabled = !busy;
        ClearBtn.IsEnabled = !busy;
        OpenRepoBtn.IsEnabled = !busy;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void ShowHint(string text, bool showTokenHelp = false)
    {
        HintText.Text = text;
        TokenHelpBtn.Visibility = showTokenHelp ? Visibility.Visible : Visibility.Collapsed;
        HintCard.Visibility = Visibility.Visible;
    }

    private void HideHint()
    {
        HintCard.Visibility = Visibility.Collapsed;
        TokenHelpBtn.Visibility = Visibility.Collapsed;
    }

    private void TokenHelp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/settings/tokens") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("打开浏览器失败：" + ex.Message);
        }
    }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private TextBlock EmptyLine(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Margin = new Thickness(0, 3, 0, 3),
        Foreground = Res("TextSecondaryBrush")
    };

    private static string Shorten(string text, int max)
    {
        text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
