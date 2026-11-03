using System.Windows;
using HWZCodeBridge.Core;
using HWZCodeBridge.Views;

namespace HWZCodeBridge.Updating;

/// <summary>把"检查 → 提示 → 引导更新"的交互收敛到一处，供设置页、关于页与启动检查复用。</summary>
public static class UpdateUi
{
    /// <summary>
    /// 检查更新并处理结果。
    /// </summary>
    /// <param name="owner">对话框宿主窗口。</param>
    /// <param name="quiet">true 时只在发现新版本时弹窗，其余情况静默返回（用于启动自动检查）。</param>
    /// <returns>给界面显示的一句话状态。</returns>
    public static async Task<string> CheckAndPromptAsync(Window? owner, bool quiet = false)
    {
        UpdateCheckResult result;

        try
        {
            result = await new UpdateService().CheckAsync(SettingsStore.Current.UpdateRepo, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Error("更新", "检查更新异常", ex.ToString());
            return "检查更新失败";
        }

        SettingsStore.Current.LastUpdateCheck = DateTime.Now.ToString("o");
        SettingsStore.Save();

        switch (result.Status)
        {
            case UpdateStatus.Available when result.Info is not null:
            {
                // 自动检查时尊重"跳过此版本"，手动检查时则重新提示
                if (quiet && result.Info.Version == SettingsStore.Current.SkippedVersion)
                {
                    Logger.Debug("更新", $"已跳过版本 v{result.Info.Version}，本次不提示");
                    return "已跳过当前版本";
                }

                if (!quiet)
                {
                    SettingsStore.Current.SkippedVersion = "";
                    SettingsStore.Save();
                }

                var window = new UpdateWindow(result.Info) { Owner = owner };
                window.ShowDialog();

                return window.Skipped
                    ? $"已跳过 v{result.Info.Version}"
                    : $"发现新版本 v{result.Info.Version}";
            }

            case UpdateStatus.UpToDate:
                if (!quiet)
                {
                    MessageBox.Show(result.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return result.Message;

            case UpdateStatus.Failed:
                if (!quiet)
                {
                    MessageBox.Show(result.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return result.Message;

            default:
                return result.Message;
        }
    }
}
