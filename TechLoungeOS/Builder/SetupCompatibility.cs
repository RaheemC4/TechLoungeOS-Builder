using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TechLoungeOS;

// Clean-install eligibility defaults, confined to WinPE. They neither disable
// firmware security nor emulate instructions missing from a target processor.
public static class SetupCompatibility
{
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[] { "BypassTPMCheck", "BypassSecureBootCheck", "BypassRAMCheck" });
    private record BootEntry(int ImageIndex, string Architecture, string Version);

    internal record BackgroundPlan(int Index,Func<string,IReadOnlyDictionary<string,string>> Stage,Action<string,string> VerifySaved);
    public static Task Prepare(ICommandRunner run,string media,string work,Action<string> log,CancellationToken ct)=>PrepareCore(run,media,work,log,ct,null);
    internal static Task PrepareWithBackground(ICommandRunner run,string media,string work,Action<string> log,CancellationToken ct,BackgroundPlan background)=>PrepareCore(run,media,work,log,ct,background);

    private static async Task PrepareCore(ICommandRunner run, string media, string work, Action<string> log, CancellationToken ct,BackgroundPlan? background)
    {
        var source = Path.Combine(media, @"sources\boot.wim");
        Policy.RequireUnlinkedPath(source);
        var inventory = await run.PowerShell($$"""
            $entries=@(Get-WindowsImage -ImagePath {{Policy.Ps(source)}} -ErrorAction Stop|ForEach-Object {
              $detail=Get-WindowsImage -ImagePath {{Policy.Ps(source)}} -Index $_.ImageIndex -ErrorAction Stop
              [pscustomobject]@{ImageIndex=[int]$detail.ImageIndex;Architecture=[string]$detail.Architecture;Version=[string]$detail.Version}
            })
            ConvertTo-Json -InputObject $entries -Compress
            """, ct);
        var entries = JsonSerializer.Deserialize<BootEntry[]>(inventory) ?? [];
        if (entries.Length is < 1 or > 32 || entries.Select(e => e.ImageIndex).Distinct().Count() != entries.Length ||
            entries.Any(e => e.ImageIndex < 1 || e.Architecture is not "0" and not "9" || !Version.TryParse(e.Version, out var v) || v.Major < 6))
            throw new IOException("Windows Setup compatibility needs readable Windows PE image metadata. No boot image was replaced.");
        if(background is not null&&!entries.Any(e=>e.ImageIndex==background.Index))throw new IOException("The selected WinPE boot image is missing from the compatibility inventory.");
        var candidate = Path.Combine(work, "setup-compatibility-boot.wim");
        var backup = Path.Combine(work,background is null?"setup-compatibility-original-boot.wim":"source-boot.wim");
        if (File.Exists(candidate) || File.Exists(backup)) throw new IOException("An existing Setup compatibility transaction needs review.");
        File.Copy(source, candidate, false); File.SetAttributes(candidate, FileAttributes.Normal);
        var receipts = new List<object>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var mount = Path.Combine(work, "compatibility-pe-" + entry.ImageIndex);
            if (Directory.Exists(mount) || File.Exists(mount)) throw new IOException("Setup compatibility mount path already exists.");
            Directory.CreateDirectory(mount); var mounted = false;
            try
            {
                mounted = true;
                await run.Dism("/Mount-Image", "/ImageFile:" + candidate, "/Index:" + entry.ImageIndex, "/MountDir:" + mount, "/CheckIntegrity");
                ct.ThrowIfCancellationRequested();
                var before = WinPeBackground.Snapshot(mount, ct);
                var services = SourcePreservation.CaptureServices(mount);
                var hive = Path.Combine(mount, @"Windows\System32\config\SYSTEM");
                var previous = BootableDrive.Hash(hive);
                ApplyHive(hive, Version.Parse(entry.Version));
                var expected=background is null?new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase):WinPeArtwork.Stage(mount,Path.Combine(work,"pe-artwork-"+entry.ImageIndex));
                if(background?.Index==entry.ImageIndex)foreach(var file in background.Stage(mount))expected.Add(file.Key,file.Value);
                if(expected.Keys.Any(p=>!WinPeArtwork.IsPicture(p)&&!p.Equals(@"Windows\System32\winpeshl.ini",StringComparison.OrdinalIgnoreCase)&&!p.StartsWith(@"Windows\System32\TechLoungeOS\",StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Unexpected file in the WinPE interface plan.");
                SourcePreservation.VerifyServices(services, SourcePreservation.CaptureServices(mount));
                var values=ReadValues(hive);
                if(Names.Any(n=>!values.TryGetValue(n,out var value)||value!=1))throw new IOException("WinPE interface preparation lost a required Setup compatibility value.");
                var after = WinPeBackground.Snapshot(mount, ct);
                const string allowed = @"Windows\System32\config\SYSTEM";
                if (before.Any(p => !after.TryGetValue(p.Key, out var hash) || (!p.Key.Equals(allowed, StringComparison.OrdinalIgnoreCase) && !expected.ContainsKey(p.Key) && p.Value != hash)))
                    throw new IOException("Setup compatibility changed an unrelated WinPE file. The source boot image was retained.");
                var added=after.Keys.Except(before.Keys,StringComparer.OrdinalIgnoreCase).ToArray();
                if(expected.Any(p=>!after.TryGetValue(p.Key,out var hash)||hash!=p.Value)||
                    !added.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(expected.Keys.Except(before.Keys,StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))
                    throw new IOException("WinPE preparation added unexpected files or changed its interface payload.");
                if(background?.Index==entry.ImageIndex)
                    File.WriteAllText(Path.Combine(work,"pe-preservation.json"),JsonSerializer.Serialize(new{before,after,added,changed=expected.Keys.Append(allowed).Where(before.ContainsKey),compatibilityIncluded=true,servicesPreserved=services.Count},new JsonSerializerOptions{WriteIndented=true}));
                receipts.Add(new { index = entry.ImageIndex, before = previous, after = BootableDrive.Hash(hive), values, originalFilesPreserved = before.Count - 1-expected.Keys.Count(before.ContainsKey), servicesPreserved = services.Count,artwork=expected.Where(p=>WinPeArtwork.IsPicture(p.Key)) });
                ct.ThrowIfCancellationRequested();
                await run.Dism("/Unmount-Image", "/MountDir:" + mount, "/Commit", "/CheckIntegrity"); mounted = false;
                log("Verified Setup TPM/Secure Boot/RAM bypasses in WinPE image " + entry.ImageIndex + "; original tools and services preserved.");
            }
            finally
            {
                if (mounted)
                    try { await run.Dism("/Unmount-Image", "/MountDir:" + mount, "/Discard"); }
                    catch (Exception ex) { log("Setup compatibility cleanup requires recovery at " + mount + ": " + ex.Message); }
            }
        }
        ct.ThrowIfCancellationRequested();
        background?.VerifySaved(source,candidate);
        File.WriteAllText(Path.Combine(work, "setup-compatibility.json"), JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }));
        File.SetAttributes(source, File.GetAttributes(source) & ~FileAttributes.ReadOnly);
        File.Replace(candidate, source, backup);
    }

    public static void ApplyHive(string source, Version version)
    {
        Policy.RequireUnlinkedPath(source); SourcePreservation.RequireImageFile(source, 4096, 256L << 20);
        if (version.Major < 6) throw new IOException("Unsupported Windows PE registry format.");
        var temporary = source + ".tlos-compatibility";
        if (File.Exists(temporary)) throw new IOException("An unfinished Setup compatibility hive needs review.");
        Ok(OROpenHive(source, out var hive));
        try
        {
            var present = ReadValues(hive);
            if (Names.All(n => present.TryGetValue(n, out var value) && value == 1)) return;
            var parent = hive;
            try
            {
                foreach (var part in new[] { "Setup", "LabConfig" })
                {
                    Ok(ORCreateKey(parent, part, 0, 0, 0, out var child, 0));
                    if (parent != hive) Ok(ORCloseKey(parent)); parent = child;
                }
                foreach (var name in Names) Ok(ORSetValue(parent, name, 4, BitConverter.GetBytes(1), 4));
            }
            finally { if (parent != hive) Ok(ORCloseKey(parent)); }
            var actual = ReadValues(hive);
            if (Names.Any(n => !actual.TryGetValue(n, out var value) || value != 1)) throw new IOException("Setup compatibility registry readback failed.");
            Ok(ORSaveHive(hive, temporary, (uint)version.Major, (uint)version.Minor));
        }
        finally { Ok(ORCloseHive(hive)); }
        var attributes = File.GetAttributes(source);
        try { File.SetAttributes(source, attributes & ~FileAttributes.ReadOnly); File.Replace(temporary, source, null); }
        finally { File.SetAttributes(source, attributes); }
        var saved = ReadValues(source);
        if (Names.Any(n => !saved.TryGetValue(n, out var value) || value != 1)) throw new IOException("Saved Setup compatibility hive failed verification.");
    }
    public static IReadOnlyDictionary<string, uint> ReadValues(string source)
    {
        Policy.RequireUnlinkedPath(source); Ok(OROpenHive(source, out var hive));
        try { return ReadValues(hive); } finally { Ok(ORCloseHive(hive)); }
    }
    private static Dictionary<string, uint> ReadValues(nint hive)
    {
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        var error = OROpenKey(hive, @"Setup\LabConfig", out var key);
        if (error is 2 or 3) return result; Ok(error);
        try
        {
            foreach (var name in Names)
            {
                uint length = 4; var bytes = new byte[4];
                error = ORGetValue(key, null, name, out var kind, bytes, ref length);
                if (error is 2 or 234) continue; Ok(error);
                if (kind == 4 && length == 4) result.Add(name, BitConverter.ToUInt32(bytes));
            }
        }
        finally { Ok(ORCloseKey(key)); }
        return result;
    }
    private static void Ok(uint error) { if (error != 0) throw new IOException("Windows PE Setup compatibility registry operation failed.", new Win32Exception((int)error)); }
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern uint OROpenHive(string path, out nint hive);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern uint OROpenKey(nint hive, string name, out nint key);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern uint ORCreateKey(nint hive, string name, nint keyClass, uint options, nint security, out nint key, nint disposition);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern uint ORSetValue(nint key, string name, uint kind, byte[] data, uint size);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern uint ORGetValue(nint key, string? subkey, string name, out uint kind, byte[] data, ref uint size);
    [DllImport("offreg.dll", CharSet = CharSet.Unicode, ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern uint ORSaveHive(nint hive, string path, uint major, uint minor);
    [DllImport("offreg.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern uint ORCloseKey(nint key);
    [DllImport("offreg.dll", ExactSpelling = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern uint ORCloseHive(nint hive);
}
