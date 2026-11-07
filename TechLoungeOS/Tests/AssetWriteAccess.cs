using System.Security.AccessControl;
using TechLoungeOS;

// Retained native-test entry points. Historical pinned permission evidence stays test-only.
internal static class AssetWriteAccess
{
    internal static void Run(Action action)=>KnownWallpaperWriteAccess.Run(action);
    internal static FileStream CreateNew(string path)=>KnownWallpaperWriteAccess.CreateNew(path);
    internal static void ReplaceEntry(string target,string active,string temporary,string evidence,Action replace)
    {
        var pinned=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(@"S:\feature-repair1609\retry1615\asset-repair\permissions.json"))!;
        foreach(var path in new[]{target,active,Path.GetDirectoryName(target)!})
            KnownWallpaperWriteAccess.RequireSameSecurity(path,Convert.FromBase64String(pinned[path]));
        KnownWallpaperWriteAccess.ReplaceEntry(target,active,temporary,evidence,replace);
    }
    internal static async Task Diagnose()
    {
        await new TechLoungeOS.ProcessRunner(Console.WriteLine).PowerShell("if((Get-CimInstance Win32_ComputerSystemProduct).UUID -ine '9ce10821-65ff-4a1f-9663-f07c01145563'){throw 'Owned diagnostic VM required'}");
        const string root=@"S:\feature-repair1609";
        var original=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,@"retry1615\asset-repair\permissions.json")))!;
        var rows=new List<object>();
        Run(()=>{foreach(var pair in original){var before=new RawSecurityDescriptor(Convert.FromBase64String(pair.Value),0);var after=new RawSecurityDescriptor(KnownWallpaperWriteAccess.ReadSecurity(pair.Key),0);rows.Add(new{path=pair.Key,expected=before.GetSddlForm(AccessControlSections.All),actual=after.GetSddlForm(AccessControlSections.All),expectedFlags=before.ControlFlags.ToString(),actualFlags=after.ControlFlags.ToString()});}});
        var text=System.Text.Json.JsonSerializer.Serialize(rows,new System.Text.Json.JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(root,"security-diagnosis1617.json"),text);Console.WriteLine(text);
    }
    internal static void SecurityFixtures()
    {
        byte[] Bytes(string s){var sd=new RawSecurityDescriptor(s);var bytes=new byte[sd.BinaryLength];sd.GetBinaryForm(bytes,0);return bytes;}
        void Equal(string a,string b,bool expected){if((KnownWallpaperWriteAccess.SecurityKey(Bytes(a))==KnownWallpaperWriteAccess.SecurityKey(Bytes(b)))!=expected)throw new IOException("Security equivalence fixture failed: "+a+" / "+b);}
        const string original="O:SYG:SYD:PAI(A;OICI;FA;;;SY)(A;;FR;;;BA)";
        const string canonical="O:SYG:SYD:P(A;OICI;FA;;;SY)(A;;FR;;;BA)S:NO_ACCESS_CONTROL";
        Equal(original,canonical,true);
        Equal(original,canonical.Replace("FR;;;BA","FA;;;BA"),false);
        Equal(original,canonical.Replace("O:SY","O:BA"),false);
        Equal(original,canonical.Replace("D:P","D:"),false);
        Equal(original,canonical.Replace("OICI;FA","OICIIO;FA"),false);
        Equal(original,"O:SYG:SYD:NO_ACCESS_CONTROL",false);
        Equal("O:SYG:SYD:P(A;;FA;;;SY)S:(AU;SAFA;FA;;;WD)","O:SYG:SYD:P(A;;FA;;;SY)",false);
        Equal("O:SYG:SYD:P(A;;FA;;;SY)S:(ML;;NW;;;LW)","O:SYG:SYD:P(A;;FA;;;SY)",false);
        Equal("O:SYG:SYD:AI(A;;FR;;;BA)","O:SYG:SYD:(A;;FR;;;BA)",false);
        Equal("O:SYG:SYD:P(A;;FA;;;SY)S:P(AU;SAFA;FA;;;WD)","O:SYG:SYD:P(A;;FA;;;SY)S:(AU;SAFA;FA;;;WD)",false);
        Console.WriteLine("PASS: protected inheritance marker/NULL audit canonicalisation accepted; changed access, owner, inheritance, audit, integrity label and NULL DACL rejected.");
    }
}
