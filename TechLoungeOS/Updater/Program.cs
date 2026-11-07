using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TechLoungeOS.Updates;

static void WaitForParent(string id,string startTicks)
{
    if(!int.TryParse(id,out var pid)||!long.TryParse(startTicks,out var ticks))throw new IOException("Invalid parent identity.");
    try
    {
        using var parent=Process.GetProcessById(pid);
        if(parent.StartTime.ToUniversalTime().Ticks!=ticks)throw new IOException("Parent process identity changed.");
        if(!parent.WaitForExit(300000))throw new IOException("The app is still busy. No files were changed.");
    }
    catch(ArgumentException){ /* The parent has already exited. Never kill a process. */ }
}
if(args.Length is 2 or 4 && args[0]=="--recover")
{
    var recoveryRoot=args[1];
    try
    {
        if(args.Length==4)WaitForParent(args[2],args[3]);
        using var recoveryLease=UpdateCoordinator.Enter(recoveryRoot);
        UpdateCoordinator.EnsureAppClosed(recoveryRoot);UpdateCommit.Recover(recoveryRoot);return 0;
    }
    catch(Exception){return 1;}
}
if(args is not ["--apply",var stage,var install,var parentText,var ticksText,var mode])return 2;
var log=Path.Combine(stage,"apply.log");
try
{
    UpdatePackage.Unlinked(stage);UpdateCommit.ValidateInstall(install);
    if(!int.TryParse(parentText,out var parentId)||!long.TryParse(ticksText,out var ticks)||mode is not ("restart" or "exit"))throw new IOException("Invalid updater arguments.");
    var current=FileVersionInfo.GetVersionInfo(Path.Combine(install,UpdateTrust.MainExe)).FileVersion;
    var manifest=UpdatePackage.VerifyEnvelope(File.ReadAllBytes(Path.Combine(stage,"builder-update.json")),UpdateTrust.PublicKeySpki,Version.Parse(current!),Environment.OSVersion.Version.Build,DateTimeOffset.UtcNow);
    WaitForParent(parentText,ticksText);
    using var exclusiveLease=UpdateCoordinator.Enter(install);
    UpdateCoordinator.EnsureAppClosed(install);
        var extracted=Path.Combine(stage,"apply-verified");UpdatePackage.Extract(Path.Combine(stage,"package.zip"),extracted,manifest);
        var backup=UpdateCommit.Apply(install,extracted,manifest);
        File.AppendAllText(log,$"{DateTimeOffset.UtcNow:O} Updated to {manifest.Version}. Previous files retained in {backup}.{Environment.NewLine}");
    exclusiveLease.Dispose();
    if(mode=="restart")Process.Start(new ProcessStartInfo(Path.Combine(install,UpdateTrust.MainExe)){UseShellExecute=true,WorkingDirectory=install});
    return 0;
}
catch(Exception ex)
{
    try{File.AppendAllText(log,$"{DateTimeOffset.UtcNow:O} Update failed: {ex}{Environment.NewLine}");}catch(IOException){}
    return 1;
}
