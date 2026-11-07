using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TechLoungeOS;

// Extra evidence for three observed Windows metadata changes. No registry writes.
internal static class WindowsUpdateMetadata
{
    internal const string TaskFilesKey="identity:scheduled-task-files";
    internal sealed record Field(string RawKey,string Hive,string Branch,string[] Path,string Value,int Prune=0,bool Optional=false);
    internal static readonly Field[] Fields=[
        new("default-user:NTUSER",@"Users\Default\NTUSER.DAT","",["Software","Microsoft","UEV","Agent"],"UserConsoleVersion",Optional:true),
        new(@"preference:SYSTEM:ControlSet001\Control\Session Manager\Memory Management",@"Windows\System32\config\SYSTEM",@"ControlSet001\Control\Session Manager\Memory Management",[],"HotPatchTableSize"),
        new("policy:Policies",@"Windows\System32\config\SOFTWARE","Policies",["Microsoft","Windows NT","Printers","DriverRanking"],"UseWindowsReadyPrintDriverRanking",2)];
    internal static string ValueKey(string raw)=>"metadata:value:"+raw;
    internal static string RemainderKey(string raw)=>"metadata:remainder:"+raw;
    internal static Dictionary<string,string> Capture(string mount)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var field in Fields)
        {
            var tree=SourcePreservation.ReadRegistryTree(Path.Combine(mount,field.Hive),field.Branch,field.Optional);
            var pair=Fingerprint(tree,field.Path,field.Value,field.Prune);
            result.Add(ValueKey(field.RawKey),pair.Value);result.Add(RemainderKey(field.RawKey),pair.Remainder);
        }
        return result;
    }
    internal static (string Value,string Remainder) Fingerprint(JsonElement? tree,string[] path,string name,int prune)
    {
        if(tree is null)return ("missing-value","missing-key");
        var root=JsonNode.Parse(tree.Value.GetRawText()) as JsonObject??throw new IOException("Invalid offline metadata tree");
        var nodes=new List<JsonObject>{root};JsonObject? parent=root;
        foreach(var part in path){parent=parent?["key:"+part] as JsonObject;if(parent is null)break;nodes.Add(parent);}
        string value="missing-value";
        if(parent?.Remove("value:"+name,out var original)==true)value=Digest(original!.ToJsonString());
        // Remove only the known empty printer-ranking containers, never other policies.
        for(int i=nodes.Count-1;i>0 && i>=path.Length-prune+1 && nodes[i].Count==0;i--)
            nodes[i-1].Remove("key:"+path[i-1]);
        return (value,Digest(root.ToJsonString()));
    }
    internal static string TaskFiles(string mount)
    {
        var root=Path.Combine(mount,@"Windows\System32\Tasks");
        if(!Directory.Exists(root))return "missing-key";
        Policy.RequireUnlinkedPath(root);var files=new SortedDictionary<string,object>(StringComparer.Ordinal);long bytes=0;
        var folders=new Stack<string>();folders.Push(root);int visited=0;
        while(folders.Count>0)
        {
            var folder=folders.Pop();Policy.RequireUnlinkedPath(folder);
            if(++visited>2048)throw new IOException("Scheduled-task directory inspection exceeds its budget.");
            foreach(var child in Directory.EnumerateDirectories(folder)){Policy.RequireUnlinkedPath(child);folders.Push(child);}
            foreach(var file in Directory.EnumerateFiles(folder))
            {
                SourcePreservation.RequireImageFile(file,1,2L<<20);
                if(files.Count>=2048 || (bytes+=new FileInfo(file).Length)>64L<<20)throw new IOException("Scheduled-task evidence exceeds its inspection budget.");
                var acl=new FileInfo(file).GetAccessControl(AccessControlSections.Owner|AccessControlSections.Group|AccessControlSections.Access)
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Owner|AccessControlSections.Group|AccessControlSections.Access);
                files.Add(Path.GetRelativePath(root,file),new{sha256=BootableDrive.Hash(file),acl});
            }
        }
        return Digest(JsonSerializer.Serialize(files));
    }
    private static string Digest(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
