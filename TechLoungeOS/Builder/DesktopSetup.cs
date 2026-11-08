using System.Security.Cryptography;
using System.Text.Json;

namespace TechLoungeOS;

// One fixed SYSTEM action will finish machine installers after the desktop exists.
// The protected build manifest pins input bytes before they reach the privileged cache.
public static class DesktopSetup
{
    public record Input(string Root, string Path, string Destination, long Bytes, string Sha256);
    public record Step(string Id, Input[] Files);
    private static readonly string[] Order = ["Runtimes", "Edge", "StoreXbox", "HWiNFO", "LegacyNetwork", "Recovery", "Office"];

    public static bool Relative(string? path) => path is { Length: > 0 and <= 220 } &&
        !path.Any(c => char.IsControl(c) || c is ':' or '/' or '*' or '?' or '"' or '<' or '>' or '|') &&
        path.Split('\\').All(part => part.Length > 0 && part is not ("." or "..") &&
            !part.EndsWith('.') && !part.EndsWith(' ') &&
            !System.Text.RegularExpressions.Regex.IsMatch(part.Split('.')[0],
                @"\A(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[0-9\u00b9\u00b2\u00b3]|LPT[0-9\u00b9\u00b2\u00b3])\z",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant));

    public static string Folder(string mount) => Path.Combine(mount, @"ProgramData\TechLoungeOS\MachineSetup");

    public static Step Snapshot(string id, string sourceRoot, string sourcePrefix, string folder)
    {
        if (!Order.Contains(id, StringComparer.Ordinal) || sourceRoot is not ("Windows" or "Common") || !Relative(sourcePrefix))
            throw new IOException("Unsupported desktop setup input.");
        Policy.RequireUnlinkedPath(folder);
        var files = Files(folder).Order(StringComparer.Ordinal).Select(file =>
        {
            Policy.RequireUnlinkedPath(file);
            var relative = Path.GetRelativePath(folder, file);
            if (!Relative(relative)) throw new IOException("Unsupported desktop setup filename.");
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new Input(sourceRoot, sourcePrefix + "\\" + relative, relative, input.Length,
                Convert.ToHexString(SHA256.HashData(input)));
        }).ToArray();
        if (files.Length is < 1 or > 4096 || files.Sum(f => f.Bytes) > 10L * 1024 * 1024 * 1024)
            throw new IOException("Desktop setup payload exceeds its budget.");
        Validate(new(id, files));
        return new(id, files);
    }

    public static void Validate(Step step)
    {
        if (step == null || !Order.Contains(step.Id, StringComparer.Ordinal) || step.Files == null || step.Files.Length is < 1 or > 4096 ||
            step.Files.Any(f => f == null || f.Root is not ("Windows" or "Common") || !Relative(f.Path) || !Relative(f.Destination) ||
                f.Bytes < 0 || f.Bytes > 4L * 1024 * 1024 * 1024 || f.Sha256 is not { Length: 64 } || !f.Sha256.All(Uri.IsHexDigit)) ||
            step.Files.Select(f => f.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != step.Files.Length ||
            step.Files.Sum(f => f.Bytes) > 10L * 1024 * 1024 * 1024)
            throw new IOException("Unsafe desktop setup manifest.");
    }

    private static IEnumerable<string> Files(string folder, int depth = 0)
    {
        if (depth > 32) throw new IOException("Desktop setup payload folders are nested too deeply.");
        Policy.RequireUnlinkedPath(folder);
        foreach (var path in Directory.EnumerateFileSystemEntries(folder))
        {
            Policy.RequireUnlinkedPath(path);
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
            {
                foreach (var file in Files(path, depth + 1)) yield return file;
            }
            else yield return path;
        }
    }

    public static string Worker
    {
        get
        {
            using var input = typeof(DesktopSetup).Assembly.GetManifestResourceStream("TechLoungeOS.DesktopSetupWorker.ps1")
                ?? throw new IOException("Desktop setup worker was not packaged.");
            using var reader = new StreamReader(input);
            return reader.ReadToEnd();
        }
    }

    public static string FirstLogin => WaitFor("core-ready","machine-setup");
    public static string OfficeFirstLogin => WaitFor("complete","office-install");
    private static string WaitFor(string receipt,string name) => $$$"""
        Invoke-Step '{{{name}}}' {
          $machine=Join-Path $env:ProgramData 'TechLoungeOS\MachineSetup'
          if(!(Test-Path -LiteralPath "$machine\queue-ready")){throw 'Windows setup registration is incomplete. Ask an administrator to review MachineSetup\queue.ps1. No elevation was requested.'}
          if(!(Test-Path -LiteralPath "$machine\{{{receipt}}}")){
            $requested=[DateTime]::UtcNow
            Start-ScheduledTask -TaskName 'TechLoungeOS-MachineSetup' -ErrorAction Stop
            $timer=[Diagnostics.Stopwatch]::StartNew()
            while(!(Test-Path -LiteralPath "$machine\{{{receipt}}}")){
              $path=Join-Path $machine 'status.json'
              if(Test-Path -LiteralPath $path){
                $stream=[IO.FileStream]::new($path,'Open','Read',([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
                try{$reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8);$status=$reader.ReadToEnd()|ConvertFrom-Json}finally{if($reader){$reader.Dispose()}else{$stream.Dispose()}}
                Write-SetupProgress ([string]$status.step) ([string]$status.message) 'installing'
                if($status.phase -eq 'failed' -and ([DateTime]$status.updatedUtc).ToUniversalTime() -ge $requested){throw [string]$status.message}
              }
              if($timer.Elapsed.TotalSeconds -gt 5 -and (Get-ScheduledTask -TaskName 'TechLoungeOS-MachineSetup' -ErrorAction Stop).State -ne 'Running'){
                if(!(Test-Path -LiteralPath "$machine\{{{receipt}}}")){throw 'Required setup stopped before completion. Review MachineSetup\status.json and retry. Successful installers are retained.'}
              }
              # Release only the observer after an hour. Never terminate an installer.
              if($timer.Elapsed.TotalMinutes -ge 60){throw 'An installer is still running. Your desktop remains available. Review setup progress before retrying; the installer was not stopped.'}
              Start-Sleep -Milliseconds 500
            }
          }
        }
        if(!(Test-Path -LiteralPath "$state\{{{name}}}.done")){Set-Content "$state\status.txt" $script:failures.ToArray();Write-SetupProgress '' ($script:failures -join [Environment]::NewLine) 'failed';exit 1}
        """;

    public static void Stage(string mount, IEnumerable<Step> installers)
    {
        var steps=installers.ToArray();
        foreach(var step in steps)Validate(step);
        if(steps.Length is < 1 or > 7 || steps.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()!=steps.Length)
            throw new IOException("Unsupported desktop setup step set.");
        steps=steps.OrderBy(s=>Array.IndexOf(Order,s.Id)).ToArray();
        var folder=Folder(mount);Policy.RequireUnlinkedPath(folder);
        if(Directory.Exists(folder))throw new IOException("The image already contains a desktop setup queue.");
        SourcePreservation.RequireImageFile(Path.Combine(mount,@"ProgramData\TechLoungeOS\FirstLogin.exe"),512,1<<20);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"steps.json"),JsonSerializer.Serialize(steps));
        File.WriteAllText(Path.Combine(folder,"worker.ps1"),Worker);
        File.WriteAllText(Path.Combine(folder,"runtime-policy.ps1"),TweaksRuntime.Worker);
        File.WriteAllText(Path.Combine(folder,"queue.ps1"),FixedSetupTask.Queue("TechLoungeOS-MachineSetup"));
        SetupHooks.Prefix(mount,"@\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"%ProgramData%\\TechLoungeOS\\MachineSetup\\queue.ps1\"\r\n");
    }
}
