using System.Security.AccessControl;
using System.Text.Json;
using System.Text.Json.Nodes;
using TechLoungeOS;

internal static class WindowsUpdateMetadataChecks
{
    internal static void Run(string folder,Action<bool,string> check)
    {
        JsonElement Parse(string text)=>JsonDocument.Parse(text).RootElement.Clone();
        var first=Parse("""{"key:Software":{"key:Microsoft":{"key:UEV":{"key:Agent":{"value:UserConsoleVersion":{"kind":1,"data":"b2xk"},"value:Custom":{"kind":4,"data":"BAAAAA=="}}}}}}""");
        var updated=JsonNode.Parse(first.GetRawText())!;
        updated["key:Software"]!["key:Microsoft"]!["key:UEV"]!["key:Agent"]!["value:UserConsoleVersion"]!["data"]="bmV3";
        var path=new[]{"Software","Microsoft","UEV","Agent"};
        var old=WindowsUpdateMetadata.Fingerprint(first,path,"UserConsoleVersion",0);
        var next=WindowsUpdateMetadata.Fingerprint(JsonSerializer.SerializeToElement(updated),path,"UserConsoleVersion",0);
        check(old.Value!=next.Value && old.Remainder==next.Remainder,"profile version metadata is isolated while every other default-user setting remains in the fingerprint");
        updated["key:Software"]!["key:Microsoft"]!["key:UEV"]!["key:Agent"]!["value:Custom"]!["data"]="AAAAAA==";
        check(old.Remainder!=WindowsUpdateMetadata.Fingerprint(JsonSerializer.SerializeToElement(updated),path,"UserConsoleVersion",0).Remainder,"an internal profile version change cannot hide an overwritten custom setting");
        var policy=Parse("""{"key:Microsoft":{"key:Windows NT":{"key:Existing":{"value:Custom":{"kind":4,"data":"BAAAAA=="}}}}}""");
        var changed=JsonNode.Parse(policy.GetRawText())!;
        changed["key:Microsoft"]!["key:Windows NT"]!["key:Printers"]=JsonNode.Parse("""{"key:DriverRanking":{"value:UseWindowsReadyPrintDriverRanking":{"kind":4,"data":"AQAAAA=="}}}""");
        var printer=new[]{"Microsoft","Windows NT","Printers","DriverRanking"};
        var source=WindowsUpdateMetadata.Fingerprint(policy,printer,"UseWindowsReadyPrintDriverRanking",2);
        var target=WindowsUpdateMetadata.Fingerprint(JsonSerializer.SerializeToElement(changed),printer,"UseWindowsReadyPrintDriverRanking",2);
        check(source.Value=="missing-value" && target.Value!="missing-value" && source.Remainder==target.Remainder,"only the known newly added printer-ranking value and its empty containers are isolated");
        changed["key:Microsoft"]!["key:Windows NT"]!["key:Printers"]!["value:Unexpected"]=JsonNode.Parse("""{"kind":4,"data":"AQAAAA=="}""");
        check(source.Remainder!=WindowsUpdateMetadata.Fingerprint(JsonSerializer.SerializeToElement(changed),printer,"UseWindowsReadyPrintDriverRanking",2).Remainder,"additional printer-policy changes remain visible");
        var missing=WindowsUpdateMetadata.Fingerprint(null,path,"UserConsoleVersion",0);
        check(missing.Value=="missing-value" && missing.Remainder=="missing-key","a missing profile cannot impersonate a known source metadata version");

        var root=Path.Combine(folder,"task-files-metadata");var tasks=Path.Combine(root,@"Windows\System32\Tasks");Directory.CreateDirectory(tasks);
        var file=Path.Combine(tasks,"InertFixture");File.WriteAllText(file,"<Task><Actions>fixture only, never registered</Actions></Task>");
        var hash=WindowsUpdateMetadata.TaskFiles(root);
        File.WriteAllText(file,"<Task><Actions>changed fixture, never registered</Actions></Task>");
        check(hash!=WindowsUpdateMetadata.TaskFiles(root),"task-file fingerprints detect an action change independently of cached registry records");
        File.WriteAllText(file,"<Task><Actions>fixture only, never registered</Actions></Task>");
        var original=new FileInfo(file).GetAccessControl();
        try
        {
            var modified=new FileInfo(file).GetAccessControl();modified.SetAccessRuleProtection(true,true);new FileInfo(file).SetAccessControl(modified);
            check(hash!=WindowsUpdateMetadata.TaskFiles(root),"task-file fingerprints also retain NTFS owner, group and access permissions");
        }
        finally{new FileInfo(file).SetAccessControl(original);}
        File.WriteAllBytes(Path.Combine(tasks,"OversizedFixture"),new byte[(2<<20)+1]);
        try{WindowsUpdateMetadata.TaskFiles(root);throw new Exception("Oversized task evidence accepted");}catch(IOException){check(true,"task-file inspection enforces its per-file budget");}
    }
}
