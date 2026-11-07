using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TechLounge.DiscordRepair
{
    internal static class Checks
    {
        static int passed;
        static void Assert(bool value, string name) { if (!value) throw new Exception(name); passed++; }
        static string VersionFolder(string root, string version)
        {
            string folder = Path.Combine(root, "app-" + version);
            Directory.CreateDirectory(Path.Combine(folder, "resources"));
            File.WriteAllText(Path.Combine(folder, "Discord.exe"), "fixture");
            File.WriteAllText(Path.Combine(folder, "resources", "app.asar"), "stock-" + version);
            return folder;
        }
        static byte[] Asar(string version)
        {
            string code = "global.oaVersion='" + version + "';log('Init','OpenAsar');";
            var entry = new { size = Encoding.UTF8.GetByteCount(code), offset = "0" };
            string json = new JavaScriptSerializer().Serialize(new { files = new Dictionary<string, object> {
                { "index.js", entry }, { "appSettings.js", new { size = 0, offset = "0" } }, { "asarUpdate.js", new { size = 0, offset = "0" } }
            }});
            byte[] header = Encoding.UTF8.GetBytes(json);
            int padded = (header.Length + 3) & ~3;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(4); writer.Write(padded + 8); writer.Write(padded + 4); writer.Write(header.Length);
                writer.Write(header); writer.Write(new byte[padded - header.Length]); writer.Write(Encoding.UTF8.GetBytes(code));
                return stream.ToArray();
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--record") { File.WriteAllLines(args[1], args.Skip(2)); return 0; }
            string work = Path.Combine(Path.GetTempPath(), "TLT-DiscordChecks-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert(String.Equals(Launcher.ProcessImage(Process.GetCurrentProcess().Id),Process.GetCurrentProcess().MainModule.FileName,StringComparison.OrdinalIgnoreCase),"limited-query process path matches the native current executable without reading another process's memory");
                string root = Path.Combine(work, "Discord"), home = Path.Combine(work, "helper");
                Directory.CreateDirectory(home);
                Assert(Launcher.Repair(home, root, false) == "discord-not-installed", "missing Discord");
                string old = VersionFolder(root, "1.0.9999"), latest = VersionFolder(root, "1.0.10000");
                Directory.CreateDirectory(Path.Combine(root, "app-99.0.0"));
                Assert(Launcher.LatestVersion(root) == latest, "numeric ordering ignores incomplete install");
                string target = Path.Combine(latest, "resources", "app.asar");
                byte[] stock = File.ReadAllBytes(target), payload = Asar("nightly-fixture");
                string cache = Path.Combine(home, "openasar.asar"), receipt = Path.Combine(home, "openasar.sha256");
                File.WriteAllBytes(cache, payload); File.WriteAllText(receipt, Launcher.Hash(cache));
                Assert(Launcher.IsOpenAsar(cache), "valid ASAR recognised");
                Assert(Launcher.Repair(home, root, true).StartsWith("already-running") && File.ReadAllBytes(target).SequenceEqual(stock), "running Discord left untouched");
                using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bool refused = false;
                    try { Launcher.Repair(home, root, false); } catch (IOException) { refused = true; }
                    Assert(refused && File.ReadAllBytes(target).SequenceEqual(stock), "locked archive unchanged");
                }
                Assert(Launcher.Repair(home, root, false).StartsWith("openasar-restored"), "fresh repair");
                Assert(File.ReadAllBytes(target).SequenceEqual(payload), "exact installed bytes");
                Assert(File.ReadAllBytes(Directory.GetFiles(Path.Combine(home, "backups"), "*.backup", SearchOption.AllDirectories).Single()).SequenceEqual(stock), "exact backup");
                Assert(File.ReadAllText(Path.Combine(old, "resources", "app.asar")) == "stock-1.0.9999", "older version untouched");
                Assert(Launcher.Repair(home, root, false).StartsWith("openasar-present"), "repeat launch is idempotent");
                byte[] newer = Asar("nightly-newer"); File.WriteAllBytes(target, newer);
                Launcher.Repair(home, root, false);
                Assert(File.ReadAllBytes(target).SequenceEqual(newer), "newer OpenAsar preserved");
                File.WriteAllBytes(target, stock); File.WriteAllText(cache, "corrupt");
                bool rejected = false;
                try { Launcher.Repair(home, root, false); } catch (IOException) { rejected = true; }
                Assert(rejected && File.ReadAllBytes(target).SequenceEqual(stock), "corrupt cache cannot replace Discord");
                // Exercise the same error-to-launch path as the production entry point.
                File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(root, "Discord.exe"));
                string marker = Path.Combine(work, "started.txt");
                string[] forwarded = { "two words", "quote\"inside", "trailing\\" };
                Assert(Launcher.Execute(home, root, new[] { "--record", marker }.Concat(forwarded).ToArray()) == 0, "repair failure still launches");
                for (int i = 0; i < 50 && !File.Exists(marker); i++) Thread.Sleep(100);
                Assert(File.Exists(marker) && File.ReadAllLines(marker).SequenceEqual(forwarded), "child launched and arguments preserved");
                Assert(File.ReadAllText(Path.Combine(home, "launcher.log")).Contains("repair-deferred"), "failure recorded without dialog");
                File.WriteAllBytes(cache, payload);
                string updated = VersionFolder(root, "1.0.10001");
                Launcher.Repair(home, root, false);
                Assert(File.ReadAllBytes(Path.Combine(updated, "resources", "app.asar")).SequenceEqual(payload), "new Discord update repaired");
                File.WriteAllText(Path.Combine(updated, "resources", "app.asar"), "reinstalled");
                Launcher.Repair(home, root, false);
                Assert(File.ReadAllBytes(Path.Combine(updated, "resources", "app.asar")).SequenceEqual(payload), "same-version reinstall repaired");
                string renamed = Path.Combine(updated, "resources", "app.orig.asar");
                File.WriteAllText(renamed, "renamed-stock"); File.WriteAllText(Path.Combine(updated, "resources", "app.asar"), "mod-loader");
                Launcher.Repair(home, root, false);
                Assert(File.ReadAllBytes(renamed).SequenceEqual(payload) && File.ReadAllText(Path.Combine(updated, "resources", "app.asar")) == "mod-loader", "other mod loader preserved");
                string linkPath = Path.Combine(work, "Discord.lnk");
                ShellLinks.Update(linkPath, home, root, true);
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic link = shell.CreateShortcut(linkPath);
                try { Assert((string)link.TargetPath == Path.Combine(home, "DiscordLauncher.exe") && (string)link.Arguments == "", "shortcut uses on-demand helper"); }
                finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
                ShellLinks.Update(linkPath, home, root, true);
                Assert(File.Exists(linkPath), "shortcut reintegration succeeds");
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                link = shell.CreateShortcut(linkPath);
                try { link.TargetPath = Path.Combine(root, "Discord.exe"); link.Arguments = "--processStart Discord.exe"; link.Save(); }
                finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
                ShellLinks.Update(linkPath, home, root, false);
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                link = shell.CreateShortcut(linkPath);
                try { Assert((string)link.TargetPath == Path.Combine(home, "DiscordLauncher.exe") && (string)link.Arguments == "", "reinstall replaces vendor shortcut again"); }
                finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
                dynamic explorer = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic directory = explorer.NameSpace(work);
                dynamic item = directory.ParseName("Discord.lnk");
                try { Assert((string)item.ExtendedProperty("System.AppUserModel.ID") == "com.squirrel.Discord.Discord", "Discord taskbar grouping identity"); }
                finally { Marshal.FinalReleaseComObject(item); Marshal.FinalReleaseComObject(directory); Marshal.FinalReleaseComObject(explorer); }
                File.WriteAllText(args[0], "PASS: " + passed + " native assertions (repair, updates, reinstall, lock, cache failure, fail-open child launch, args, mod loader, shortcuts).\n");
                return 0;
            }
            catch (Exception error) { File.WriteAllText(args[0], error.ToString()); return 1; }
            finally
            {
                // Only the generated, known temporary test directory is removed.
                try { Directory.Delete(work, true); } catch (IOException) { }
            }
        }
    }
}
