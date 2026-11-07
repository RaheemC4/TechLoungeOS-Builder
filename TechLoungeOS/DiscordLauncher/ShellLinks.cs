using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TechLounge.DiscordRepair
{
    internal static class ShellLinks
    {
        const string AppId = "com.squirrel.Discord.Discord";
        [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr Value;
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHGetPropertyStoreFromParsingName(string path, IntPtr bind, uint flags, ref Guid iid, out IPropertyStore store);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(uint eventId, uint flags, string item, IntPtr other);

        internal static void SetAppId(string path)
        {
            IPropertyStore store;
            Guid iid = typeof(IPropertyStore).GUID;
            SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 2, ref iid, out store);
            var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            var value = new PropVariant { Type = 31, Value = Marshal.StringToCoTaskMemUni(AppId) };
            try { store.SetValue(ref key, ref value); store.Commit(); }
            finally { Marshal.FreeCoTaskMem(value.Value); Marshal.FinalReleaseComObject(store); }
        }

        internal static void Update(string path, string home, string root, bool create)
        {
            if (!create && !File.Exists(path)) return;
            Launcher.OrdinaryPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string exe = Path.Combine(home, "DiscordLauncher.exe");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic link = shell.CreateShortcut(path);
            try
            {
                string oldTarget = link.TargetPath;
                string oldArgs = link.Arguments;
                bool existing = File.Exists(path);
                bool ours = string.Equals(oldTarget, exe, StringComparison.OrdinalIgnoreCase);
                bool discord = oldTarget.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
                bool legacy = oldArgs.Replace('/', '\\').IndexOf(@"\OpenAsarRepair\Repair-Discord.ps1", StringComparison.OrdinalIgnoreCase) >= 0;
                if (existing && !ours && !discord && !legacy) return;
                if (existing && !ours)
                {
                    string key;
                    using (var sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-", "");
                    string backups = Path.Combine(home, "shortcut-backups");
                    Launcher.OrdinaryPath(backups);
                    Directory.CreateDirectory(backups);
                    string backup = Path.Combine(backups, key + ".lnk");
                    if (!File.Exists(backup)) { File.Copy(path, backup); File.WriteAllText(backup + ".path.txt", path); }
                }
                link.TargetPath = exe;
                link.Arguments = "";
                link.WorkingDirectory = home;
                link.IconLocation = Path.Combine(root, "app.ico") + ",0";
                link.Description = "Discord with automatic OpenAsar repair";
                link.Save();
            }
            finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
            SetAppId(path);
            SHChangeNotify(0x00002000, 0x0005 | 0x1000, path, IntPtr.Zero);
        }

        internal static void Integrate(string home, string root)
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string start = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            Update(Path.Combine(start, "Discord.lnk"), home, root, true);
            Update(Path.Combine(start, "Discord Inc", "Discord.lnk"), home, root, false);
            Update(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Discord.lnk"), home, root, false);
            Update(Path.Combine(roaming, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Discord.lnk"), home, root, false);
            // Do not create or modify Windows startup entries, tasks or Discord's updater.
        }
    }
}
