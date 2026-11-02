using System.Windows;

namespace HWZCodeBridge.Themes;

/// <summary>主题切换：浅色为基线字典，深色字典后置合并覆盖颜色键。</summary>
public static class ThemeManager
{
    private const string DarkMarker = "Theme.Dark.xaml";

    public static void Apply(string theme)
    {
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;

        var existing = merged.FirstOrDefault(d =>
            d.Source is not null && d.Source.OriginalString.Contains(DarkMarker));

        if (existing is not null) merged.Remove(existing);

        if (!string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)) return;

        merged.Add(new ResourceDictionary
        {
            Source = new Uri("Themes/Theme.Dark.xaml", UriKind.Relative)
        });
    }
}
