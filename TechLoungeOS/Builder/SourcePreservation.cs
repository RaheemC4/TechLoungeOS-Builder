using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace TechLoungeOS;

public record SourceProfile(string Classification, string[] Evidence);

public static class SourcePreservation
{
    public static SourceProfile Classify(string iso, IEnumerable<string> editionNames, string? mount = null)
    {
        var evidence = new List<string>();
        var brands = new[] { "ghost spectre", "ghostspectre", "tiny11", "revios", "revi os", "atlasos" };
        foreach (var name in editionNames.Prepend(Path.GetFileNameWithoutExtension(iso)))
        {
            if (brands.Any(b => name.Replace("_", " ").Contains(b, StringComparison.OrdinalIgnoreCase)))
                evidence.Add("Custom-image branding in metadata/name: " + name);
            else if(Regex.IsMatch(name,@"\bSUPERLITE(?:SE)?\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
                evidence.Add("Custom edition naming in image metadata: "+name);
        }
        if (mount != null && Directory.Exists(Path.Combine(mount, "Ghost Toolbox")))
            evidence.Add("Ghost Toolbox folder is present in the source image.");
        if (mount != null && File.Exists(Path.Combine(mount, @"Users\Default\Desktop\Ghost Toolbox.lnk")))
            evidence.Add("Ghost Toolbox shortcut is present in the source default profile; its target may be deployed during setup.");
        return new(evidence.Count > 0 ? "Custom-image indicators found" : "Source origin unverified", evidence.ToArray());
    }

    public static void VerifyComponents(ImageServicing.Package[] beforeApps, ImageServicing.Package[] afterApps,
        ImageServicing.Feature[] beforeFeatures, ImageServicing.Feature[] afterFeatures, Component[] selected)
    {
        foreach (var app in afterApps)
            if (!beforeApps.Any(a => a.PackageName == app.PackageName))
                throw new IOException("Preservation check: a package absent from the source appeared: " + app.DisplayName);
        foreach (var app in beforeApps.Where(a => !selected.Any(c => c.Kind == "Appx" && c.Name == a.DisplayName)))
            if (!afterApps.Any(a => a.PackageName == app.PackageName))
                throw new IOException("Preservation check: an unselected source app disappeared: " + app.DisplayName);
        foreach (var feature in afterFeatures)
            if (!beforeFeatures.Any(f => f.FeatureName == feature.FeatureName))
                throw new IOException("Preservation check: a feature absent from the source appeared: " + feature.FeatureName);
        foreach (var feature in beforeFeatures.Where(f => !selected.Any(c => c.Kind == "Feature" && c.Name == f.FeatureName)))
            if (!afterFeatures.Any(f => f.FeatureName == feature.FeatureName && f.State.Equals(feature.State, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Preservation check: an unselected source feature changed: " + feature.FeatureName);
    }

    public static void VerifyServices(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        if (before.Count != after.Count || before.Any(s => !after.TryGetValue(s.Key, out var value) || value != s.Value))
            throw new IOException("Preservation check: source service, policy or default-user configuration changed. No image will be committed. Keep the affected optional component until its changes can be reviewed.");
    }

    public static IReadOnlyDictionary<string, string> CaptureConfiguration(string mount)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in CaptureServices(mount)) result.Add(item.Key, item.Value);
        foreach (var item in CaptureSettings(mount)) result.Add(item.Key, item.Value);
        return result;
    }

    internal static IReadOnlyDictionary<string,string> CaptureUpdateConfiguration(string mount)
    {
        var result=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in CaptureConfiguration(mount))result.Add(item.Key,item.Value);
        foreach(var item in CaptureUpdatePreferences(Path.Combine(mount,@"Windows\System32\config\SYSTEM"),Path.Combine(mount,@"Windows\System32\config\SOFTWARE")))
            result.Add(item.Key,item.Value);
        var tasks=TaskCacheIdentity.Capture(Path.Combine(mount,@"Windows\System32\config\SOFTWARE"));
        result.Add(TaskCacheIdentity.SemanticKey,tasks.Semantic);result.Add(TaskCacheIdentity.RegistrationKey,tasks.Registration);
        foreach(var item in WindowsUpdateMetadata.Capture(mount))result.Add(item.Key,item.Value);
        result.Add(WindowsUpdateMetadata.TaskFilesKey,WindowsUpdateMetadata.TaskFiles(mount));
        return result;
    }

    internal static JsonElement? ReadTaskCacheTree(string software)
        =>ReadRegistryTree(software,@"Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache");

    internal static JsonElement? ReadRegistryTree(string path,string branch,bool optional=false)
    {
        Policy.RequireUnlinkedPath(path);
        if(optional && !File.Exists(path))return null;
        RequireHiveFile(path);
        Check(OROpenHive(path,out var hive),"open source registry evidence");
        try
        {
            var key=hive;var status=branch.Length==0?0:OROpenKey(hive,branch,out key);
            if(status is 2 or 3)return null;
            Check(status,"open source registry branch");
            try{var count=0;return JsonSerializer.SerializeToElement(ReadKey(key,0,ref count));}
            finally{if(key!=hive)_ = ORCloseKey(key);}
        }
        finally{_ = ORCloseHive(hive);}
    }

    // Global performance/display/network preferences can live outside Services or Policies.
    // Read these independently so reviewed OS service metadata cannot hide their loss.
    internal static IReadOnlyDictionary<string,string> CaptureUpdatePreferences(string system,string software)
    {
        var result=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var branch in new[]{@"Microsoft\Windows\CurrentVersion\Internet Settings",@"Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            @"Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"})
            result.Add("preference:SOFTWARE:"+branch,HashBranch(software,branch,optionalHive:false));
        Policy.RequireUnlinkedPath(system);RequireHiveFile(system);
        Check(OROpenHive(system,out var hive),"open source preference control sets");
        string[] sets;
        try{sets=Subkeys(hive).Where(n=>Regex.IsMatch(n,@"\AControlSet\d{3}\z")).ToArray();}
        finally{_ = ORCloseHive(hive);}
        if(sets.Length==0)throw new IOException("No source control sets were readable; preference preservation is unverified.");
        foreach(var set in sets)
            foreach(var branch in new[]{@"Control\PriorityControl",@"Control\Session Manager\Memory Management",@"Control\Session Manager\kernel",
                @"Control\Power\PowerThrottling",@"Control\GraphicsDrivers",@"Control\FileSystem",@"Control\Session Manager\Environment",@"Control\CrashControl"})
                result.Add("preference:SYSTEM:"+set+"\\"+branch,HashBranch(system,set+"\\"+branch,optionalHive:false));
        return result;
    }

    public static void VerifyStagedConfiguration(IReadOnlyDictionary<string,string> before,IReadOnlyDictionary<string,string> after,string? defaultUserPolicyHash)
    {
        var expected=new Dictionary<string,string>(before,StringComparer.OrdinalIgnoreCase);
        const string userHive="default-user:NTUSER";
        if(defaultUserPolicyHash!=null && expected.ContainsKey(userHive))expected[userHive]=defaultUserPolicyHash;
        VerifyServices(expected,after);
    }

    public static string DefaultUserHash(string source)=>HashBranch(source,"",optionalHive:false);

    public static string? ReadStringValue(string source,string branch,string name)
    {
        var value=ReadSourceValue(source,branch,name);
        if(value==null)return null;
        if(value.Value.Kind is not 1 and not 2 || value.Value.Bytes.Length%2!=0)throw new IOException("Source regional setting is not a Windows string: "+name);
        return Encoding.Unicode.GetString(value.Value.Bytes).TrimEnd('\0');
    }
    public static uint? ReadDwordValue(string source,string branch,string name)
    {
        var value=ReadSourceValue(source,branch,name);
        if(value==null)return null;
        if(value.Value.Kind!=4 || value.Value.Bytes.Length!=4)throw new IOException("Source registry setting is not a DWORD: "+name);
        return BitConverter.ToUInt32(value.Value.Bytes);
    }
    private static (uint Kind,byte[] Bytes)? ReadSourceValue(string source,string branch,string name)
    {
        Policy.RequireUnlinkedPath(source);RequireHiveFile(source);
        Check(OROpenHive(source,out var hive),"open source regional settings");
        try
        {
            uint size=0;
            var status=ORGetValue(hive,branch,name,out var kind,null,ref size);
            if(status is 2 or 3)return null;
            if(status!=0 && status!=234)Check(status,"read source regional setting");
            if(size>65536)throw new IOException("Source regional setting exceeds its size limit.");
            var data=new byte[size];Check(ORGetValue(hive,branch,name,out kind,data,ref size),"read source regional setting");
            if(size!=data.Length)throw new IOException("Source regional setting changed while reading.");
            return (kind,data);
        }
        finally{_ = ORCloseHive(hive);}
    }
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORGetValue(nint key,string? branch,string name,out uint kind,byte[]? data,ref uint size);

    public static IReadOnlyDictionary<string, string> CaptureSettings(string mount)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var software = Path.Combine(mount, @"Windows\System32\config\SOFTWARE");
        foreach (var branch in new[] { "Policies", @"Microsoft\Windows\CurrentVersion\Policies",
            @"Microsoft\Windows\CurrentVersion\Run",@"Microsoft\Windows\CurrentVersion\RunOnce",
            @"WOW6432Node\Microsoft\Windows\CurrentVersion\Run",@"WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
            @"Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache" })
            result["policy:" + branch] = HashBranch(software, branch, optionalHive: false);
        result["default-user:NTUSER"] = HashBranch(Path.Combine(mount, @"Users\Default\NTUSER.DAT"), "", optionalHive: true);
        return result;
    }

    private static string HashBranch(string source, string branch, bool optionalHive)
    {
        Policy.RequireUnlinkedPath(source);
        try { _ = File.GetAttributes(source); }
        catch (Exception ex) when (optionalHive && ex is FileNotFoundException or DirectoryNotFoundException)
        { return "missing-hive"; }
        RequireHiveFile(source);
        Check(OROpenHive(source, out var hive), "open source settings hive");
        try
        {
            var key = hive;
            if (branch.Length > 0)
            {
                var error = OROpenKey(hive, branch, out key);
                if (error is 2 or 3) return "missing-key";
                Check(error, "open source policy key");
            }
            try
            {
                var count = 0;
                return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ReadKey(key, 0, ref count)))));
            }
            finally { if (key != hive) _ = ORCloseKey(key); }
        }
        finally { _ = ORCloseHive(hive); }
    }

    private static void RequireHiveFile(string source) => RequireImageFile(source, 4096, 268435456);

    public static void RequireImageFile(string source, long minimum, long maximum)
    {
        // Fresh DISM mounts expose WIM-backed files before they are hydrated. Never follow name-surrogate links.
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            using var file = CreateFileW(NativePath(source), 0, 7, 0, 3, 0x00200000, 0);
            if (file.IsInvalid || !GetFileInformationByHandleEx(file, 9, out var tag, 8) || !IsSupportedHiveTag(tag.Tag))
                throw new IOException("Source image file uses an unsupported reparse point. Only native WIM-backed files are accepted.");
        }
        var info = new FileInfo(source);
        if (info.Length < minimum || info.Length > maximum)
            throw new IOException("Source image file exceeds the supported size limits.");
    }

    internal static bool IsSupportedHiveTag(uint tag) => tag == 0x80000008; // IO_REPARSE_TAG_WIM
    internal static string NativePath(string path)
    {
        var full=Path.GetFullPath(path);
        return full.StartsWith(@"\\?\",StringComparison.Ordinal)?full:full.StartsWith(@"\\",StringComparison.Ordinal)?@"\\?\UNC\"+full[2..]:@"\\?\"+full;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag { public uint Attributes; public uint Tag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int kind, out AttributeTag info, uint size);

    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint OROpenHive(string path, out nint handle);
    [DllImport("offreg.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCloseHive(nint handle);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint OROpenKey(nint parent, string name, out nint handle);
    [DllImport("offreg.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCloseKey(nint handle);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint OREnumKey(nint handle, uint index, StringBuilder name, ref uint length, nint keyClass, nint classLength, nint time);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint OREnumValue(nint handle, uint index, StringBuilder name, ref uint nameLength, out uint type, byte[]? data, ref uint dataLength);

    public static IReadOnlyDictionary<string, string> CaptureServices(string mount)
        =>CaptureServicesHive(Path.Combine(mount,@"Windows\System32\config\SYSTEM"));

    internal static IReadOnlyDictionary<string,string> CaptureServicesHive(string source)
    {
        Policy.RequireUnlinkedPath(Path.GetDirectoryName(source)!);
        if (!File.Exists(source)) throw new IOException("Source SYSTEM hive is missing or inaccessible; service preservation cannot be checked.");
        RequireHiveFile(source);
        try
        {
            // Offline Registry reads the file into memory. No HKLM/HKU mounts, security edits or save APIs are used.
            Check(OROpenHive(source, out var hive), "open source SYSTEM hive");
            try
            {
                var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var sets = Subkeys(hive).Where(n => Regex.IsMatch(n, @"\AControlSet\d{3}\z")).ToArray();
                if (sets.Length == 0) throw new IOException("No source control sets were readable; service preservation is unverified.");
                foreach (var set in sets)
                {
                    Check(OROpenKey(hive, set + @"\Services", out var services), "open source services catalog");
                    try
                    {
                        foreach (var name in Subkeys(services))
                        {
                            Check(OROpenKey(services, name, out var key), "open service configuration");
                            try
                            {
                                var count = 0;
                                var json = JsonSerializer.Serialize(ReadKey(key, 0, ref count));
                                result[set + "\\" + name] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
                            }
                            finally { _ = ORCloseKey(key); }
                        }
                    }
                    finally { _ = ORCloseKey(services); }
                }
                return result;
            }
            finally { _ = ORCloseHive(hive); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        { throw new IOException("The servicing host needs Microsoft's Offline Registry library (offreg.dll) in System32 to verify source services.", ex); }
    }

    private static void Check(uint error, string action)
    {
        if (error != 0) throw new IOException("Cannot " + action + "; service preservation is unverified.", new Win32Exception((int)error));
    }

    private static string[] Subkeys(nint key)
    {
        var result = new List<string>();
        for (uint i = 0; i < 10000; i++)
        {
            var name = new StringBuilder(256); uint length = 256;
            var error = OREnumKey(key, i, name, ref length, 0, 0, 0);
            if (error == 259) return result.ToArray();
            Check(error, "enumerate source service keys"); result.Add(name.ToString());
        }
        throw new IOException("Source registry exceeds the 10000-subkey inspection limit.");
    }

    private static SortedDictionary<string, object?> ReadKey(nint key, int depth, ref int count)
    {
        if (depth > 64 || ++count > 10000) throw new IOException("Service registry structure exceeds inspection limits.");
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        for (uint i = 0; ; i++)
        {
            if (i >= 10000) throw new IOException("Service registry value count exceeds inspection limits.");
            var name = new StringBuilder(16384); uint nameLength = 16384, dataLength = 0;
            var error = OREnumValue(key, i, name, ref nameLength, out var kind, null, ref dataLength);
            if (error == 259) break;
            if (error is not 0 and not 234) Check(error, "inspect source service value size");
            if (dataLength > 16777216) throw new IOException("Service registry value exceeds the 16 MB inspection limit.");
            var data = new byte[dataLength]; nameLength = 16384;
            Check(OREnumValue(key, i, name, ref nameLength, out kind, data, ref dataLength), "read source service value");
            values["value:" + name] = new { kind, data = Convert.ToBase64String(data, 0, (int)dataLength) };
        }
        foreach (var name in Subkeys(key))
        {
            Check(OROpenKey(key, name, out var child), "open service subkey");
            try { values["key:" + name] = ReadKey(child, depth + 1, ref count); }
            finally { _ = ORCloseKey(child); }
        }
        return values;
    }
}
