using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using HWZCodeBridge.Build;
using HWZCodeBridge.Core;
using Microsoft.Win32;

namespace HWZCodeBridge.Views;

public partial class ToolsView : UserControl
{
    private readonly ObservableCollection<ToolDefinition> _items = new();

    public ToolsView()
    {
        InitializeComponent();
        ToolList.ItemsSource = _items;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ToolRegistry.LoadPathOverrides();
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        SummaryText.Text = "正在检测本机工具…";

        await ToolRegistry.DetectAllAsync();
        RefreshList();

        var total = _items.Count;
        var ready = _items.Count(t => t.Detected);
        SummaryText.Text = $"共 {total} 个工具，本机已就绪 {ready} 个。未就绪的工具可通过官网链接获取。";
    }

    private void RefreshList()
    {
        _items.Clear();
        foreach (var tool in ToolRegistry.All)
        {
            _items.Add(tool);
        }
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        await ScanAsync();
        Logger.Info("工具", "已重新检测本机工具");
    }

    private void OpenHomepage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToolDefinition tool) return;

        if (string.IsNullOrWhiteSpace(tool.Homepage))
        {
            MessageBox.Show("该工具未登记官方主页。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenUrl(tool.Homepage);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn("工具", "打开链接失败", ex.Message);
        }
    }

    private async void SetPath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToolDefinition tool) return;

        var dialog = new OpenFileDialog
        {
            Title = $"指定 {tool.Name} 的可执行文件位置",
            Filter = "可执行文件|*.exe;*.cmd;*.bat;*.com|所有文件|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        ToolRegistry.SavePathOverride(tool.Id, dialog.FileName);
        await ScanAsync();
        Logger.Info("工具", $"{tool.Name} 的路径已指向 {dialog.FileName}");
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ToolDefinition tool) return;

        if (tool.IsBuiltIn)
        {
            MessageBox.Show(
                "内置工具条目用于登记第三方来源与许可证，不可移除。\n" +
                "如果不需要使用它，直接不选择即可。",
                "内置工具", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"确定移除自定义工具「{tool.Name}」吗？", "确认移除",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK) return;

        ToolRegistry.RemoveCustom(tool.Id);
        RefreshList();
    }

    private void ToggleAddForm_Click(object sender, RoutedEventArgs e)
    {
        AddForm.Visibility = AddForm.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void SaveCustom_Click(object sender, RoutedEventArgs e)
    {
        var tool = new ToolDefinition
        {
            Name = TxtName.Text.Trim(),
            Purpose = TxtPurpose.Text.Trim(),
            Category = "自定义",
            SourceNote = TxtSource.Text.Trim(),
            Homepage = TxtHome.Text.Trim(),
            License = TxtLicense.Text.Trim(),
            DetectCommand = TxtExe.Text.Trim(),
            VersionArgs = string.IsNullOrWhiteSpace(TxtArgs.Text) ? "--version" : TxtArgs.Text.Trim(),
            IsBuiltIn = false
        };

        if (!ToolRegistry.AddCustom(tool, out var error))
        {
            MessageBox.Show(error, "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TxtName.Clear();
        TxtPurpose.Clear();
        TxtSource.Clear();
        TxtHome.Clear();
        TxtLicense.Clear();
        TxtExe.Clear();

        AddForm.Visibility = Visibility.Collapsed;

        await ScanAsync();
        MessageBox.Show($"已添加「{tool.Name}」，来源信息已记录。", "保存成功",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
