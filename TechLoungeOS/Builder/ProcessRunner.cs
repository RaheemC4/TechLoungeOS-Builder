using System.Diagnostics;
using System.Text;

namespace TechLoungeOS;

public interface ICommandRunner
{
    Task<string> Run(string exe, IEnumerable<string> args, CancellationToken ct = default);
    Task<string> PowerShell(string script, CancellationToken ct = default);
    Task<string> Dism(params string[] args);
}

public sealed class ProcessRunner(Action<string> log, string? scratchDirectory = null, string? dismPath = null) : ICommandRunner
{
    public Task<string> Run(string exe, IEnumerable<string> args, CancellationToken ct = default) =>
        // WinGet emits UTF8; other native tools can still use the Windows console code page.
        Run(exe, args, ct, Path.GetFileName(exe).Equals("winget.exe", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8 : null);

    private async Task<string> Run(string exe, IEnumerable<string> args, CancellationToken ct, Encoding? outputEncoding)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding, StandardErrorEncoding = outputEncoding };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        Exception? loggingFailure = null;
        void Write(string text)
        {
            // A full log drive must not stop pipe draining or prevent recovery commands.
            try { log(text); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Interlocked.CompareExchange(ref loggingFailure, ex, null); }
        }
        Write($"> {Path.GetFileName(exe)} " + (info.ArgumentList.Contains("-Command") ? "[PowerShell operation]" : string.Join(" ", info.ArgumentList)));
        using var process = Process.Start(info) ?? throw new IOException("Could not start " + exe);
        async Task<string> Drain(StreamReader reader)
        {
            var output = new StringBuilder();
            while (await reader.ReadLineAsync() is { } line) { output.AppendLine(line); Write(line); }
            return output.ToString();
        }
        var stdout = Drain(process.StandardOutput);
        var stderr = Drain(process.StandardError);
        // Do not kill DISM during servicing. Cancellation takes effect between operations.
        await process.WaitForExitAsync();
        var result = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0 && process.ExitCode != 3010)
            throw new IOException($"{Path.GetFileName(exe)} failed ({process.ExitCode}). {error} {result}");
        if (loggingFailure != null) throw new IOException("The command finished, but its output could not be logged. Check disk space and access permissions; image recovery may be needed.", loggingFailure);
        ct.ThrowIfCancellationRequested();
        return result;
    }
    public Task<string> PowerShell(string script, CancellationToken ct = default) => Run(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
        ["-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-Command",
            "$env:PSModulePath=(Join-Path $PSHOME 'Modules') + ';' + $env:PSModulePath; $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); try {\n" + script + "\n} catch { [Console]::Error.WriteLine(($_ | Out-String)); exit 1 }"], ct, Encoding.UTF8);
    public Task<string> Dism(params string[] args)
    {
        if (scratchDirectory != null) Directory.CreateDirectory(scratchDirectory);
        return Run(dismPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dism.exe"),
            ["/English", .. scratchDirectory == null ? Array.Empty<string>() :
                ["/ScratchDir:" + scratchDirectory, "/LogPath:" + Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scratchDirectory))!, "dism.log")], .. args]);
    }
}
