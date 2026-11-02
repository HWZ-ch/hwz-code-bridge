using System.Windows;
using System.Windows.Controls;
using HWZCodeBridge.Core;
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
        Loaded += (_, _) => Navigate("convert");
        Closed += (_, _) => Logger.Info("退出", "程序正常关闭");
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
