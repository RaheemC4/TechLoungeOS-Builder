using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using TechLoungeOS;

internal static class PeArtworkChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        var mount=Path.Combine(root,"startup-artwork");var system=Path.Combine(mount,@"Windows\System32");Directory.CreateDirectory(system);
        var active=Path.Combine(system,"setup.bmp");File.WriteAllText(active,"original Ghost visual fixture");
        var retained=Path.Combine(mount,@"Windows\WinSxS\original-setup.bmp");Directory.CreateDirectory(Path.GetDirectoryName(retained)!);
        if(!CreateHardLinkW(retained,active,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        File.SetAttributes(active,FileAttributes.ReadOnly|FileAttributes.Archive);
        var oldHash=BootableDrive.Hash(active);var oldAttributes=File.GetAttributes(active);
        var software=Path.Combine(system,@"config\SOFTWARE");
        SettingsChecks.WriteValues(software,[(@"Microsoft\Windows NT\CurrentVersion\WinPE","CustomBackground",2,Encoding.Unicode.GetBytes("%SystemRoot%\\system32\\setup.bmp\0"))]);
        var hiveHash=BootableDrive.Hash(software);
        var setup=Path.Combine(mount,@"sources\background.bmp");Directory.CreateDirectory(Path.GetDirectoryName(setup)!);File.WriteAllText(setup,"original purple setup fixture");
        var evidence=Path.Combine(root,"startup-artwork-evidence");var expected=WinPeArtwork.Stage(mount,evidence);
        check(expected.Count==3&&expected.All(p=>BootableDrive.Hash(Path.Combine(mount,p.Key))==p.Value),"early WinPE, active custom background and Windows Setup receive independently checked packaged artwork");
        check(BootableDrive.Hash(retained)==oldHash&&File.GetAttributes(retained)==oldAttributes&&File.GetAttributes(active)==oldAttributes,
            "replacing a read-only active picture preserves original component-store hard-link data and attributes");
        check(BootableDrive.Hash(software)==hiveHash,"startup branding preserves original background configuration and all registry settings");
        check(Directory.GetFiles(evidence,"original-picture",SearchOption.AllDirectories).Any(p=>BootableDrive.Hash(p)==oldHash),"original startup picture retained outside the image as recovery evidence");
        var bitmap=WinPeArtwork.Read("startup.bmp");var jpeg=WinPeArtwork.Read("startup.jpg");
        check(bitmap[0]=='B'&&bitmap[1]=='M'&&BitConverter.ToInt32(bitmap,18)==1920&&BitConverter.ToInt32(bitmap,22)==1080&&jpeg[0]==255&&jpeg[1]==216,
            "background assets retain real BMP/JPEG encodings and full-HD bitmap dimensions");
        check(expected[@"Windows\System32\setup.bmp"]==Convert.ToHexString(SHA256.HashData(bitmap))&&expected[@"sources\background.bmp"]==expected[@"Windows\System32\setup.bmp"],
            "early custom shell and installer use identical matching artwork");
        var unknown=Path.Combine(root,"unknown-startup-artwork");var unknownHive=Path.Combine(unknown,@"Windows\System32\config\SOFTWARE");
        SettingsChecks.WriteValues(unknownHive,[(@"Microsoft\Windows NT\CurrentVersion\WinPE","CustomBackground",2,Encoding.Unicode.GetBytes("X:\\Vendor\\unknown.bmp\0"))]);
        try{WinPeArtwork.Stage(unknown,Path.Combine(root,"unknown-artwork-evidence"));throw new Exception("Unknown startup picture accepted");}
        catch(IOException){check(!File.Exists(Path.Combine(unknown,@"Windows\System32\winpe.jpg")),"unknown startup background stops before staging rather than silently leaving mixed branding");}
        var ghost=Path.Combine(root,"ghost-startup-artwork");
        var ghostHive=Path.Combine(ghost,@"Windows\System32\config\SOFTWARE");
        // Observed in the real Ghost boot image, not inferred from the filename.
        SettingsChecks.WriteValues(ghostHive,[(@"Microsoft\Windows NT\CurrentVersion\WinPE","CustomBackground",2,Encoding.Unicode.GetBytes("%systemroot%\\system32\\winre.jpg\0"))]);
        var ghostPicture=Path.Combine(ghost,@"Windows\System32\winre.jpg");File.WriteAllText(ghostPicture,"inert old Ghost startup artwork");
        var ghostHiveHash=BootableDrive.Hash(ghostHive);
        var recovery=Path.Combine(ghost,@"Windows\System32\Recovery\winre.wim");Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
        File.WriteAllText(recovery,"inert recovery-image sentinel");var recoveryHash=BootableDrive.Hash(recovery);
        var ghostPlan=WinPeArtwork.Stage(ghost,Path.Combine(root,"ghost-artwork-evidence"));
        check(ghostPlan.Count==2&&ghostPlan[@"Windows\System32\winre.jpg"]==Convert.ToHexString(SHA256.HashData(jpeg))&&BootableDrive.Hash(ghostPicture)==ghostPlan[@"Windows\System32\winre.jpg"],
            "observed Ghost winre.jpg startup reference accepts the exact active picture with verified artwork");
        check(BootableDrive.Hash(ghostHive)==ghostHiveHash&&BootableDrive.Hash(recovery)==recoveryHash,
            "Ghost startup artwork retains its registry reference and does not change the recovery WIM");
        File.SetAttributes(active,FileAttributes.Normal);File.SetAttributes(retained,FileAttributes.Normal);
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool CreateHardLinkW(string target,string source,nint security);
}
