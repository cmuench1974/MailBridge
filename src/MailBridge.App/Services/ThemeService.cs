using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace MailBridge.App.Services;

/// <summary>
/// Applies the app theme ("System", "Light", "Dark") to every registered
/// window. Because the windows extend their content into the title bar, the
/// caption buttons (minimize, maximize, close) do not follow the app theme
/// on their own and are recolored here.
/// </summary>
public class ThemeService
{
    private readonly List<Window> _windows = new();
    private string _theme = "System";

    public void Register(Window window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);

        if (window.Content is FrameworkElement root)
        {
            // With "System" the actual theme changes without an ApplyTheme call.
            root.ActualThemeChanged += (_, _) => UpdateCaptionButtons(window);
        }

        Apply(window);
    }

    public void ApplyTheme(string theme)
    {
        _theme = theme;
        foreach (var window in _windows.ToList())
        {
            Apply(window);
        }
    }

    private void Apply(Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = _theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        UpdateCaptionButtons(window);
    }

    private static void UpdateCaptionButtons(Window window)
    {
        if (window.Content is not FrameworkElement root)
        {
            return;
        }

        var titleBar = window.AppWindow.TitleBar;
        var dark = root.ActualTheme == ElementTheme.Dark;

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(0xFF, 0x80, 0x80, 0x80) : Color.FromArgb(0xFF, 0x90, 0x90, 0x90);
        titleBar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x00, 0x00, 0x00);
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x08, 0x00, 0x00, 0x00);
        titleBar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
    }
}
