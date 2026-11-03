using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using HWZCodeBridge.Announcements;
using HWZCodeBridge.Core;

namespace HWZCodeBridge.Views;

public partial class AnnouncementWindow : Window
{
    private readonly Announcement _announcement;

    public AnnouncementWindow(Announcement announcement)
    {
        InitializeComponent();

        _announcement = announcement;

        ApplyLevel(announcement.Level);

        LevelBadgeText.Text = announcement.LevelText;
        TitleText.Text = string.IsNullOrWhiteSpace(announcement.Title) ? "公告" : announcement.Title;
        ContentBox.Text = announcement.Content;

        if (string.IsNullOrWhiteSpace(announcement.PublishedText))
        {
            TimeText.Visibility = Visibility.Collapsed;
        }
        else
        {
            TimeText.Text = "发布于 " + announcement.PublishedText;
        }

        if (announcement.HasAction)
        {
            ActionBtn.Content = announcement.ActionText;
            ActionBtn.Visibility = Visibility.Visible;
        }
    }

    private void ApplyLevel(string level)
    {
        var brushKey = level switch
        {
            "warn" => "WarnBrush",
            "important" => "DangerBrush",
            _ => "AccentBrush"
        };

        if (TryFindResource(brushKey) is not Brush brush) return;

        LevelBar.Background = brush;
        LevelBadge.BorderBrush = brush;
        LevelBadgeText.Foreground = brush;
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (!_announcement.HasAction) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _announcement.ActionUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Warn("公告", "打开公告链接失败", ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
