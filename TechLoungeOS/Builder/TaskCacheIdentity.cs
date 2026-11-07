using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TechLoungeOS;

// Servicing may re-register a task under a new GUID. Keep every other value,
// action, trigger and security descriptor in the comparison.
internal static class TaskCacheIdentity
{
    internal const string RawKey=@"policy:Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache";
    internal const string SemanticKey="identity:scheduled-tasks";
    internal const string RegistrationKey="identity:scheduled-task-registration";
    internal static (string Semantic,string Registration) Capture(string software)
    {
        var tree=SourcePreservation.ReadTaskCacheTree(software);
        if(tree is null)return ("missing-key","missing-key");
        try{return (Hash(tree.Value),RegistrationHash(tree.Value));}
        catch(InvalidDataException){return ("unreviewed-task-identities","unreviewed-task-identities");}
        // This marker never grants an exception. The original raw branch hash
        // remains mandatory for stripped or otherwise ambiguous task caches.
    }
    internal static string Hash(JsonElement tree)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(tree))));
    internal static string RegistrationHash(JsonElement tree)
    {
        var copy=JsonNode.Parse(tree.GetRawText()) as JsonObject??throw Invalid();
        if(copy["key:Tasks"] is JsonObject tasks)
            foreach(var task in tasks.Select(p=>p.Value).OfType<JsonObject>())
                foreach(var value in new[]{"Triggers","Hash","DynamicInfo"})task.Remove("value:"+value);
        // Compiled actions remain in this supplemental fingerprint. A migration
        // also requires the exact full semantic hash, including triggers, plus
        // the original raw cache and both task-file/ACL hashes. An empty offline
        // Tasks directory must never turn into permission to ignore triggers.
        return Hash(JsonSerializer.SerializeToElement(copy));
    }
    internal static string Canonical(JsonElement tree)
    {
        var ids=new Dictionary<Guid,string>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tasks=new Dictionary<Guid,JsonElement>();
        if(tree.ValueKind!=JsonValueKind.Object)throw Invalid();
        if(tree.TryGetProperty("key:Tasks",out var taskRoot))
            foreach(var property in taskRoot.EnumerateObject())
            {
                if(!property.Name.StartsWith("key:",StringComparison.Ordinal) || !Guid.TryParseExact(property.Name[4..],"B",out var id) || !tasks.TryAdd(id,property.Value))throw Invalid();
            }
        if(tree.TryGetProperty("key:Tree",out var nameRoot))Collect(nameRoot,"",0);
        if(ids.Count!=tasks.Count)throw Invalid($"Named task identities ({ids.Count}) do not match task records ({tasks.Count}).");
        return JsonSerializer.Serialize(Normalise(tree,[],0));

        void Collect(JsonElement node,string path,int depth)
        {
            if(depth>64 || node.ValueKind!=JsonValueKind.Object)throw Invalid();
            if(node.TryGetProperty("value:Id",out var value))
            {
                if(path.Length==0 || !Guid.TryParseExact(Text(value),"B",out var id) || !ids.TryAdd(id,path) || !names.Add(path) ||
                    !tasks.TryGetValue(id,out var task) || !task.TryGetProperty("value:Path",out var stored) || !string.Equals(Text(stored),path,StringComparison.OrdinalIgnoreCase))throw Invalid();
            }
            foreach(var property in node.EnumerateObject().Where(p=>p.Name.StartsWith("key:",StringComparison.Ordinal)))
            {
                var part=property.Name[4..];if(part.Length==0 || part.Contains('\\'))throw Invalid();
                Collect(property.Value,path+"\\"+part,depth+1);
            }
        }
        SortedDictionary<string,object?> Normalise(JsonElement node,string[] path,int depth)
        {
            if(depth>64 || node.ValueKind!=JsonValueKind.Object)throw Invalid();
            var result=new SortedDictionary<string,object?>(StringComparer.Ordinal);
            foreach(var property in node.EnumerateObject())
            {
                var key=property.Name;
                if(key.StartsWith("key:",StringComparison.Ordinal))
                {
                    var part=key[4..];
                    if(path.Length==1 && new[]{"Tasks","Boot","Logon","Plain","Maintenance"}.Contains(path[0],StringComparer.Ordinal))
                    {
                        if(!Guid.TryParseExact(part,"B",out var id) || !ids.TryGetValue(id,out var name))throw Invalid();
                        key="key:task:"+name;
                    }
                    if(!result.TryAdd(key,Normalise(property.Value,[..path,part],depth+1)))throw Invalid();
                }
                else if(key=="value:Id" && path.Length>0 && path[0]=="Tree")
                {
                    if(!Guid.TryParseExact(Text(property.Value),"B",out var id) || !ids.TryGetValue(id,out var name) ||
                        !string.Equals("\\"+string.Join('\\',path.Skip(1)),name,StringComparison.OrdinalIgnoreCase))throw Invalid();
                    result.Add(key,new{kind=1,data=Convert.ToBase64String(Encoding.Unicode.GetBytes(name+'\0'))});
                }
                else if(key.StartsWith("value:",StringComparison.Ordinal))result.Add(key,property.Value.Clone());
                else throw Invalid();
            }
            return result;
        }
    }
    private static string Text(JsonElement value)
    {
        if(value.GetProperty("kind").GetUInt32()!=1)throw Invalid();
        byte[] data;
        try{data=Convert.FromBase64String(value.GetProperty("data").GetString()??"");}
        catch(FormatException){throw Invalid();}
        if(data.Length<2 || data.Length>65536 || data.Length%2!=0 || data[^1]!=0 || data[^2]!=0)throw Invalid();
        // Windows Task Scheduler stores these REG_SZ identities with two
        // terminators. Padding is not part of the task name or GUID.
        var text=Encoding.Unicode.GetString(data).TrimEnd('\0');
        if(text.Contains('\0'))throw Invalid();return text;
    }
    private static InvalidDataException Invalid(string? reason=null)=>new(reason??"Task-cache identities are incomplete or ambiguous; retain the strict raw comparison.");
}
