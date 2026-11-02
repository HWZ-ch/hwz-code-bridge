using System.Text;
using System.Windows;
using System.Windows.Threading;
using HWZCodeBridge.Core;
using HWZCodeBridge.Themes;

namespace HWZCodeBridge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureCreated();

        // 允许读取 GB18030，用于打开中文 Windows 下常见的 GBK 编码源文件。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 三个异常入口全部接管，确保任何 BUG 都能进日志，而不是静默闪退。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Logger.Fatal("未捕获异常", "AppDomain 级别未处理异常", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("未捕获异常", "后台任务未观察到的异常", args.Exception.ToString());
            args.SetObserved();
        };

        base.OnStartup(e);

        Logger.Info("启动", $"{AppPaths.ProductName} v{typeof(App).Assembly.GetName().Version} 已启动");
        Logger.Debug("环境", $"系统 {Environment.OSVersion} / .NET {Environment.Version}");
        Logger.Debug("环境", $"数据目录 {AppPaths.RootDir}");

        ThemeManager.Apply(SettingsStore.Current.Theme);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Fatal("界面异常", "界面线程未处理异常", e.Exception);

        MessageBox.Show(
            "程序遇到一个未处理的错误，已自动写入日志。\n\n" +
            $"错误摘要：{e.Exception.Message}\n\n" +
            "你可以到「日志」页面一键导出诊断报告，反馈给 HWZ工作室。",
            "HWZ代码互换工坊 — 出错了",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // 已记录并提示，继续运行以便用户导出日志；避免一次偶发异常直接杀掉进程。
        e.Handled = true;
    }
}
