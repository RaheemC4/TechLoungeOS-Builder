using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TechLoungeOS;

internal static class SettingsChecks
{
    [DllImport("offreg.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCreateHive(out nint hive);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCreateKey(nint hive, string name, nint keyClass, uint options, nint security, out nint key, nint disposition);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORSetValue(nint key, string name, uint kind, byte[] data, uint size);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORSaveHive(nint hive, string path, uint major, uint minor);
    [DllImport("offreg.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCloseKey(nint key);
    [DllImport("offreg.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCloseHive(nint hive);

    internal static void WriteHive(string path, string branch, uint value, uint type = 4)
        => WriteValues(path,[(branch,"ExistingTweak",type,BitConverter.GetBytes(value))]);
    internal static void WriteValues(string path,IEnumerable<(string Branch,string Name,uint Kind,byte[] Bytes)> values)
    {
        void Ok(uint code) { if (code != 0) throw new IOException("Fixture hive API failed: " + code); }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Ok(ORCreateHive(out var hive));
        try
        {
            foreach(var value in values)
            {
                var pathParts = value.Branch.Split('\\');
                for (var i = 1; i < pathParts.Length; i++)
                {
                    Ok(ORCreateKey(hive, string.Join('\\', pathParts.Take(i)), 0, 0, 0, out var parent, 0));
                    _ = ORCloseKey(parent);
                }
                Ok(ORCreateKey(hive,value.Branch,0,0,0,out var key,0));
                try { Ok(ORSetValue(key,value.Name,value.Kind,value.Bytes,(uint)value.Bytes.Length)); }
                finally { _ = ORCloseKey(key); }
            }
            Ok(ORSaveHive(hive, path, 6, 1));
        }
        finally { _ = ORCloseHive(hive); }
    }

    internal static void Run(string root, Action<bool, string> check)
    {
        check(SourcePreservation.IsSupportedHiveTag(0x80000008) &&
            new uint[] { 0xA000000C, 0xA0000003, 0x80000017, 0x9000001A, 0 }.All(t => !SourcePreservation.IsSupportedHiveTag(t)),
            "WIM hive placeholders are allowed without accepting symlinks, junctions or other providers");
        var mount = Path.Combine(root, "settings-source");
        var software = Path.Combine(mount, @"Windows\System32\config\SOFTWARE");
        var user = Path.Combine(mount, @"Users\Default\NTUSER.DAT");
        WriteHive(software, @"Policies\Example", 1);
        WriteHive(user, @"Software\Example", 42);
        var beforeHash = SHA256.HashData(File.ReadAllBytes(software));
        var before = SourcePreservation.CaptureSettings(mount);
        SourcePreservation.VerifyServices(before, SourcePreservation.CaptureSettings(mount));
        check(before.Count == 8 && beforeHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(software))),
            "actual offline policy/default-user/startup/task snapshots are stable and do not write the hive");
        void Reject(string label)
        {
            try { SourcePreservation.VerifyServices(before, SourcePreservation.CaptureSettings(mount)); }
            catch (IOException) { check(true, label); return; }
            throw new Exception("Preservation accepted changed settings.");
        }
        foreach(var branch in new[]{@"Microsoft\Windows\CurrentVersion\Run",@"Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache"})
        {
            File.Delete(software);WriteValues(software,[("Policies\\Example","ExistingTweak",4,BitConverter.GetBytes(1u)),(branch,"UnwantedStartup",4,BitConverter.GetBytes(1u))]);
            Reject("adding an inherited startup/task entry blocks publication: "+branch);
        }
        File.Delete(software); WriteHive(software, @"Policies\Example", 0);
        Reject("resetting an inherited machine policy blocks publication");
        File.Delete(software); WriteHive(software, @"Policies\Example", 1, type: 3);
        Reject("policy type changes are detected even when value bytes are unchanged");
        File.Delete(software); WriteHive(software, @"Policies\Example", 1);
        File.Delete(user); WriteHive(user, @"Software\Example", 43);
        Reject("overwriting an inherited default-user tweak blocks publication");
        File.Delete(user);
        var absent = SourcePreservation.CaptureSettings(mount);
        SourcePreservation.VerifyServices(absent, SourcePreservation.CaptureSettings(mount));
        check(absent["default-user:NTUSER"] == "missing-hive", "a stripped default-user hive remains explicitly absent without restoration");
        File.Delete(software);
        try { _ = SourcePreservation.CaptureSettings(mount); }
        catch (IOException) { check(true, "unreadable required settings hive cannot masquerade as empty policies"); return; }
        throw new Exception("Missing SOFTWARE hive accepted.");
    }
}
