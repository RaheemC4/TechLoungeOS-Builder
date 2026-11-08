using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TechLoungeOS;

// New-profile defaults only. Discover templates from the selected image, never the
// build host. The original NTUSER backup and final preservation hash are owned by
// RecommendedDefaultUser's existing atomic transaction.
internal static class FolderViewDefaults
{
    internal const string MachinePath=@"Microsoft\Windows\CurrentVersion\Explorer\FolderTypes";
    internal const string UserPath=@"Software\Microsoft\Windows\CurrentVersion\Explorer\FolderTypes";
    internal const string Sort="prop:+System.ItemTypeText;+System.ItemNameDisplay";

    internal static void Seed(nint user,string mount)
    {
        var software=Path.Combine(mount,@"Windows\System32\config\SOFTWARE");
        SourcePreservation.RequireImageFile(software,4096,512L<<20);
        Policy.RequireUnlinkedPath(software);
        Ok(OROpenHive(software,out var machine));
        try{
            Ok(OROpenKey(machine,MachinePath,out var types));
            try{
                var count=0;var budget=16384;
                foreach(var name in Children(types)){
                    if(!Guid.TryParseExact(name,"B",out _))continue;
                    Ok(OROpenKey(types,name,out var template));
                    try{
                        var result=OROpenKey(template,"TopViews",out var top);
                        if(result==2)continue;
                        Ok(result);Ok(ORCloseKey(top));
                        var target=CreatePath(user,UserPath+"\\"+name);
                        try{
                            CopyMissing(template,target,0,ref budget);
                            Ok(OROpenKey(target,"TopViews",out var views));
                            try{foreach(var view in Children(views)){
                                if(!Guid.TryParseExact(view,"B",out _))continue;
                                Ok(OROpenKey(views,view,out var current));
                                try{WriteString(current,"GroupBy","");WriteString(current,"SortByList",Sort);count++;}
                                finally{Ok(ORCloseKey(current));}
                            }}finally{Ok(ORCloseKey(views));}
                        }finally{Ok(ORCloseKey(target));}
                    }finally{Ok(ORCloseKey(template));}
                }
                if(count==0)throw new IOException("This image has no recognised Explorer folder-view templates. Folder defaults could not be prepared.");
                // Remove only inherited folder/dialog views which override templates.
                // Desktop icon positions and other shell settings are retained.
                foreach(var path in new[]{@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags",@"Software\Microsoft\Windows\Shell\Bags"}){
                    var result=OROpenKey(user,path,out var bags);if(result==2)continue;Ok(result);
                    try{foreach(var bag in Children(bags)){
                        if(bag!="AllFolders"&&!uint.TryParse(bag,out _))continue;
                        foreach(var shell in new[]{"Shell","ComDlg","ComDlgLegacy"})RemoveTree(bags,bag+"\\"+shell,0,ref budget);
                    }}finally{Ok(ORCloseKey(bags));}
                }
            }finally{Ok(ORCloseKey(types));}
        }finally{Ok(ORCloseHive(machine));}
    }

    private static nint CreatePath(nint root,string path)
    {
        var current=root;
        try{foreach(var part in path.Split('\\')){
            Ok(ORCreateKey(current,part,0,0,0,out var next,0));
            if(current!=root)Ok(ORCloseKey(current));current=next;
        }return current;}catch{if(current!=root)ORCloseKey(current);throw;}
    }
    private static string[] Children(nint key)
    {
        var result=new List<string>();
        for(uint i=0;i<4096;i++){
            var name=new StringBuilder(16384);uint length=16384;
            var code=OREnumKey(key,i,name,ref length,0,0,0);
            if(code==259)return result.ToArray();Ok(code);result.Add(name.ToString());
        }
        throw new IOException("Too many Explorer template keys.");
    }
    private static void CopyMissing(nint source,nint target,int depth,ref int budget)
    {
        if(depth>16||--budget<0)throw new IOException("Explorer template size exceeds its servicing limit.");
        for(uint i=0;;i++){
            if(i>4096)throw new IOException("Too many Explorer template values.");
            var name=new StringBuilder(16384);uint length=16384,size=65536;var bytes=new byte[size];
            var code=OREnumValue(source,i,name,ref length,out var kind,bytes,ref size);
            if(code==259)break;Ok(code);
            uint existingSize=0;var exists=ORGetValue(target,null,name.ToString(),out _,null,ref existingSize);
            if(exists==2)Ok(ORSetValue(target,name.ToString(),kind,bytes,size));
            else if(exists!=0&&exists!=234)Ok(exists);
        }
        foreach(var name in Children(source)){
            Ok(OROpenKey(source,name,out var child));
            try{Ok(ORCreateKey(target,name,0,0,0,out var destination,0));
                try{CopyMissing(child,destination,depth+1,ref budget);}finally{Ok(ORCloseKey(destination));}
            }finally{Ok(ORCloseKey(child));}
        }
    }
    private static void WriteString(nint key,string name,string value)
    {
        var bytes=Encoding.Unicode.GetBytes(value+'\0');Ok(ORSetValue(key,name,1,bytes,(uint)bytes.Length));
        uint size=(uint)bytes.Length;var actual=new byte[size];Ok(ORGetValue(key,null,name,out var kind,actual,ref size));
        if(kind!=1||size!=bytes.Length||!actual.SequenceEqual(bytes))throw new IOException("Explorer default-view readback failed.");
    }
    private static void RemoveTree(nint parent,string name,int depth,ref int budget)
    {
        if(depth>32||--budget<0)throw new IOException("Inherited Explorer view cache exceeds its servicing limit.");
        var result=OROpenKey(parent,name,out var key);if(result==2)return;Ok(result);
        try{foreach(var child in Children(key))RemoveTree(key,child,depth+1,ref budget);}finally{Ok(ORCloseKey(key));}
        Ok(ORDeleteKey(parent,name));
    }
    private static void Ok(uint code){if(code!=0)throw new IOException("Cannot prepare Explorer folder-view defaults.",new Win32Exception((int)code));}
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint OROpenHive(string path,out nint hive);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint OROpenKey(nint parent,string name,out nint key);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint ORCreateKey(nint parent,string name,nint keyClass,uint options,nint security,out nint key,nint disposition);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint ORGetValue(nint key,string? subkey,string name,out uint kind,byte[]? data,ref uint size);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint ORSetValue(nint key,string name,uint kind,byte[] data,uint size);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint OREnumKey(nint key,uint index,StringBuilder name,ref uint length,nint keyClass,nint classLength,nint time);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint OREnumValue(nint key,uint index,StringBuilder name,ref uint length,out uint kind,byte[] data,ref uint size);
    [DllImport("offreg.dll",CharSet=CharSet.Unicode,ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint ORDeleteKey(nint parent,string name);
    [DllImport("offreg.dll",ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint ORCloseKey(nint key);
    [DllImport("offreg.dll",ExactSpelling=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint ORCloseHive(nint hive);
}
