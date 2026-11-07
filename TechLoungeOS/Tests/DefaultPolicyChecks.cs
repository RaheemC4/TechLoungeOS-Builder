using System.Runtime.InteropServices;
using TechLoungeOS;

static class DefaultPolicyChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var mount=Path.Combine(root,"default-policy");
        var hive=Path.Combine(mount,@"Users\Default\NTUSER.DAT");
        SettingsChecks.WriteHive(hive,@"Software\ExistingVendor",42);
        var bytes=File.ReadAllBytes(hive);
        var preparedHash=RecommendedDefaultUser.Stage(mount);
        check(preparedHash==SourcePreservation.DefaultUserHash(hive),"default-user staging returns the independently prepared canonical hive hash for final preservation checks");
        check(File.ReadAllBytes(Path.Combine(mount,@"ProgramData\TechLoungeOS\source-hooks\Recommended-Default-NTUSER.DAT")).SequenceEqual(bytes),
            "default-profile policy preparation preserves the exact original hive backup");
        Ok(OROpenHive(hive,out var handle));
        try
        {
            uint Read(string path,string name)
            {
                uint size=4;var data=new byte[4];Ok(ORGetValue(handle,path,name,out var type,data,ref size));
                if(type!=4 || size!=4)throw new Exception("Unexpected fixture value type");
                return BitConverter.ToUInt32(data);
            }
            check(Read(@"SOFTWARE\Policies\Microsoft\Windows\Explorer","DisableSearchBoxSuggestions")==1
                && Read(@"Software\ExistingVendor","ExistingTweak")==42,
                "native offline hive preparation seeds the reviewed search policy and retains unrelated vendor settings");
            check(DesktopDefaults.Settings.Where(s=>s.Scope=="User").All(s=>Read(s.Path,s.Name)==s.Value),"new-profile desktop defaults are seeded before Explorer first starts, independently of first-login timing");
        }
        finally{Ok(ORCloseHive(handle));}
        bool refused=false;try{RecommendedDefaultUser.Stage(mount);}catch(IOException){refused=true;}
        check(refused,"a pre-existing original-hive backup prevents accidental repeated overwrite");
    }
    private static void Ok(uint value){if(value!=0)throw new Exception("Offline fixture API failed: "+value);}
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint OROpenHive(string path,out nint handle);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORGetValue(nint key,string subkey,string name,out uint type,byte[] data,ref uint size);
    [DllImport("offreg.dll",ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint ORCloseHive(nint hive);
}
