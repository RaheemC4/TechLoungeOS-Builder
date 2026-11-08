namespace TechLoungeOS;

public static class LockScreen
{
    public static void Stage(string mount)
    {
        var folder=Path.Combine(mount,@"ProgramData\TechLoungeOS\Appearance");
        Policy.RequireUnlinkedPath(folder);
        Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"lock-screen.png");
        using var resource=typeof(LockScreen).Assembly.GetManifestResourceStream("TechLoungeOS.LockScreen.png")
            ?? throw new IOException("The approved lock-screen background was not packaged.");
        var helper=Path.Combine(AppContext.BaseDirectory,"LockScreenBridge.exe");
        SourcePreservation.RequireImageFile(helper,512,1<<20);
        File.Copy(helper,Path.Combine(folder,"LockScreenBridge.exe"),false);
        using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        resource.CopyTo(output);
    }

    public const string Functions="""
        function Set-TechLoungeLockScreen([string]$Image) {
          foreach($root in @('HKLM:','HKCU:')) {
            $key=Get-ItemProperty "$root\Software\Policies\Microsoft\Windows\Personalization" -ErrorAction SilentlyContinue
            if($key.LockScreenImage -or $key.NoChangingLockScreen -eq 1 -or $key.NoLockScreen -eq 1){throw 'The lock screen is managed by policy. Existing policy was preserved.'}
          }
          if(!(Test-Path -LiteralPath $Image -PathType Leaf)){throw 'The TechLoungeOS lock-screen image is missing.'}
          Write-TechLoungeLockScreenImage $Image
        }
        function Write-TechLoungeLockScreenImage([string]$Image) {
          $helper=Join-Path $env:ProgramData 'TechLoungeOS\Appearance\LockScreenBridge.exe'
          $process=Start-Process -FilePath $helper -ArgumentList @('--apply',('"'+$Image+'"')) -WindowStyle Hidden -Wait -PassThru
          if($process.ExitCode -ne 0){throw 'Windows could not apply and verify the lock screen. Review LocalAppData\TechLoungeOS\lock-screen-error.txt; setup remains retryable.'}
        }
        """;

    public static string FirstLogin => "\n"+Functions+"\n"+"""
        Invoke-Step 'lock-screen' {
          Set-TechLoungeLockScreen (Join-Path $env:ProgramData 'TechLoungeOS\Appearance\lock-screen.png')
          Set-Content -LiteralPath (Join-Path $state 'lock-screen-status.txt') 'The TechLoungeOS background was applied and its image read back. Windows retains its own clock and sign-in controls.'
        }
        """+"\n";
}
