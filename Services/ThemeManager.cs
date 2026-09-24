using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MeshCaddy.Services;

public static class ThemeManager
{
    public static void Apply(string mode)
    {
        var light = mode.Equals("Light", StringComparison.OrdinalIgnoreCase) ||
                    mode.Equals("System", StringComparison.OrdinalIgnoreCase) && WindowsUsesLightTheme();
        var colors = light
            ? new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#F4F7FA", ["PanelBrush"] = "#FFFFFF", ["CardBrush"] = "#E9EEF3",
                ["SurfaceBrush"] = "#FFFFFF", ["ViewportBrush"] = "#EDF2F5", ["BorderBrush"] = "#B7C1CB",
                ["TextPrimaryBrush"] = "#14213D", ["TextSecondaryBrush"] = "#526171", ["TextMutedBrush"] = "#718090",
                ["ButtonBrush"] = "#E1E8EE", ["OverlayBrush"] = "#CCF4F7FA", ["AccentBrush"] = "#24C7A4"
            }
            : new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#0E172B", ["PanelBrush"] = "#14213D", ["CardBrush"] = "#192846",
                ["SurfaceBrush"] = "#1C2B4D", ["ViewportBrush"] = "#0F192E", ["BorderBrush"] = "#526171",
                ["TextPrimaryBrush"] = "#F4F7FA", ["TextSecondaryBrush"] = "#C6D0DA", ["TextMutedBrush"] = "#8795A3",
                ["ButtonBrush"] = "#243657", ["OverlayBrush"] = "#CC0E172B", ["AccentBrush"] = "#24C7A4"
            };
        foreach (var (key, value) in colors)
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

        // Built-in WPF templates (ComboBox, ListBox and popup menus) consult
        // system-color resource keys instead of ordinary Foreground/Background.
        var resources = Application.Current.Resources;
        var surface = (Brush)resources["SurfaceBrush"];
        var panel = (Brush)resources["PanelBrush"];
        var text = (Brush)resources["TextPrimaryBrush"];
        var muted = (Brush)resources["TextSecondaryBrush"];
        var accent = (Brush)resources["AccentBrush"];
        resources[SystemColors.WindowBrushKey] = panel;
        resources[SystemColors.ControlBrushKey] = surface;
        resources[SystemColors.ControlLightBrushKey] = surface;
        resources[SystemColors.ControlLightLightBrushKey] = surface;
        resources[SystemColors.ControlDarkBrushKey] = (Brush)resources["BorderBrush"];
        resources[SystemColors.ControlTextBrushKey] = text;
        resources[SystemColors.WindowTextBrushKey] = text;
        resources[SystemColors.MenuBrushKey] = surface;
        resources[SystemColors.MenuTextBrushKey] = text;
        resources[SystemColors.GrayTextBrushKey] = muted;
        resources[SystemColors.HighlightBrushKey] = accent;
        resources[SystemColors.HighlightTextBrushKey] = (Brush)resources["WindowBackgroundBrush"];
        resources[SystemColors.InactiveSelectionHighlightBrushKey] = accent;
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = (Brush)resources["WindowBackgroundBrush"];
    }

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }
}
