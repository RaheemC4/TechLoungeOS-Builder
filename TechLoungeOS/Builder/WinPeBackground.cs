using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TechLoungeOS;

// Replace the recognised legacy toolbar while preserving original tools and initialization.
public static class WinPeBackground
{
    public const string LaunchLine="%SYSTEMROOT%\\System32\\TechLoungeOS\\PeBackground.exe, --pe";
    public static byte[] AddLaunch(byte[] original,string? keyboard=null)
    {
        if(original.Length>65536)throw new IOException("WinPE shell configuration is too large to safely extend.");
        // Preserve encoding and unrelated entries. The user explicitly requested
        // replacement of Ghost's old LaunchBar, while retaining its tool files.
        if(original.Contains((byte)0))throw new IOException("This WinPE shell encoding is unsupported. Keep its original background.");
        var text=Encoding.Latin1.GetString(original);
        if(text.Contains("TechLoungeOS",StringComparison.OrdinalIgnoreCase))throw new IOException("WinPE already has a Builder background integration. Keep the existing background.");
        var lines=System.Text.RegularExpressions.Regex.Matches(text,@"(?im)^\[LaunchApps\][ \t]*\r?$");
        if(lines.Count!=1||System.Text.RegularExpressions.Regex.IsMatch(text,@"(?im)^\[LaunchApp\]"))throw new IOException("This WinPE shell has no unambiguous LaunchApps section. Its launcher was left unchanged.");
        var legacy=System.Text.RegularExpressions.Regex.Matches(text,@"(?im)^[ \t]*(?:%SYSTEMDRIVE%\\Windows|%SYSTEMROOT%)\\System32\\LaunchBar_x64\.exe[ \t]*(?=\r?$)");
        if(legacy.Count!=1||legacy[0].Index<lines[0].Index)
            throw new IOException("The setup wizard currently requires a recognised single legacy toolbar launcher. Keep the original setup environment for this image.");
        // Remain the foreground shell process at the exact former launch point.
        // Detaching here would let winpeshl exit and reboot after initialization.
        var entry=legacy[0];
        if(keyboard!=null&&!System.Text.RegularExpressions.Regex.IsMatch(keyboard,@"\A[0-9A-F]{4}:[0-9A-F]{8}\z"))throw new IOException("Invalid WinPE keyboard selection.");
        var launch=keyboard==null?LaunchLine:"%SYSTEMROOT%\\System32\\wpeutil.exe, SetKeyboardLayout "+keyboard+"\r\n"+LaunchLine;
        return [..original[..entry.Index],..Encoding.ASCII.GetBytes(launch),..original[(entry.Index+entry.Length)..]];
    }
    public static IReadOnlyDictionary<string,string> StageMounted(string mount,byte[] executable,IReadOnlyDictionary<string,byte[]>? additional=null,SetupRegional? regional=null)
    {
        var shell=Path.Combine(mount,@"Windows\System32\winpeshl.ini");
        var target=Path.Combine(mount,@"Windows\System32\TechLoungeOS");
        Policy.RequireUnlinkedPath(shell);Policy.RequireUnlinkedPath(target);
        if(Directory.Exists(target)||File.Exists(target)||new DirectoryInfo(target).LinkTarget!=null)throw new IOException("The WinPE background folder already exists. Existing tools were left untouched.");
        foreach(var name in new[]{"cmd.exe","kernel32.dll","user32.dll","gdi32.dll","msvcrt.dll","advapi32.dll","shell32.dll","ole32.dll","msimg32.dll"})
            if(!File.Exists(Path.Combine(mount,@"Windows\System32",name)))throw new IOException("This WinPE lacks a background prerequisite: "+name+". Keep its original background.");
        var keyboard=KeyboardForImage(mount,regional);
        byte[]? original=null;
        if(Directory.Exists(shell))throw new IOException("Windows PE shell configuration is a directory. Existing files were left untouched.");
        if(File.Exists(shell)){SourcePreservation.RequireImageFile(shell,1,65536);original=File.ReadAllBytes(shell);}
        var updated=original==null?WinPeStartup.ReadStandardShell(mount,keyboard):AddLaunch(original,keyboard);
        if(executable.Length<1024||executable[0]!='M'||executable[1]!='Z')throw new IOException("The native background payload is missing or invalid.");
        if(additional!=null&&additional.Keys.Any(name=>!new[]{"account-auto.xml","account-manual.xml","media.id",@"Fonts\manrope-regular.ttf",@"Fonts\manrope-semibold.ttf",@"Fonts\OFL.txt"}.Contains(name,StringComparer.Ordinal)))
            throw new IOException("Unexpected setup wizard payload name.");
        var payload=original==null?PeTools.Read(mount):new Dictionary<string,byte[]>(StringComparer.Ordinal);
        payload.Add("PeBackground.exe",executable);
        if(original!=null)payload.Add("winpeshl.original.ini",original);
        else payload.Add("startup-original.json",Encoding.UTF8.GetBytes("{\"winpeshlIniExisted\":false,\"startup\":\"standard-wpeinit\"}"));
        if(additional!=null)foreach(var entry in additional)payload.Add(entry.Key,entry.Value);
        var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in payload)
        {
            var path=Path.Combine(target,entry.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,entry.Value);
            expected.Add(Path.GetRelativePath(mount,path),Convert.ToHexString(SHA256.HashData(entry.Value)));
        }
        // Failure aborts the private WIM transaction; no source media is changed.
        if(original!=null)File.SetAttributes(shell,File.GetAttributes(shell)&~FileAttributes.ReadOnly);
        File.WriteAllBytes(shell,updated);
        expected.Add(Path.GetRelativePath(mount,shell),Convert.ToHexString(SHA256.HashData(updated)));
        return expected;
    }
    public static async Task Prepare(ICommandRunner run,string media,string work,Action<string> log,CancellationToken ct,SetupRegional? regional=null)
    {
        var source=Path.Combine(media,@"sources\boot.wim");
        var candidate=Path.Combine(work,"animated-boot.wim");var mount=Path.Combine(work,"pe-mount");
        var bootIndex=WinPeImage.ReadBootIndex(source);
        log("Using the source Windows PE boot image at index "+bootIndex+".");
        File.Copy(source,candidate,false);File.SetAttributes(candidate,File.GetAttributes(candidate)&~FileAttributes.ReadOnly);
        Directory.CreateDirectory(mount);var mounted=false;
        try
        {
            var answers=new[]{Path.Combine(media,"Autounattend.xml"),Path.Combine(media,@"sources\unattend.xml")}.Where(File.Exists).ToArray();
            if(answers.Length>1)throw new IOException("Multiple source answer files need review before adding the account wizard.");
            string? answer=null;
            if(answers.Length==1){SourcePreservation.RequireImageFile(answers[0],0,2<<20);answer=File.ReadAllText(answers[0]);}
            var marker=Path.Combine(media,"TechLoungeOS.media");
            if(File.Exists(marker)||Directory.Exists(marker))throw new IOException("Installation media already contains a Builder session marker.");
            var id=Guid.NewGuid().ToString("D");
            var extra=new Dictionary<string,byte[]>(StringComparer.Ordinal)
            {
                ["account-auto.xml"]=Encoding.UTF8.GetBytes(SetupAnswerFile.Template(true,answer,regional)),
                ["account-manual.xml"]=Encoding.UTF8.GetBytes(SetupAnswerFile.Template(false,answer,regional)),
                ["media.id"]=Encoding.ASCII.GetBytes(id)
            };
            foreach(var font in new[]{"manrope-regular.ttf","manrope-semibold.ttf","OFL.txt"})
            {
                using var fontStream=typeof(WinPeBackground).Assembly.GetManifestResourceStream("TechLoungeOS.PeFonts."+font)??throw new IOException("The WinPE wizard font was not packaged: "+font);
                using var fontBytes=new MemoryStream();fontStream.CopyTo(fontBytes);extra.Add(@"Fonts\"+font,fontBytes.ToArray());
            }
            // Use the WIM's marked boot image; never guess a different setup entry.
            ct.ThrowIfCancellationRequested();mounted=true;
            await run.Dism("/Mount-Image","/ImageFile:"+candidate,"/Index:"+bootIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),"/MountDir:"+mount,"/CheckIntegrity");
            log("Checking inherited WinPE tools and files before adding the background. This file verification can take several minutes.");
            var before=Snapshot(mount,ct);
            using var resource=typeof(WinPeBackground).Assembly.GetManifestResourceStream("TechLoungeOS.PeBackground.exe")??throw new IOException("Native WinPE background was not packaged.");
            using var bytes=new MemoryStream();resource.CopyTo(bytes);
            var expected=StageMounted(mount,bytes.ToArray(),extra,regional);
            log("Verifying that the original WinPE tools stayed unchanged and only the background wrapper was added.");
            var after=Snapshot(mount,ct);var shell=@"Windows\System32\winpeshl.ini";
            foreach(var entry in before)
                if(!entry.Key.Equals(shell,StringComparison.OrdinalIgnoreCase)&&(!after.TryGetValue(entry.Key,out var hash)||hash!=entry.Value))
                    throw new IOException("An inherited WinPE file changed: "+entry.Key);
            var added=after.Keys.Except(before.Keys,StringComparer.OrdinalIgnoreCase).ToArray();
            if(expected.Any(entry=>!after.TryGetValue(entry.Key,out var hash)||hash!=entry.Value)||
                !added.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(expected.Keys.Except(before.Keys,StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))throw new IOException("Windows PE payload verification failed or unexpected files were added.");
            File.WriteAllText(Path.Combine(work,"pe-preservation.json"),JsonSerializer.Serialize(new{before,after,added,changed=before.ContainsKey(shell)?new[]{shell}:Array.Empty<string>()},new JsonSerializerOptions{WriteIndented=true}));
            log("WinPE file preservation verified. Saving the background changes.");
            ct.ThrowIfCancellationRequested();await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Commit","/CheckIntegrity");mounted=false;
            if(WinPeImage.ReadBootIndex(candidate)!=bootIndex)throw new IOException("The saved Windows PE image changed its boot selection. The source boot image was kept.");
            File.Copy(source,Path.Combine(work,"source-boot.wim"),false);
            File.SetAttributes(source,File.GetAttributes(source)&~FileAttributes.ReadOnly);File.Move(candidate,source,true);
            File.WriteAllText(marker,id,Encoding.ASCII);
            log("Added the native WinPE account wizard. Existing shell entries and tools were retained; boot and account sign-in testing are still required.");
        }
        finally
        {
            if(mounted)
                try{await run.Dism("/Unmount-Image","/MountDir:"+mount,"/Discard");}
                catch(Exception ex){log("WinPE cleanup requires recovery at "+mount+": "+ex.Message);}
        }
    }
    internal static string? KeyboardForImage(string mount,SetupRegional? regional)
    {
        if(regional?.OverrideKeyboard!=true)return null;
        var input=regional.InputLocale;
        if(!System.Text.RegularExpressions.Regex.IsMatch(input,@"\A[0-9A-F]{4}:[0-9A-F]{8}\z"))throw new IOException("The WinPE wizard needs one explicitly selected keyboard.");
        var system=Path.Combine(mount,@"Windows\System32\config\SYSTEM");
        var current=SourcePreservation.ReadDwordValue(system,"Select","Current")??SourcePreservation.ReadDwordValue(system,"Select","Default");
        if(current is null or 0 or >999)throw new IOException("WinPE keyboard configuration could not be read.");
        var file=SourcePreservation.ReadStringValue(system,$"ControlSet{current:000}\\Control\\Keyboard Layouts\\{input[5..]}","Layout File");
        if(string.IsNullOrWhiteSpace(file)||Path.GetFileName(file)!=file||!file.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))throw new IOException("The selected keyboard is not available in this WinPE. Choose another keyboard before building.");
        SourcePreservation.RequireImageFile(Path.Combine(mount,@"Windows\System32",file),1024,64L<<20);
        SourcePreservation.RequireImageFile(Path.Combine(mount,@"Windows\System32\wpeutil.exe"),1024,64L<<20);
        return input;
    }
    internal static Dictionary<string,string> Snapshot(string root,CancellationToken ct)
    {
        try{return SnapshotCore(root,ct,false);}
        catch(UnauthorizedAccessException){return ImageReadAccess.Run(()=>SnapshotCore(root,ct,true));}
    }
    private static Dictionary<string,string> SnapshotCore(string root,CancellationToken ct,bool backupRead)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string folder)
        {
            Policy.RequireUnlinkedPath(folder);
            foreach(var path in Directory.EnumerateFiles(folder))
            {
                ct.ThrowIfCancellationRequested();Policy.RequireUnlinkedPath(path);SourcePreservation.RequireImageFile(path,0,long.MaxValue);
                using var file=backupRead?ImageReadAccess.OpenRead(path):File.OpenRead(path);result.Add(Path.GetRelativePath(root,path),Convert.ToHexString(SHA256.HashData(file)));
            }
            foreach(var directory in Directory.EnumerateDirectories(folder))Visit(directory);
        }
        Visit(root);return result;
    }

}
