using System.Runtime.InteropServices;
using TechLoungeOS;

static class DefaultPolicyChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var mount=Path.Combine(root,"default-policy");
        var hive=Path.Combine(mount,@"Users\Default\NTUSER.DAT");
        const string downloads="{885a186e-a440-4ada-812b-db871b942259}";
        const string future="{11111111-2222-3333-4444-555555555555}";
        const string view=@"\TopViews\{00000000-0000-0000-0000-000000000000}";
        const string bags=@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\1";
        byte[] Text(string value)=>System.Text.Encoding.Unicode.GetBytes(value+'\0');
        SettingsChecks.WriteValues(hive,[
            (@"Software\ExistingVendor","ExistingTweak",4,BitConverter.GetBytes(42u)),
            (FolderViewDefaults.UserPath+"\\"+downloads+view,"ColumnList",1,Text("prop:System.Size;System.ItemNameDisplay")),
            (bags+@"\Shell","GroupView",4,BitConverter.GetBytes(1u)),
            (bags+@"\Desktop","IconLayout",4,BitConverter.GetBytes(42u))]);
        var software=Path.Combine(mount,@"Windows\System32\config\SOFTWARE");
        SettingsChecks.WriteValues(software,new[]{downloads,future}.SelectMany(id=>new[]{
            (FolderViewDefaults.MachinePath+"\\"+id+view,"GroupBy",1u,Text("System.DateModified")),
            (FolderViewDefaults.MachinePath+"\\"+id+view,"SortByList",1u,Text("prop:System.ItemNameDisplay")),
            (FolderViewDefaults.MachinePath+"\\"+id+view,"LogicalViewMode",4u,BitConverter.GetBytes(3u))}));
        var softwareHash=RuntimePack.Hash(software);
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
            check(RecommendedProfile.Settings.Where(s=>s.Scope=="User").All(s=>Read(s.Path,s.Name)==s.Value),"all reviewed user Recommended values are seeded before the first desktop, with no dependency on installer timing");
            check(DesktopDefaults.Settings.Where(s=>s.Scope=="User").All(s=>Read(s.Path,s.Name)==s.Value),"new-profile desktop defaults are seeded before Explorer first starts, independently of first-login timing");
            check(Read(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","HideFileExt")==0&&
                Read(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","Hidden")==1&&
                Read(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings","TaskbarEndTask")==1,
                "new accounts show file extensions and hidden files and enable taskbar End task before first sign-in");
            string ReadText(string path,string name){
                uint size=1024;var bytes=new byte[size];Ok(ORGetValue(handle,path,name,out var kind,bytes,ref size));
                if(kind!=1)throw new Exception("Unexpected Explorer text value type");
                return System.Text.Encoding.Unicode.GetString(bytes,0,(int)size).TrimEnd('\0');
            }
            check(new[]{downloads,future}.All(id=>ReadText(FolderViewDefaults.UserPath+"\\"+id+view,"GroupBy")==""&&
                ReadText(FolderViewDefaults.UserPath+"\\"+id+view,"SortByList")=="prop:+System.ItemTypeText;+System.ItemNameDisplay"),
                "Downloads and an unknown future template default to no grouping and ascending Type, then Name");
            check(ReadText(FolderViewDefaults.UserPath+"\\"+downloads+view,"ColumnList")=="prop:System.Size;System.ItemNameDisplay"&&
                Read(FolderViewDefaults.UserPath+"\\"+future+view,"LogicalViewMode")==3&&Read(bags+@"\Desktop","IconLayout")==42,
                "folder template mode, existing custom columns and desktop icon layout are retained");
            uint missingSize=4;var missing=new byte[4];
            check(ORGetValue(handle,bags+@"\Shell","GroupView",out _,missing,ref missingSize) is 2 or 3,
                "inherited folder-view cache cannot override the requested new-profile template defaults");
            check(RuntimePack.Hash(software)==softwareHash,"folder-default preparation leaves the image SOFTWARE hive byte-exact");
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
