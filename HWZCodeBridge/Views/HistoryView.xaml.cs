using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using HWZCodeBridge.Core;
using Microsoft.Win32;

namespace HWZCodeBridge.Views;

public partial class HistoryView : UserControl
{
    private readonly ObservableCollection<HistoryRecord> _view = new();
    private bool _loading = true;

    public HistoryView()
    {
        InitializeComponent();
        RecordList.ItemsSource = _view;
        Loaded += (_, _) => OnLoaded();
        HistoryStore.Changed += () => Dispatcher.Invoke(Reload);
    }

    private void Reload()
    {
        // XAML 解析期间控件尚未全部创建，同时避免初始化阶段做无意义的查询
        if (_loading || SearchBox is null || KindBox is null || EmptyHint is null) return;

        var keyword = SearchBox.Text.Trim();
        var kind = (KindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        var query = HistoryStore.All.AsEnumerable();

        if (kind == "convert") query = query.Where(r => r.Kind == RecordKind.Convert);
        else if (kind == "build") query = query.Where(r => r.Kind == RecordKind.Build);

        if (keyword.Length > 0)
        {
            query = query.Where(r =>
                r.SourceName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.RouteText.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                r.Status.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        _view.Clear();
        foreach (var record in query) _view.Add(record);

        EmptyHint.Visibility = _view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = HistoryStore.All.Count == 0
            ? "还没有记录。完成一次代码互换或编译后会出现在这里。"
            : "没有符合筛选条件的记录。";
    }

    private void OnLoaded()
    {
        _loading = true;
        KindBox.SelectedIndex = 0;
        _loading = false;
        Reload();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null)
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!_loading) Reload();
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) Reload();
    }

    private void DeleteOne_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HistoryRecord record) return;
        HistoryStore.Remove(record.Id);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryStore.All.Count == 0)
        {
            MessageBox.Show("当前没有可导出的记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出转换记录",
            Filter = "CSV 文件|*.csv|所有文件|*.*",
            FileName = $"HWZ转换记录-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var count = HistoryStore.Export(dialog.FileName);
            MessageBox.Show($"已导出 {count} 条记录到：\n{dialog.FileName}", "导出完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("记录", "导出记录失败", ex.Message);
            MessageBox.Show("导出失败：" + ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryStore.All.Count == 0)
        {
            MessageBox.Show("当前没有记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"确定清空全部 {HistoryStore.All.Count} 条记录吗？此操作不可撤销。",
            "清空记录", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.OK) HistoryStore.Clear();
    }
}
