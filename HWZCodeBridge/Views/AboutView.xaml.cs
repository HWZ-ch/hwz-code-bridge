using System.Windows.Controls;
using HWZCodeBridge.Build;

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
}
