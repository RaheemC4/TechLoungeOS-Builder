namespace TechLoungeOS;

public static class WindowsRecovery
{
    public static async Task ValidateSource(ICommandRunner runner,string mount)
    {
        foreach(var relative in new[]{@"Windows\System32\reagentc.exe",@"Windows\System32\Recovery\Winre.wim"})
        {
            var path=Path.Combine(mount,relative);
            Policy.RequireUnlinkedPath(path);
            if(!File.Exists(path))throw new IOException("Windows recovery is required, but this source removed "+relative+". Use a source retaining a compatible recovery image. The original ISO stays unchanged.");
        }
        await runner.Dism("/Get-WimInfo","/WimFile:"+Path.Combine(mount,@"Windows\System32\Recovery\Winre.wim"));
    }

    public const string Script="""
        $ErrorActionPreference='Stop'
        $folder=Join-Path $env:ProgramData 'TechLoungeOS\recovery'
        New-Item -ItemType Directory -Path $folder -Force|Out-Null
        $lock=[IO.File]::Open((Join-Path $folder 'configure.lock'),'OpenOrCreate','ReadWrite','None')
        try {
          $reagent=Join-Path $env:windir 'System32\reagentc.exe'
          $configuration=Join-Path $env:windir 'System32\Recovery\ReAgent.xml'
          function Test-Registration {
            if(!(Test-Path -LiteralPath $configuration)){return $false}
            $settings=New-Object Xml.XmlReaderSettings
            $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver=$null
            $settings.MaxCharactersInDocument=1048576
            $reader=[Xml.XmlReader]::Create($configuration,$settings)
            try{$document=New-Object Xml.XmlDocument;$document.XmlResolver=$null;$document.Load($reader)}finally{$reader.Dispose()}
            $id=[guid]::Empty
            return ($document.WindowsRE.InstallState.state -eq '1' -and [guid]::TryParse([string]$document.WindowsRE.WinreBCD.id,[ref]$id) -and $id -ne [guid]::Empty -and ![string]::IsNullOrWhiteSpace([string]$document.WindowsRE.WinreLocation.path))
          }
          # Keep an already registered source recovery environment and its location.
          if(!(Test-Registration)){
            & $reagent /enable *> (Join-Path $folder 'enable.log')
            if($LASTEXITCODE -ne 0){throw 'Windows recovery could not be enabled. Review recovery\enable.log.'}
          }
          & $reagent /info *> (Join-Path $folder 'info.log')
          if($LASTEXITCODE -ne 0 -or !(Test-Registration)){throw 'Windows recovery registration did not verify.'}
          [IO.File]::WriteAllText((Join-Path $folder 'complete'),'WinRE registration verified. Actual recovery boot validation is a separate test.')
        } catch {
          Remove-Item -LiteralPath (Join-Path $folder 'complete') -ErrorAction SilentlyContinue
          [IO.File]::WriteAllText((Join-Path $folder 'status.txt'),$_.Exception.Message)
          throw
        } finally {$lock.Dispose()}
        """;

    public const string FirstLogin="""

        Invoke-Step 'windows-recovery' {
          if(!(Test-Path (Join-Path $env:ProgramData 'TechLoungeOS\recovery\complete'))){throw 'Windows recovery was not verified during setup. Review ProgramData\TechLoungeOS\recovery. No automatic elevation was attempted.'}
        }
        """;

    public static DesktopSetup.Step Stage(string mount)
    {
        var folder=Path.Combine(mount,@"ProgramData\TechLoungeOS");
        Policy.RequireUnlinkedPath(folder);Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"recovery.ps1"),Script);
        var file=Path.Combine(folder,"recovery.ps1");
        return new("Recovery",[new("Common",@"TechLoungeOS\recovery.ps1","install.ps1",new FileInfo(file).Length,RuntimePack.Hash(file))]);
    }
}
