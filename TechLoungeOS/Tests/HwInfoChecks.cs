using System.Text.Json;
using TechLoungeOS;

static class HwInfoChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var folder=Path.Combine(root,"hwinfo");var commands=new DownloadFixture();
        await HwInfoPack.Prepare(commands,folder,CancellationToken.None);
        var item=JsonSerializer.Deserialize<HwInfoPack.Installer>(File.ReadAllText(Path.Combine(folder,"installer.json")))!;
        check(item.Version=="8.54"&&item.Sha256==RuntimePack.Hash(Path.Combine(folder,"HWiNFO.exe"))
            &&commands.Arguments.Contains("download")&&commands.Arguments.Contains("--architecture")&&commands.Arguments.Contains("x64"),
            "HWiNFO preparation resolves the current x64 WinGet payload and records its version and exact hash without running it");
        check(commands.SignatureCheck.Contains("Get-AuthenticodeSignature")&&commands.SignatureCheck.Contains("REALiX, s.r.o."),"HWiNFO preparation also requires its valid REALiX publisher signature");
        var pf=Path.Combine(folder,"pf");var exe=Path.Combine(pf,@"HWiNFO64\HWiNFO64.exe");
        var script=HwInfoPack.Script.Replace("if(!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'HWiNFO setup requires administrator permission.'}","# fixture-only privilege substitution")
            .Replace("$env:ProgramFiles",Policy.Ps(pf));
        var path=Path.Combine(folder,"install.ps1");File.WriteAllText(path,script);
        string Header(int exit)=>$$$"""
            $global:hwInfoFixturePolls=0;$global:hwInfoFixtureStopped=@()
            function Get-AuthenticodeSignature {
              $certificate=[pscustomobject]@{};$certificate|Add-Member ScriptMethod GetNameInfo {param($Type,$Issuer) 'REALiX, s.r.o.'}
              [pscustomobject]@{Status='Valid';SignerCertificate=$certificate}
            }
            function Start-Sleep {param($Milliseconds) if($Milliseconds -ne 250){throw 'Unexpected polling delay'}}
            function Get-Process {param($Name,$Id)
              if($Id){return [pscustomobject]@{SessionId=0}}
              $global:hwInfoFixturePolls++
              [pscustomobject]@{Id=100;SessionId=0;Path={{{Policy.Ps(exe)}}}}
              if($global:hwInfoFixturePolls -ge 8 -and 101 -notin $global:hwInfoFixtureStopped){[pscustomobject]@{Id=101;SessionId=0;Path={{{Policy.Ps(exe)}}}}}
              [pscustomobject]@{Id=102;SessionId=1;Path={{{Policy.Ps(exe)}}}}
              [pscustomobject]@{Id=103;SessionId=0;Path='C:\Unrelated\HWiNFO64.exe'}
            }
            function Stop-Process {param($Id,$ErrorAction) $global:hwInfoFixtureStopped+=$Id}
            function Start-Process {param($FilePath,$ArgumentList,[switch]$PassThru)
              Set-Content {{{Policy.Ps(Path.Combine(folder,"calls.txt"))}}} $ArgumentList
              if({{{exit}}} -eq 0){New-Item {{{Policy.Ps(Path.GetDirectoryName(exe)!)}}} -ItemType Directory -Force|Out-Null;Set-Content {{{Policy.Ps(exe)}}} 'inert installed file'}
              $process=[pscustomobject]@{Handle=1;ExitCode={{{exit}}}};$process|Add-Member ScriptMethod WaitForExit {};return $process
            }
            & {{{Policy.Ps(path)}}}
            ConvertTo-Json -InputObject @($global:hwInfoFixtureStopped) -Compress|Set-Content {{{Policy.Ps(Path.Combine(folder,"stopped.json"))}}}
            """;
        var run=new ProcessRunner(_=>{});await run.PowerShell(Header(0));
        check(File.Exists(Path.Combine(folder,"complete"))&&File.ReadAllText(Path.Combine(folder,"calls.txt")).Trim()=="/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
            &&File.ReadAllText(Path.Combine(folder,"stopped.json")).Trim()=="[101]",
            "pre-desktop HWiNFO setup stops only its delayed new child in the setup session, retains prior/other-session/unrelated processes and requests no restart");
        File.Delete(Path.Combine(folder,"calls.txt"));await run.PowerShell(Header(0));
        check(!File.Exists(Path.Combine(folder,"calls.txt"))&&File.ReadAllText(exe).Trim()=="inert installed file","existing source HWiNFO is signature-checked and retained without reinstall or process cleanup");
        File.Delete(exe);bool failed=false;try{await run.PowerShell(Header(1603));}catch(IOException){failed=true;}
        check(failed&&!File.Exists(Path.Combine(folder,"complete"))&&File.ReadAllText(Path.Combine(folder,"status.txt")).Contains("1603"),"failed HWiNFO setup clears stale success and records the actual installer failure");
        File.Delete(Path.Combine(folder,"calls.txt"));File.AppendAllText(Path.Combine(folder,"HWiNFO.exe"),"tampered");
        failed=false;try{await run.PowerShell(Header(0));}catch(IOException){failed=true;}
        check(failed&&!File.Exists(Path.Combine(folder,"calls.txt")),"HWiNFO checksum mismatch stops before executing the installer");
        var mount=Path.Combine(root,"hwinfo-stage");var hook=SetupHooks.ImageHook(mount);Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
        var original=System.Text.Encoding.ASCII.GetBytes("@echo inherited source hook\r\n");File.WriteAllBytes(hook,original);
        var deferred=RuntimePack.AddSetupCommand(mount,"TechLoungeOS-HWiNFO",folder);
        check(File.ReadAllBytes(hook).SequenceEqual(original)&&deferred.Id=="HWiNFO"&&deferred.Files.Any(f=>f.Destination=="install.ps1"),"selected HWiNFO is pinned for desktop setup while retaining the complete original hook unchanged");
        var login=AppRecipes.Script("realix.hwinfo");
        check(AppRecipes.Custom("REALiX.HWiNFO")&&login==HwInfoPack.FirstLogin&&!login.Contains("Start-Process")&&!login.Contains("Install-WinGetApp"),"all HWiNFO ID casing uses the machine-setup receipt and desktop shortcut without launching or reinstalling it at login");
    }
    private sealed class DownloadFixture:ICommandRunner
    {
        public string[] Arguments=[];public string SignatureCheck="";
        public Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)
        {
            Arguments=args.ToArray();if(exe!="winget.exe"||Arguments[0]!="download")throw new Exception("Unexpected host execution");
            var folder=Arguments[Array.IndexOf(Arguments,"--download-directory")+1];
            File.WriteAllText(Path.Combine(folder,"HWiNFO_8.54_Machine_X64_inno.exe"),"inert installer, never executed");return Task.FromResult("");
        }
        public Task<string> PowerShell(string script,CancellationToken ct=default){SignatureCheck=script;return Task.FromResult("");}
        public Task<string> Dism(params string[] args)=>throw new Exception("Unexpected servicing");
    }
}
