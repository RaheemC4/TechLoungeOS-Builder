using System;
using System.IO;
using Microsoft.Win32;

namespace TechLoungeOS.AccountSetup
{
    // Windows Setup supplies the elevated, explicitly created user. Prepare only
    // this executable's runtime policy; normal first-login installers stay unelevated.
    internal static class RuntimePolicy
    {
        internal static void Prepare(RegistryKey machine, RegistryKey user, string runtime, string data)
        {
            var settings = new[] { new[] { "BrowserExecutableFolder", runtime }, new[] { "UserDataFolder", data } };
            foreach (var setting in settings)
            {
                var path = @"Software\Policies\Microsoft\Edge\WebView2\" + setting[0];
                foreach (var hive in new[] { machine, user })
                    using (var key = hive.OpenSubKey(path))
                    {
                        var old = key == null ? null : key.GetValue("TechLoungeTweaks.exe");
                        if (old != null && !string.Equals(old as string, setting[1], StringComparison.OrdinalIgnoreCase))
                            throw new IOException("An existing app-specific WebView2 policy differs.");
                    }
            }
            foreach (var setting in settings)
            {
                var path = @"Software\Policies\Microsoft\Edge\WebView2\" + setting[0];
                using (var key = user.CreateSubKey(path))
                {
                    if (key == null) throw new IOException("Cannot prepare the app-specific WebView2 policy.");
                    key.SetValue("TechLoungeTweaks.exe", setting[1], RegistryValueKind.String);
                    if (!string.Equals(key.GetValue("TechLoungeTweaks.exe") as string, setting[1], StringComparison.OrdinalIgnoreCase))
                        throw new IOException("App-specific WebView2 policy readback failed.");
                }
            }
        }
    }
}
