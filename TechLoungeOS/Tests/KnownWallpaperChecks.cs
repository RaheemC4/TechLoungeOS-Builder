using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text.Json;
using TechLoungeOS;

internal static class KnownWallpaperChecks
{
    internal static void PreservationFixtures()
    {
        var root=Path.Combine(Path.GetTempPath(),"TLOS-artwork-preservation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var active=Path.Combine(root,"visible.jpg");
        try
        {
            File.WriteAllText(active,"Original custom artwork fixture");
            var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(active)));var attributes=File.GetAttributes(active);
            KnownWallpaperRepair.RequireArtwork(active,hash,attributes);
            void Reject(Action action)
            {try{action();throw new Exception("Changed artwork was accepted");}catch(IOException){}}
            File.SetAttributes(active,attributes|FileAttributes.Hidden);
            Reject(()=>KnownWallpaperRepair.RequireArtwork(active,hash,attributes));
            File.SetAttributes(active,attributes);File.WriteAllText(active,"Replaced by an update");
            Reject(()=>KnownWallpaperRepair.RequireArtwork(active,hash,attributes));
            File.Delete(active);Reject(()=>KnownWallpaperRepair.RequireArtwork(active,hash,attributes));
            Console.WriteLine("Artwork preservation fixtures passed: original retained; changed bytes, attributes and missing artwork rejected. No Windows image was serviced.");
        }
        finally{if(File.Exists(active))File.SetAttributes(active,FileAttributes.Normal);Directory.Delete(root,true);}
    }

    internal static async Task NativeFixtures()
    {
        await new ProcessRunner(Console.WriteLine).PowerShell("""
            if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ine '9ce10821-65ff-4a1f-9663-f07c01145563'){throw 'Exact owned fixture VM required'}
            $disk=Get-Partition -DriveLetter S|Get-Disk
            if(([string]$disk.SerialNumber).Trim() -notmatch '^TLOS-REVISION1250(?:\s+_00000001\.)?$' -or $disk.IsBoot -or $disk.IsSystem){throw 'Exact owned fixture disk required'}
            """);
        const string root=@"S:\feature-repair1609\repair-fixtures1722";
        if(Directory.Exists(root)||File.Exists(root))throw new IOException("Fresh fixture evidence required.");
        Policy.RequireUnlinkedPath(root);Directory.CreateDirectory(root);
        using var identity=WindowsIdentity.GetCurrent();var user=identity.User!.Value;var level=identity.ImpersonationLevel;
        var success=Path.Combine(root,"success");Directory.CreateDirectory(success);
        var target=Path.Combine(success,"store.jpg");var active=Path.Combine(success,"active.jpg");var donor=Path.Combine(root,"donor.jpg");
        File.WriteAllText(target,"Original custom artwork fixture");File.WriteAllText(donor,"Verified Microsoft replacement fixture");
        if(!CreateHardLinkW(active,target,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        File.SetAttributes(target,FileAttributes.ReadOnly|FileAttributes.Hidden|FileAttributes.NotContentIndexed);
        // Windows component files can grant administrators read access only. Attribute
        // restoration must still work after the temporary write ACL has been removed.
        var sourceAcl=new FileSecurity();
        sourceAcl.SetSecurityDescriptorSddlForm("D:P(A;;FA;;;SY)(A;;FR;;;BA)(A;;FR;;;"+user+")",AccessControlSections.Access);
        new FileInfo(target).SetAccessControl(sourceAcl);
        try{File.SetAttributes(target,FileAttributes.ReadOnly|FileAttributes.NotContentIndexed);throw new IOException("The restrictive fixture unexpectedly allowed ordinary attribute writes.");}
        catch(UnauthorizedAccessException){}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var original=Hash(target);var replacement=Hash(donor);
        KnownWallpaperWriteAccess.Run(()=>KnownWallpaperRepair.ReplaceVerifiedFiles(target,active,donor,Path.Combine(root,"success-evidence"),original,replacement));
        if(File.ReadAllText(target)!="Verified Microsoft replacement fixture" || File.ReadAllText(active)!="Original custom artwork fixture" ||
            File.ReadAllText(Path.Combine(root,@"success-evidence\original-store.jpg"))!="Original custom artwork fixture" ||
            (File.GetAttributes(target)&(FileAttributes.ReadOnly|FileAttributes.Hidden|FileAttributes.NotContentIndexed))!=(FileAttributes.ReadOnly|FileAttributes.Hidden|FileAttributes.NotContentIndexed))
            throw new IOException("Real NTFS hard-link isolation or attributes failed.");
        var bad=Path.Combine(root,"bad-evidence");
        var badTarget=Path.Combine(root,"bad-store.jpg");var badActive=Path.Combine(root,"bad-active.jpg");
        File.WriteAllText(badTarget,"Original custom artwork fixture");
        if(!CreateHardLinkW(badActive,badTarget,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try{KnownWallpaperWriteAccess.Run(()=>KnownWallpaperRepair.ReplaceVerifiedFiles(badTarget,badActive,donor,bad,original,new string('0',64)));throw new Exception("Unverified replacement accepted");}
        catch(IOException){if(Directory.Exists(bad)||Hash(badTarget)!=original||Hash(badActive)!=original)throw new IOException("A refused repair changed inputs.");}
        var failure=Path.Combine(root,"failure");Directory.CreateDirectory(failure);
        var failedTarget=Path.Combine(failure,"store.jpg");var failedActive=Path.Combine(failure,"active.jpg");var temporary=Path.Combine(failure,"temporary.jpg");
        File.WriteAllText(failedTarget,"Untouched source");File.WriteAllText(temporary,"Unused staging");
        if(!CreateHardLinkW(failedActive,failedTarget,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        File.SetAttributes(failedTarget,FileAttributes.Hidden|FileAttributes.NotContentIndexed);
        Directory.CreateDirectory(Path.Combine(root,"failure-evidence"));
        KnownWallpaperWriteAccess.Run(()=>
        {
            var paths=new[]{failedTarget,failedActive,failure,temporary};
            var permissions=paths.ToDictionary(p=>p,KnownWallpaperWriteAccess.ReadSecurity);
            try{KnownWallpaperWriteAccess.ReplaceEntry(failedTarget,failedActive,temporary,Path.Combine(root,"failure-evidence"),()=>throw new IOException("Deliberate fixture failure"));throw new Exception("Fixture did not fail");}
            catch(IOException error)when(error.Message=="Deliberate fixture failure"){}
            foreach(var path in paths)KnownWallpaperWriteAccess.RequireSameSecurity(path,permissions[path]);
            if(File.GetAttributes(failedTarget)!=(FileAttributes.Hidden|FileAttributes.NotContentIndexed)||File.GetAttributes(failedActive)!=(FileAttributes.Hidden|FileAttributes.NotContentIndexed))
                throw new IOException("Failure recovery changed source attributes.");
        });
        using var after=WindowsIdentity.GetCurrent();
        if(after.User!.Value!=user||after.ImpersonationLevel!=level||File.ReadAllText(failedTarget)!="Untouched source"||File.ReadAllText(failedActive)!="Untouched source")
            throw new IOException("Failure recovery or impersonation restoration failed.");
        File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(new{passed=true,toolSha256=Hash(typeof(KnownWallpaperChecks).Assembly.Location),hardLinkIsolated=true,visibleArtworkRetained=true,attributesRetained=true,
            restrictedSourceAttributesRestored=true,wrongInputsRefused=true,permissionsRestoredAfterFailure=true,callerIdentityRestored=true,realWindowsImageModified=false,utc=DateTimeOffset.UtcNow}));
        Console.WriteLine("PASS: real NTFS repair isolation, original artwork/backup, attributes, refused inputs, failure permission recovery and caller identity restoration.");
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool CreateHardLinkW(string target,string source,nint security);
}
