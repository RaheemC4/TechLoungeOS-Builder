using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace TechLounge.DiscordRepair
{
    // This executable runs once per launch. No service, timer, startup entry or network access.
    internal static class Launcher
    {
        internal static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechLoungeTweaks", "Discord");
        internal static readonly string DiscordRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");

        [STAThread]
        static int Main(string[] args)
        {
            return Execute(Home, DiscordRoot, args);
        }

        internal static int Execute(string home, string root, string[] args)
        {
            bool integrate = args.Length == 1 && args[0] == "--integrate";
            bool repairOnly = args.Length == 1 && args[0] == "--repair-only";
            bool locked = false;
            using (var mutex = new Mutex(false, @"Local\TechLounge.DiscordRepair." + WindowsIdentity.GetCurrent().User.Value))
            {
                try
                {
                    try { locked = mutex.WaitOne(3000); }
                    catch (AbandonedMutexException) { locked = true; }
                    if (!locked) throw new IOException("Another launch is repairing Discord.");
                    if (integrate)
                    {
                        ShellLinks.Integrate(home, root);
                        Log(home, "shortcuts-configured");
                        return 0;
                    }
                    string result = Repair(home, root, IsRunning(root));
                    Log(home, result);
                }
                catch (Exception error)
                {
                    Log(home, "repair-deferred: " + error.Message);
                    if (integrate || repairOnly) return 1;
                }
                finally { if (locked) mutex.ReleaseMutex(); }
            }
            if (repairOnly) return 0;
            try
            {
                // Preserve Discord's vendor launch/update path. Its own update restart may bypass us.
                StartDiscord(root, args);
                Log(home, "discord-started");
                return 0;
            }
            catch (Exception error) { Log(home, "launch-failed: " + error.Message); return 1; }
        }

        internal static void StartDiscord(string root, string[] args)
        {
            string exe = Path.Combine(root, "Discord.exe");
            if (!File.Exists(exe))
            {
                string latest = LatestVersion(root);
                if (latest == null) throw new FileNotFoundException("Discord is not installed.");
                exe = Path.Combine(latest, "Discord.exe");
            }
            Process.Start(new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote))) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false });
        }

        internal static string Quote(string value)
        {
            // Windows CommandLineToArgvW quoting, including a trailing backslash.
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
                result.Append(ch); slashes = 0;
            }
            result.Append('\\', slashes * 2).Append('"');
            return result.ToString();
        }

        internal static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static void OrdinaryPath(string path)
        {
            for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked paths are not supported: " + current);
        }

        internal static string LatestVersion(string root)
        {
            if (!Directory.Exists(root)) return null;
            OrdinaryPath(root);
            Version newest = null;
            string found = null;
            foreach (string folder in Directory.GetDirectories(root, "app-*"))
            {
                Version version;
                if (!Version.TryParse(Path.GetFileName(folder).Substring(4), out version)) continue;
                if (!File.Exists(Path.Combine(folder, "Discord.exe")) || !File.Exists(Path.Combine(folder, "resources", "app.asar"))) continue;
                OrdinaryPath(folder);
                if (newest == null || version > newest) { newest = version; found = folder; }
            }
            return found;
        }

        internal static bool IsRunning(string root)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var process in Process.GetProcessesByName("Discord"))
                using (process)
                {
                    try { if (ProcessImage(process.Id).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { return true; } // Cannot prove it is safe to replace.
                }
            return false;
        }

        // MainModule requires memory-read permission and can reject a different
        // signed-in user's Discord even though its executable path is queryable.
        internal static string ProcessImage(int id)
        {
            IntPtr handle=OpenProcess(0x1000,false,id);
            if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try {
                int length=32768;var path=new StringBuilder(length);
                if(!QueryFullProcessImageName(handle,0,path,ref length))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return path.ToString();
            }finally{CloseHandle(handle);}
        }
        [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int id);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder name,ref int size);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);

        internal static bool IsOpenAsar(string path)
        {
            if (new FileInfo(path).Length > 5 * 1024 * 1024) return false;
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 16 || BitConverter.ToUInt32(data, 0) != 4) return false;
            uint headerLength = BitConverter.ToUInt32(data, 12);
            if (headerLength == 0 || headerLength > 1024 * 1024 || headerLength > data.Length - 16) return false;
            try
            {
                var header = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(data, 16, (int)headerLength));
                var files = (Dictionary<string, object>)header["files"];
                if (!files.ContainsKey("asarUpdate.js") || !files.ContainsKey("appSettings.js") || !files.ContainsKey("index.js")) return false;
                var index = (Dictionary<string, object>)files["index.js"];
                long start = 8L + BitConverter.ToUInt32(data, 4) + Convert.ToInt64(index["offset"]);
                int size = Convert.ToInt32(index["size"]);
                if (start < 16 || size < 0 || start + size > data.Length) return false;
                string source = Encoding.UTF8.GetString(data, (int)start, size);
                return source.Contains("global.oaVersion") && source.Contains("OpenAsar");
            }
            catch (Exception) { return false; }
        }

        internal static string Repair(string home, string root, bool running)
        {
            if (running) return "already-running; repair waits for a full quit";
            string folder = LatestVersion(root);
            if (folder == null) return "discord-not-installed";
            string target = Path.Combine(folder, "resources", "app.asar");
            foreach (string renamed in new[] { "app.orig.asar", "_app.asar" })
            {
                string other = Path.Combine(folder, "resources", renamed);
                if (File.Exists(other)) { target = other; break; }
            }
            OrdinaryPath(target);
            if (IsOpenAsar(target)) return "openasar-present: " + Path.GetFileName(folder);
            string cache = Path.Combine(home, "openasar.asar");
            string receipt = Path.Combine(home, "openasar.sha256");
            OrdinaryPath(cache); OrdinaryPath(receipt);
            string expected = File.ReadAllText(receipt).Trim();
            if (expected.Length != 64 || !string.Equals(Hash(cache), expected, StringComparison.OrdinalIgnoreCase) || !IsOpenAsar(cache))
                throw new IOException("The saved OpenAsar copy failed validation; Discord was left unchanged.");
            string originalHash = Hash(target);
            string backups = Path.Combine(home, "backups", Path.GetFileName(folder));
            OrdinaryPath(backups);
            Directory.CreateDirectory(backups);
            string backup = Path.Combine(backups, Path.GetFileName(target) + "." + originalHash + ".backup");
            OrdinaryPath(backup);
            if (!File.Exists(backup)) File.Copy(target, backup);
            if (Hash(backup) != originalHash) throw new IOException("Discord backup failed validation.");
            string staged = target + ".tlt-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(cache, staged);
                if (!string.Equals(Hash(staged), expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Staged OpenAsar failed validation.");
                // A process/update race leaves the original intact if Windows refuses replacement.
                File.Replace(staged, target, null);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
            return "openasar-restored: " + Path.GetFileName(folder);
        }

        internal static void Log(string home, string message)
        {
            try
            {
                Directory.CreateDirectory(home);
                string path = Path.Combine(home, "launcher.log");
                if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
            }
            catch (Exception) { } // Logging must not prevent Discord startup.
        }
    }
}
