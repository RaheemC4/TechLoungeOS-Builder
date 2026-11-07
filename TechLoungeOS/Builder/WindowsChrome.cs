using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TechLoungeOS;

internal static class WindowsChrome
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    internal static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var dark = SystemParameters.HighContrast ? 0 : 1;
        // Keep the native caption, system menu and resize behaviour. High contrast belongs to Windows.
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
}
