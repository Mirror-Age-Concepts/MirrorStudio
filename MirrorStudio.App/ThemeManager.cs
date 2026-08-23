using Avalonia;
using Avalonia.Styling;

namespace MirrorStudio.App
{
    public static class ThemeManager
    {
        public static bool IsDark =>
            Application.Current?.RequestedThemeVariant != ThemeVariant.Light;

        public static void Apply(string themeName)
        {
            if (Application.Current == null) return;
            Application.Current.RequestedThemeVariant =
                themeName.Equals("Light", System.StringComparison.OrdinalIgnoreCase)
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
        }

        /// <summary>Flips Dark/Light and returns the new theme name.</summary>
        public static string Toggle()
        {
            bool toLight = IsDark;
            Apply(toLight ? "Light" : "Dark");
            return toLight ? "Light" : "Dark";
        }
    }
}
