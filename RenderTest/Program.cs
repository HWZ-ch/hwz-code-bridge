using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HWZCodeBridge;

namespace RenderTest;

/// <summary>
/// 用 WPF 自身的离屏渲染能力给各页面截图，用于人工核对布局与配色。
/// 不依赖外部截屏工具，也不修改程序本身。
/// </summary>
internal static class Program
{
    private static string _outDir = "";

    [STAThread]
    private static void Main()
    {
        _outDir = Path.Combine(AppContext.BaseDirectory, "shots");
        Directory.CreateDirectory(_outDir);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/HWZCodeBridge;component/Themes/Theme.xaml", UriKind.Absolute)
        });

        var window = new MainWindow { Width = 1220, Height = 780, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        window.Show();
        Pump();

        Capture(window, "01-convert-light.png");

        // 在源码框填入示例，便于检查编辑区排版
        if (window.FindName("PageHost") is ContentControl host && host.Content is FrameworkElement page)
        {
            var sourceBox = page.FindName("SourceBox") as TextBox;
            if (sourceBox is not null)
            {
                sourceBox.Text = """
                                 # 计算斐波那契数列
                                 def fib(n):
                                     a = 0
                                     b = 1
                                     for i in range(n):
                                         print(a)
                                         a, b = b, a + b
                                     return a
                                 """;
                Pump();
                Capture(window, "02-convert-filled.png");
            }
        }

        var pages = new (string Nav, string File)[]
        {
            ("NavBuild", "03-build.png"),
            ("NavTools", "04-tools.png"),
            ("NavHistory", "05-history.png"),
            ("NavLog", "06-log.png"),
            ("NavSettings", "07-settings.png"),
            ("NavAbout", "08-about.png")
        };

        foreach (var (navName, file) in pages)
        {
            if (window.FindName(navName) is not RadioButton nav) continue;

            nav.IsChecked = true;
            Pump(400);
            Capture(window, file);
        }

        // 深色主题
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/HWZCodeBridge;component/Themes/Theme.Dark.xaml", UriKind.Absolute)
        });

        if (window.FindName("NavConvert") is RadioButton back) back.IsChecked = true;
        Pump(400);
        Capture(window, "09-convert-dark.png");

        window.Close();
        app.Shutdown();

        Console.WriteLine("截图完成：" + _outDir);
        foreach (var f in Directory.GetFiles(_outDir, "*.png")) Console.WriteLine("  " + Path.GetFileName(f));

        Console.Out.Flush();
        Environment.Exit(0);
    }

    private static void Pump(int extraMs = 250)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Thread.Sleep(extraMs);
        dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Capture(Window window, string fileName)
    {
        window.UpdateLayout();

        var width = (int)Math.Max(1, window.ActualWidth);
        var height = (int)Math.Max(1, window.ActualHeight);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(Path.Combine(_outDir, fileName));
        encoder.Save(stream);
    }
}
