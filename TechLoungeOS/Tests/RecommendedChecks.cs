using System.Text.Json;
using TechLoungeOS;

static class RecommendedChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var expected = new[] { "bing_search", "cdm_ads", "telemetry", "ceip", "feedback", "activity_history", "ad_id", "typing_insights", "speech_data", "ink_collection" };
        check(RecommendedProfile.Settings.Select(s => s.Tweak).Distinct().Order().SequenceEqual(expected.Order()) && RecommendedProfile.Settings.Count == 14,
            "automatic Recommended profile contains exactly ten reviewed tweaks and fourteen values, with no GPU, Defender or update pause entries");
        var run = new ProcessRunner(_ => { });
        var fixture = Path.Combine(root, "recommended O'Brien 测试"); Directory.CreateDirectory(fixture);
        var registry = @"HKCU:\Software\TechLoungeOS-RecommendedTests-" + Guid.NewGuid().ToString("N");
        try
        {
            foreach (var machine in new[] { true, false })
            {
                var scope = machine ? "Machine" : "User";
                var state = Path.Combine(fixture, scope); Directory.CreateDirectory(state);
                var settings = RecommendedProfile.Settings.Where(s => s.Scope == scope).ToArray();
                var prefix = registry + "\\" + scope + "\\";
                // Run the production script against an isolated HKCU subtree only.
                var script = RecommendedProfile.Script(machine)
                    .Replace(machine ? "$recommendedHive = 'HKLM:\\'" : "$recommendedHive = 'HKCU:\\'", "$recommendedHive = " + Policy.Ps(prefix))
                    .Replace(machine ? "$recommendedRoot = Join-Path $env:ProgramData 'TechLoungeOS\\recommended-machine'" : "$recommendedRoot = Join-Path $state 'recommended-user'", "$recommendedRoot = " + Policy.Ps(state));
                if (script.Contains("$recommendedHive = 'HKLM:\\'") || script.Contains("$recommendedHive = 'HKCU:\\'")) throw new Exception("Fixture isolation substitution failed.");
                var path = Path.Combine(fixture, scope + ".ps1"); File.WriteAllText(path, script, new System.Text.UTF8Encoding(true));
                var first = settings[0];
                await run.PowerShell("New-Item -Path " + Policy.Ps(prefix + first.Path) + " -Force|Out-Null; New-ItemProperty -LiteralPath " + Policy.Ps(prefix + first.Path) + " -Name " + Policy.Ps(first.Name) + " -Value 'original string' -PropertyType String -Force|Out-Null");
                // One write fails; remaining writes must still run and the failure must stay retryable.
                var fail = $$"""
                    function New-ItemProperty {
                      param($LiteralPath,$Name,$Value,$PropertyType,[switch]$Force)
                      if ($Name -eq {{Policy.Ps(first.Name)}}) { throw 'Synthetic denied write' }
                      Microsoft.PowerShell.Management\New-ItemProperty -LiteralPath $LiteralPath -Name $Name -Value $Value -PropertyType $PropertyType -Force:$Force
                    }
                    & {{Policy.Ps(path)}}
                    """;
                try { await run.PowerShell(fail); throw new Exception("Expected denied write"); }
                catch (IOException ex) when (ex.Message.Contains("Synthetic denied write")) { }
                var status = File.ReadAllText(Path.Combine(state, "status.txt"));
                check(!File.Exists(Path.Combine(state, "complete")) && Directory.GetFiles(state, "*.done").Length == settings.Length - 1,
                    scope + " profile reports a failed write and retains all successful entries: " + status);
                var backupBefore = File.ReadAllBytes(Path.Combine(state, "0.before.xml"));
                await run.PowerShell("& " + Policy.Ps(path));
                var result = await run.PowerShell("$rows=ConvertFrom-Json " + Policy.Ps(JsonSerializer.Serialize(settings)) + "; foreach($row in $rows){$key=Get-Item -LiteralPath (" + Policy.Ps(prefix) + "+$row.Path);try{if($key.GetValue($row.Name) -ne $row.Value -or $key.GetValueKind($row.Name) -ne 'DWord'){throw 'Unexpected readback'}}finally{$key.Close()}}; $b=Import-Clixml -LiteralPath " + Policy.Ps(Path.Combine(state,"0.before.xml")) + "; if($b.Previous.Kind -ne 'String' -or $b.Previous.Value -ne 'original string'){throw 'Original backup lost'}; 'verified'");
                check(result.Contains("verified") && File.Exists(Path.Combine(state, "complete")) && File.ReadAllBytes(Path.Combine(state, "0.before.xml")).SequenceEqual(backupBefore),
                    scope + " retry applies exact DWORD values and preserves original value/type backups");
                await run.PowerShell("Set-ItemProperty -LiteralPath " + Policy.Ps(prefix + first.Path) + " -Name " + Policy.Ps(first.Name) + " -Value 9; & " + Policy.Ps(path) + "; if((Get-ItemPropertyValue -LiteralPath " + Policy.Ps(prefix + first.Path) + " -Name " + Policy.Ps(first.Name) + ") -ne 9){throw 'Completed profile overwrote a later user change'}");
                check(true, scope + " completed profile does not reapply over subsequent user changes");
                if(!machine)
                {
                    File.Delete(Path.Combine(state,"complete"));File.Delete(Path.Combine(state,"0.done"));
                    await run.PowerShell("Set-ItemProperty -LiteralPath "+Policy.Ps(prefix+first.Path)+" -Name "+Policy.Ps(first.Name)+" -Value "+first.Value);
                    await run.PowerShell(fail);
                    check(File.Exists(Path.Combine(state,"complete")) && File.ReadAllText(Path.Combine(state,"0.done")).Contains("Verified preconfigured policy"),
                        "an exactly preconfigured user-policy DWORD completes without attempting a denied write");
                }
            }
            var mount = Path.Combine(fixture, "mount"); var media = Path.Combine(fixture,"media");
            Directory.CreateDirectory(Path.Combine(mount,@"ProgramData\TechLoungeOS"));
            var inherited = System.Text.Encoding.ASCII.GetBytes("@echo original-vendor-hook\r\nexit /b 0\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(SetupHooks.ImageHook(mount))!); File.WriteAllBytes(SetupHooks.ImageHook(mount), inherited);
            RecommendedProfile.StageMachine(mount); SetupHooks.FinalizeOem(media,mount);
            check(File.ReadAllBytes(SetupHooks.ImageHook(mount)).AsSpan().EndsWith(inherited) && File.ReadAllBytes(Path.Combine(SetupHooks.OemScripts(media), "SetupComplete.cmd")).SequenceEqual(File.ReadAllBytes(SetupHooks.ImageHook(mount))),
                "Recommended setup prefix preserves inherited hook bytes and OEM precedence");
            var user = Provisioning.Script([],false,false,recommended:true);
            var noTweaks = Provisioning.Script([],false,false);
            await run.PowerShell("$t=$null;$e=$null;[void][System.Management.Automation.Language.Parser]::ParseInput(" + Policy.Ps(user) + ",[ref]$t,[ref]$e);if($e.Count){throw ($e|Out-String)}");
            check(user.Contains("Invoke-Step 'recommended-user'") && user.Contains("Invoke-Step 'recommended-machine'") && !user.Contains("$recommendedHive = 'HKLM:") && !noTweaks.Contains("Invoke-Step 'recommended-"),
                "first login parses in PowerShell 5.1, reports skipped machine setup and only writes user settings when Tweaks integration is selected");
        }
        finally { await run.PowerShell("if(Test-Path -LiteralPath " + Policy.Ps(registry) + "){Remove-Item -LiteralPath " + Policy.Ps(registry) + " -Recurse -Force}"); }
    }
}
