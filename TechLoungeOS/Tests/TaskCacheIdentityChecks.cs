using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TechLoungeOS;

internal static class TaskCacheIdentityChecks
{
    internal static void Run(Action<bool,string> check)
    {
        const string first="{11111111-1111-1111-1111-111111111111}",second="{22222222-2222-2222-2222-222222222222}";
        JsonObject Value(uint kind,byte[] data)=>new(){["kind"]=kind,["data"]=Convert.ToBase64String(data)};
        JsonObject Text(string text)=>Value(1,Encoding.Unicode.GetBytes(text+'\0'));
        JsonObject Tree(string id)=>new(){
            ["key:Tasks"]=new JsonObject{["key:"+id]=new JsonObject{
                ["value:Path"]=Text(@"\Example"),["value:Actions"]=Value(3,[1,2,3]),
                ["value:Triggers"]=Value(3,[4,5,6]),["value:SecurityDescriptor"]=Value(3,[7,8,9]),["value:Flags"]=Value(4,[1,0,0,0])}},
            ["key:Tree"]=new JsonObject{["key:Example"]=new JsonObject{["value:Id"]=Text(id)}},
            ["key:Boot"]=new JsonObject{["key:"+id]=new JsonObject()}};
        string Hash(JsonObject tree)=>TaskCacheIdentity.Hash(JsonSerializer.SerializeToElement(tree));
        var original=Tree(first);var rebound=Tree(second);
        var expected=Hash(original);
        check(expected==Hash(rebound),"task identity comparison accepts re-registration under a different GUID with identical task behaviour");
        string Registration(JsonObject tree)=>TaskCacheIdentity.RegistrationHash(JsonSerializer.SerializeToElement(tree));
        check(Registration(original)==Registration(rebound),"registration evidence also remains stable when only task GUIDs change");
        var action=Tree(second);action["key:Tasks"]!["key:"+second]!["value:Actions"]!["data"]=Convert.ToBase64String(new byte[]{99});
        check(Registration(original)!=Registration(action),"compiled task commands remain in the registration fingerprint");
        var padded=Tree(second);padded["key:Tree"]!["key:Example"]!["value:Id"]=Text(second+'\0');
        check(expected==Hash(padded),"the observed Windows double-terminated REG_SZ task identity preserves the same named task");
        var embedded=Tree(first);embedded["key:Tree"]!["key:Example"]!["value:Id"]=Text(first+"\0hidden");Reject(embedded,"an embedded null cannot hide extra task identity text");
        foreach(var field in new[]{"Actions","Triggers","SecurityDescriptor","Flags"})
        {
            var changed=Tree(second);changed["key:Tasks"]!["key:"+second]!["value:"+field]!["data"]=Convert.ToBase64String(new byte[]{99,0,0,0});
            check(expected!=Hash(changed),"regenerated task GUIDs cannot hide changed "+field);
        }
        var empty=new JsonObject{["key:Tasks"]=new JsonObject(),["key:Tree"]=new JsonObject(),["key:Boot"]=new JsonObject()};
        check(expected!=Hash(empty),"task removal remains visible after identity canonicalisation");
        var uncategorised=Tree(first);uncategorised["key:Custom"]=new JsonObject{["key:"+first]=new JsonObject()};
        var other=Tree(second);other["key:Custom"]=new JsonObject{["key:"+second]=new JsonObject()};
        check(Hash(uncategorised)!=Hash(other),"unreviewed cache branches keep their literal GUID values");
        var duplicate=Tree(first);duplicate["key:Tree"]!["key:Other"]=new JsonObject{["value:Id"]=Text(first)};Reject(duplicate,"duplicate task GUID mappings are rejected");
        var orphan=Tree(first);orphan["key:Boot"]!["key:"+second]=new JsonObject();Reject(orphan,"unmapped scheduler-category GUIDs are rejected");
        var missing=Tree(first);((JsonObject)missing["key:Tree"]!).Clear();Reject(missing,"task records without a matching named identity are rejected");
        var mismatch=Tree(first);mismatch["key:Tasks"]!["key:"+first]!["value:Path"]=Text(@"\Wrong");Reject(mismatch,"mismatched task paths cannot inherit another task's identity");
        var binary=Tree(first);binary["key:Tree"]!["key:Example"]!["value:Id"]=Value(3,Guid.Parse(first).ToByteArray());Reject(binary,"unknown identity encodings remain unreviewed");
        void Reject(JsonObject value,string name){try{Hash(value);throw new Exception("Unsafe task identity accepted");}catch(InvalidDataException){check(true,name);}}
    }
}
