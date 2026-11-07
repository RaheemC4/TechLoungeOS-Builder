using TechLoungeOS;

static class ProvisioningChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "O'Brien 测试 first-login");
        Directory.CreateDirectory(folder);
        var fake = Path.Combine(folder, "fake-winget.cmd");
        File.WriteAllText(fake, "@echo off\r\necho %*>>\"%TL_TEST_LOG%\"\r\nif exist \"%TL_FAIL_FIRST%\" if \"%3\"==\"Valve.Steam\" exit /b 1\r\nexit /b 0\r\n");
        var fail = Path.Combine(folder, "fail-first"); File.WriteAllText(fail, "fixture");
        var header = $"$env:LOCALAPPDATA={Policy.Ps(folder)};$env:ProgramData={Policy.Ps(Path.Combine(folder, "unused"))};$env:TL_TEST_LOG={Policy.Ps(Path.Combine(folder, "calls.txt"))};$env:TL_FAIL_FIRST={Policy.Ps(fail)}; "
            + "function Get-Command { param($Name,$ErrorAction) [pscustomobject]@{ Source=" + Policy.Ps(fake) + "} }; ";
        var runner = new ProcessRunner(_ => { });
        var script = Provisioning.Script(["Valve.Steam", "VideoLAN.VLC"], false, false);
        try { await runner.PowerShell(header + script); }
        catch (IOException) { /* First app deliberately fails. Only fixture commands can run. */ }
        var state = Path.Combine(folder, "TechLoungeOS");
        check(File.Exists(Path.Combine(state, "VideoLAN.VLC.done")), "one failed installer does not block the next app");
        check(!File.Exists(Path.Combine(state, "complete")), "partial provisioning remains retryable");
        File.Delete(fail);
        await runner.PowerShell(header + script);
        check(File.Exists(Path.Combine(state, "Valve.Steam.done")) && File.Exists(Path.Combine(state, "complete")), "failed app succeeds on subsequent login");
        var calls = File.ReadAllLines(Path.Combine(folder, "calls.txt"));
        check(calls.Count(l => l.Contains("VideoLAN.VLC")) == 1 && calls.Count(l => l.Contains("Valve.Steam")) == 2, "retry preserves successes and only retries failed installer");
        await runner.PowerShell(header + script);
        check(File.ReadAllLines(Path.Combine(folder, "calls.txt")).Length == calls.Length, "completed login is a no-op with Unicode and apostrophe paths");
        File.Delete(Path.Combine(state, "complete")); File.Delete(Path.Combine(state, "Valve.Steam.done"));
        using (var locked = new FileStream(Path.Combine(state, "provision.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try { await runner.PowerShell(header + script); throw new Exception("Expected lock rejection"); }
            catch (IOException) { check(File.ReadAllLines(Path.Combine(folder, "calls.txt")).Length == calls.Length, "concurrent provisioning cannot start another installer"); }
        }
        var missing = header + "function Get-Command { param($Name,$ErrorAction) throw 'winget unavailable' }; " + script;
        try { await runner.PowerShell(missing); throw new Exception("Expected missing winget rejection"); }
        catch (IOException) { check(File.ReadAllText(Path.Combine(state, "status.txt")).Contains("Valve.Steam: winget unavailable") && !File.Exists(Path.Combine(state, "complete")), "missing winget produces readable retry status"); }

        File.WriteAllText(fake, "@echo off\r\necho %*>>\"%TL_TEST_LOG%\"\r\nexit /b %TL_TEST_EXIT%\r\n");
        foreach (var code in new[] { -1978335135, -1978335189, -1978335224, -1978335215, -1978335217 })
        {
            var caseFolder = Path.Combine(folder, "exit-" + code);
            var caseState = Path.Combine(caseFolder, "TechLoungeOS");
            var invocation = header + $"$env:LOCALAPPDATA={Policy.Ps(caseFolder)};$env:TL_TEST_EXIT='{code}'; "
                + Provisioning.Script(["7zip.7zip"], false, false);
            var succeeded = true;
            try { await runner.PowerShell(invocation); }
            catch (IOException) { succeeded = false; }
            var alreadyInstalled = code == -1978335135;
            check(succeeded == alreadyInstalled && File.Exists(Path.Combine(caseState, "7zip.7zip.done")) == alreadyInstalled
                && File.Exists(Path.Combine(caseState, "complete")) == alreadyInstalled,
                $"WinGet exit {code} only completes for explicitly already-installed packages");
            if (!alreadyInstalled)
                check(File.ReadAllText(Path.Combine(caseState, "status.txt")).Contains($"exit {code}"),
                    $"WinGet exit {code} retained in actionable retry status");
        }
        var installedCalls = File.ReadAllLines(Path.Combine(folder, "calls.txt")).Where(l => l.Contains("7zip.7zip")).ToArray();
        check(installedCalls.Length == 5 && installedCalls.All(l => l.Contains("--no-upgrade") && l.Contains("--exact --source winget")),
            "existing versions are preserved using exact WinGet selection and no-upgrade");
        var refreshes = File.ReadAllLines(Path.Combine(folder, "calls.txt")).Where(l => l.StartsWith("source ")).ToArray();
        check(refreshes.SequenceEqual(new[] { "source update --name winget --disable-interactivity", "source update --name winget --disable-interactivity" }),
            "missing source data and checksum mismatch each refresh only their named catalog once; unrelated installer errors do not reset sources");
        File.WriteAllText(fake, "@echo off\r\necho %*>>\"%TL_TEST_LOG%\"\r\nif \"%1\"==\"source\" (type nul >\"%TL_FAIL_FIRST%\" & exit /b 0)\r\nif not exist \"%TL_FAIL_FIRST%\" exit /b %TL_TEST_EXIT%\r\nexit /b 0\r\n");
        foreach(var catalogCode in new[]{-1978335217,-1978335215}) {
        var recoveryFolder = Path.Combine(folder, "source-starting-"+catalogCode);
        var recoveryLog = Path.Combine(folder, "source-recovery-calls-"+catalogCode+".txt");
        var marker = Path.Combine(folder, "source-ready-"+catalogCode);
        await runner.PowerShell(header + $"$env:TL_TEST_EXIT='{catalogCode}';$env:LOCALAPPDATA={Policy.Ps(recoveryFolder)};$env:TL_TEST_LOG={Policy.Ps(recoveryLog)};$env:TL_FAIL_FIRST={Policy.Ps(marker)}; "
            + Provisioning.Script(["7zip.7zip"], false, false));
        check(File.Exists(Path.Combine(recoveryFolder, "TechLoungeOS", "complete"))
            && File.ReadAllLines(recoveryLog).SequenceEqual(new[] {
                "install --id 7zip.7zip --exact --source winget --no-upgrade --silent --accept-source-agreements --accept-package-agreements --disable-interactivity",
                "source update --name winget --disable-interactivity",
                "install --id 7zip.7zip --exact --source winget --no-upgrade --silent --accept-source-agreements --accept-package-agreements --disable-interactivity" }),
            $"catalog error {catalogCode} recovers with one refresh and exact checksum-verified app retry in the same sign-in");
        }
        File.WriteAllText(fake,"@echo off\r\necho %*>>\"%TL_TEST_LOG%\"\r\nif \"%1\"==\"source\" exit /b 0\r\nexit /b -1978335215\r\n");
        var hashFolder=Path.Combine(folder,"persistent-hash");var hashLog=Path.Combine(folder,"persistent-hash-calls.txt");
        try{await runner.PowerShell(header+$"$env:LOCALAPPDATA={Policy.Ps(hashFolder)};$env:TL_TEST_LOG={Policy.Ps(hashLog)};"+Provisioning.Script(["Google.Chrome"],false,false));throw new Exception("Expected hash rejection");}
        catch(IOException){check(File.ReadAllLines(hashLog).Length==3 && !File.Exists(Path.Combine(hashFolder,"TechLoungeOS","Google.Chrome.done")) && !File.Exists(Path.Combine(hashFolder,"TechLoungeOS","complete")) && File.ReadAllText(Path.Combine(hashFolder,"TechLoungeOS","status.txt")).Contains("No unverified installer was run"),"persistent checksum mismatch stops after one refresh/two verified attempts and remains retryable without a false completion");}

        File.WriteAllText(fake,"@echo off\r\necho %*>>\"%TL_TEST_LOG%\"\r\nexit /b 0\r\n");
        var chromeFolder=Path.Combine(folder,"chrome-versioned");var chromeLog=Path.Combine(folder,"chrome-calls.txt");
        await runner.PowerShell(header+$"$env:LOCALAPPDATA={Policy.Ps(chromeFolder)};$env:TL_TEST_LOG={Policy.Ps(chromeLog)};"+Provisioning.Script(["Google.Chrome"],false,false));
        check(File.Exists(Path.Combine(chromeFolder,"TechLoungeOS","Google.Chrome.done"))&&File.ReadAllLines(chromeLog).SequenceEqual(new[]{"install --id Google.Chrome.EXE --exact --source winget --no-upgrade --silent --accept-source-agreements --accept-package-agreements --disable-interactivity"}),"normal Chrome selection uses the checksum-verified versioned EXE package while retaining its normal selection/retry identity");
        foreach (var scenario in new[] { "missing", "current", "disabled", "absent", "permission", "unready" })
        {
            var log = Path.Combine(folder, "defender-" + scenario + ".txt");
            var command = $$"""
                $scenario='{{scenario}}'; $script:reads=0
                function Invoke-Step { param($name,$action) & $action }
                function Get-Service { param($Name,$ErrorAction) if($scenario -ne 'absent'){[pscustomobject]@{Status='Running'} } }
                function Get-MpPreference { param($ErrorAction) [pscustomobject]@{DisableRealtimeMonitoring=($scenario -eq 'disabled')} }
                function Get-MpComputerStatus { param($ErrorAction)
                  $script:reads++
                  $ready=$scenario -eq 'current' -or ($script:reads -gt 1 -and $scenario -ne 'unready')
                  [pscustomobject]@{AntivirusEnabled=$true;AntivirusSignatureVersion=$(if($ready){'1.461.186.0'}else{'0.0.0.0'});AMEngineVersion=$(if($ready){'1.1.26090.9'}else{'0.0.0.0'});RealTimeProtectionEnabled=$ready}
                }
                function Update-MpSignature { param($ErrorAction) Add-Content {{Policy.Ps(log)}} 'definitions-only'; if($scenario -eq 'permission'){throw 'Access denied'} }
                {{Provisioning.DefenderFirstLogin}}
                """;
            var succeeded = true; string error = "";
            try { await runner.PowerShell(command); } catch (IOException ex) { succeeded = false; error = ex.Message; }
            var needsUpdate = scenario is "missing" or "permission" or "unready";
            check(succeeded == (scenario is not ("permission" or "unready")) && File.Exists(log) == needsUpdate
                && (!needsUpdate || File.ReadAllLines(log).SequenceEqual(new[] { "definitions-only" }))
                && (succeeded || error.Contains("An administrator can run Update-MpSignature")),
                "Defender " + scenario + " preserves source settings and only updates missing definitions with actionable failures");
        }
    }
}
