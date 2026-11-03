using System.Windows;
using System.Windows.Controls;
using HWZCodeBridge.Build;
using HWZCodeBridge.Updating;

namespace HWZCodeBridge.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();

        var version = typeof(AboutView).Assembly.GetName().Version;
        VersionText.Text = $"版本 {version?.Major}.{version?.Minor}.{version?.Build} · 构建于 .NET 8 / WPF";

        ToolList.ItemsSource = ToolRegistry.BuiltIn;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) button.IsEnabled = false;

        try
        {
            await UpdateUi.CheckAndPromptAsync(Window.GetWindow(this));
        }
        finally
        {
            if (sender is Button b) b.IsEnabled = true;
        }
    }
}
