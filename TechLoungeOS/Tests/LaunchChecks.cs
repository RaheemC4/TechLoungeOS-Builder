using TechLoungeOS;

static class LaunchChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        var line = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "WinPeLaunch.cmd"))
            .Single(l => l.StartsWith('>') && l.Contains("TL-RESULT.txt"));
        foreach (var code in new[] { 0, 255 })
        {
            var script = Path.Combine(root, "marker-check.cmd");
            File.WriteAllText(script, $"@echo off\r\nset \"TARGET={root}\"\r\nset RESULT={code}\r\n{line}\r\n");
            await new ProcessRunner(_=>{}).Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), ["/d", "/c", script]);
            check(File.ReadAllText(Path.Combine(root, "TL-RESULT.txt")).Trim() == code.ToString(),
                $"real CMD launcher records exit {code} without treating its digit as a stream redirect");
        }
        if (!Environment.GetEnvironmentVariable("SystemDrive")!.Equals("X:", StringComparison.OrdinalIgnoreCase))
        {
            try { await new ProcessRunner(_=>{}).Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), ["/d", "/c", Path.Combine(AppContext.BaseDirectory, "WinPeLaunch.cmd")]); throw new Exception("Launcher accepted a non-PE host"); }
            catch (IOException ex) when (ex.Message.Contains("failed (1)")) { check(true, "PE launcher rejects the installed host before initialisation or shutdown commands"); }
        }
    }
}
