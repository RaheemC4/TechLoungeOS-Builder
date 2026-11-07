using System.Text;
using TechLoungeOS;

internal static class ProcessOutputChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        const string expected = "HWiNFO\u00ae O'Brien \u6d4b\u8bd5 \u00e9";
        var folder = Path.Combine(root, "process-output");
        Directory.CreateDirectory(folder);
        var winget = Path.Combine(folder, "winget.exe");
        var native = Path.Combine(folder, "native-output.exe");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ProcessOutputFixture.exe"), winget);
        File.Copy(winget, native);
        var original = Console.OutputEncoding;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string powershell, output, nativeOutput;
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        IOException? failure = null;
        IOException? powershellFailure = null;
        try
        {
            Console.OutputEncoding = Encoding.GetEncoding(437);
            var runner = new ProcessRunner(logs.Enqueue);
            powershell = (await runner.PowerShell("[Console]::WriteLine('HWiNFO'+[char]0x00ae+' O'+[char]39+'Brien '+[char]0x6d4b+[char]0x8bd5+' '+[char]0x00e9)")).Trim();
            try { await runner.PowerShell("throw ('HWiNFO'+[char]0x00ae+' O'+[char]39+'Brien '+[char]0x6d4b+[char]0x8bd5+' '+[char]0x00e9)"); }
            catch (IOException ex) { powershellFailure = ex; }
            logs.Clear();
            output = (await runner.Run(winget, ["utf8", "ok"])).Trim();
            try { await runner.Run(winget, ["utf8", "error"]); }
            catch (IOException ex) { failure = ex; }
            nativeOutput = (await runner.Run(native, ["oem", "ok"])).Trim();
        }
        finally { Console.OutputEncoding = original; }
        check(powershell == expected, "PowerShell Unicode survives a legacy parent output code page");
        check(powershellFailure?.Message.Contains(expected) == true,
            "PowerShell failures retain readable Unicode in their error stream");
        check(output == expected && logs.Count(line => line == expected) == 4,
            "WinGet UTF8 stdout and stderr retain accented, CJK and punctuation characters");
        check(failure != null && failure.Message.Contains("failed (7)") &&
            failure.Message.Split(expected, StringSplitOptions.None).Length == 3,
            "failed WinGet retains readable Unicode from both output streams");
        check(nativeOutput == "Native \u00e9", "other native commands retain their parent-code-page decoder");
    }
}
