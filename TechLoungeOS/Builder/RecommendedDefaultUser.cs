using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TechLoungeOS;

// Seed only the reviewed user-policy value before Windows creates profiles. Standard
// accounts can read this policy but cannot necessarily write its protected key.
public static class RecommendedDefaultUser
{
    public static string Stage(string mount,bool includeRecommended=true)
    {
        var source=Path.Combine(mount,@"Users\Default\NTUSER.DAT");
        Policy.RequireUnlinkedPath(source);
        SourcePreservation.RequireImageFile(source,4096,256L<<20);
        var backup=Path.Combine(mount,@"ProgramData\TechLoungeOS\source-hooks");
        Policy.RequireUnlinkedPath(backup);Directory.CreateDirectory(backup);
        File.Copy(source,Path.Combine(backup,"Recommended-Default-NTUSER.DAT"),false);
        var temporary=source+".techloungeos-policy";
        Policy.RequireUnlinkedPath(temporary);
        if(File.Exists(temporary))throw new IOException("An unfinished default-profile policy file already exists.");
        Ok(OROpenHive(source,out var hive));
        try
        {
            var settings=DesktopDefaults.Settings.Where(s=>s.Scope=="User").Concat(includeRecommended?RecommendedProfile.Settings.Where(s=>s.Scope=="User"):[]);
            foreach(var setting in settings)
            {
                var key=hive;
                try
                {
                    foreach(var part in setting.Path.Split('\\'))
                    {
                        Ok(ORCreateKey(key,part,0,0,0,out var child,0));
                        if(key!=hive)Ok(ORCloseKey(key));
                        key=child;
                    }
                    var bytes=BitConverter.GetBytes(setting.Value);
                    Ok(ORSetValue(key,setting.Name,4,bytes,4));
                    uint size=4;var actual=new byte[4];
                    Ok(ORGetValue(key,null,setting.Name,out var kind,actual,ref size));
                    if(kind!=4 || size!=4 || BitConverter.ToInt32(actual)!=setting.Value)throw new IOException("Default-profile policy readback failed.");
                }
                finally{if(key!=hive)Ok(ORCloseKey(key));}
            }
            FolderViewDefaults.Seed(hive,mount);
            Ok(ORSaveHive(hive,temporary,10,0));
        }
        finally{Ok(ORCloseHive(hive));}
        // Preserve the destination's file security metadata; fail the uncommitted build
        // if the mounted filesystem cannot replace the hive safely.
        var expected=SourcePreservation.DefaultUserHash(temporary);
        File.Replace(temporary,source,null);
        return expected;
    }

    private static void Ok(uint result){if(result!=0)throw new IOException("Cannot prepare default-user Recommended policy.",new Win32Exception((int)result));}
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint OROpenHive(string path,out nint hive);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCreateKey(nint hive,string name,nint keyClass,uint options,nint security,out nint key,nint disposition);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORSetValue(nint key,string name,uint kind,byte[] data,uint size);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORGetValue(nint key,string? subkey,string name,out uint kind,byte[] data,ref uint size);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORSaveHive(nint hive,string path,uint major,uint minor);
    [DllImport("offreg.dll",ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCloseKey(nint key);
    [DllImport("offreg.dll",ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCloseHive(nint hive);
}
