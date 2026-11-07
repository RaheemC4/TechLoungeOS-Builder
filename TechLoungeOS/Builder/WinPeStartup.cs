using System.Text;

namespace TechLoungeOS;

internal static class WinPeStartup
{
    internal static byte[] StandardShell(byte[] startnet, string? commandLine, uint? setupType,
        uint? setupInProgress, string? keyboard)
    {
        // An absent INI is safe to extend only for the observed stock startup path.
        // Unknown initialization scripts must not be skipped or run twice.
        if (startnet.Length > 65536 || startnet.Contains((byte)0) ||
            !string.Equals(Encoding.Latin1.GetString(startnet).Trim(), "wpeinit", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(commandLine, "winpeshl.exe", StringComparison.OrdinalIgnoreCase) ||
            setupType != 1 || setupInProgress != 1)
            throw new IOException("This Windows PE has custom startup instructions. Its startup was left unchanged.");
        if (keyboard != null && !System.Text.RegularExpressions.Regex.IsMatch(keyboard, @"\A[0-9A-F]{4}:[0-9A-F]{8}\z"))
            throw new IOException("Invalid WinPE keyboard selection.");
        return Encoding.ASCII.GetBytes("[LaunchApps]\r\n%SYSTEMROOT%\\System32\\wpeinit.exe\r\n" +
            (keyboard == null ? "" : "%SYSTEMROOT%\\System32\\wpeutil.exe, SetKeyboardLayout " + keyboard + "\r\n") +
            WinPeBackground.LaunchLine + "\r\n");
    }

    internal static byte[] ReadStandardShell(string mount, string? keyboard)
    {
        var system = Path.Combine(mount, @"Windows\System32");
        foreach (var file in new[] { Path.Combine(system, "startnet.cmd"), Path.Combine(system, "wpeinit.exe"), Path.Combine(mount, @"sources\setup.exe") })
            SourcePreservation.RequireImageFile(file, 1, long.MaxValue);
        var startnet = Path.Combine(system, "startnet.cmd");
        SourcePreservation.RequireImageFile(startnet, 1, 65536);
        var hive = Path.Combine(system, @"config\SYSTEM");
        return StandardShell(File.ReadAllBytes(startnet),
            SourcePreservation.ReadStringValue(hive, "Setup", "CmdLine"),
            SourcePreservation.ReadDwordValue(hive, "Setup", "SetupType"),
            SourcePreservation.ReadDwordValue(hive, "Setup", "SystemSetupInProgress"), keyboard);
    }
}
