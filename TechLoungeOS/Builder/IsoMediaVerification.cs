namespace TechLoungeOS;

// Verify the whole exported tree, including WIM bytes that a boot test may never read.
internal static class IsoMediaVerification
{
    public record Entry(string RelativePath, long Length, string Sha256);

    public static IReadOnlyList<Entry> Snapshot(string media, CancellationToken ct)
    {
        var result = new SortedDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        void Visit(string folder)
        {
            ct.ThrowIfCancellationRequested();
            Policy.RequireUnlinkedPath(folder);
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                ct.ThrowIfCancellationRequested();
                Policy.RequireUnlinkedPath(file);
                SourcePreservation.RequireImageFile(file, 0, long.MaxValue);
                var relative = Path.GetRelativePath(media, file);
                result.Add(relative, new(relative, new FileInfo(file).Length, BootableDrive.Hash(file)));
            }
            foreach (var child in Directory.EnumerateDirectories(folder)) Visit(child);
        }
        Visit(media);
        if (result.Count == 0) throw new IOException("Installation media has no files to verify.");
        return result.Values.ToArray();
    }

    public static async Task Verify(ICommandRunner run, string partial, string manifest, CancellationToken ct)
    {
        Policy.RequireUnlinkedPath(manifest);
        // The GUID-named staged ISO was created by this export. Specify its type
        // explicitly and release only the object returned by our own mount.
        await run.PowerShell($$"""
            # TechLoungeOS complete ISO file verification
            $verificationManifest = {{Policy.Ps(manifest)}}
            $mountedExport = $null
            $verificationFailure = $null
            try {
                $mountedExport = Mount-DiskImage -ImagePath {{Policy.Ps(partial)}} -StorageType ISO -Access ReadOnly -PassThru
                $exportVolumes = @()
                for ($attempt=0; $attempt -lt 20; $attempt++) {
                    $exportVolumes = @($mountedExport | Get-Volume)
                    if ($exportVolumes.Count -eq 1 -and $exportVolumes[0].DriveLetter) { break }
                    Start-Sleep -Milliseconds 500
                }
                if ($exportVolumes.Count -ne 1 -or !$exportVolumes[0].DriveLetter -or $exportVolumes[0].DriveType -ne 'CD-ROM') { throw 'Exported ISO has no unambiguous optical volume.' }
                $verificationRoot = ([string]$exportVolumes[0].DriveLetter) + ':\'
                {{TreeBody}}
            } catch { $verificationFailure = $_ }
            finally {
                if ($mountedExport -and $mountedExport.DevicePath) {
                    $releaseFailure = $null
                    for ($releaseAttempt=0; $releaseAttempt -lt 3; $releaseAttempt++) {
                        try { Dismount-DiskImage -DevicePath $mountedExport.DevicePath | Out-Null; $releaseFailure=$null; break }
                        catch { $releaseFailure=$_; if ($releaseAttempt -lt 2) { Start-Sleep -Milliseconds 500 } }
                    }
                    if ($releaseFailure) {
                        throw ('Private ISO mount needs recovery at ' + {{Policy.Ps(partial)}} + ' (' + $mountedExport.DevicePath + '). Verification: ' + ($verificationFailure | Out-String) + ' Release: ' + ($releaseFailure | Out-String))
                    }
                }
            }
            if ($verificationFailure) { throw $verificationFailure }
            """, ct);
    }

    internal static string DirectoryScript(string root, string manifest) =>
        "$verificationRoot=" + Policy.Ps(root) + ";$verificationManifest=" + Policy.Ps(manifest) + ";\n" + TreeBody;

    private const string TreeBody = """
        $expectedTree = Get-Content -LiteralPath $verificationManifest -Raw | ConvertFrom-Json
        $expectedFiles = New-Object 'Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in @($expectedTree)) {
            if (!$entry.RelativePath -or [IO.Path]::IsPathRooted($entry.RelativePath) -or $entry.RelativePath.Contains(':') -or @($entry.RelativePath.Split('\') | Where-Object {$_ -eq '..' -or $_ -eq '.'}).Count -or $entry.Sha256 -notmatch '\A[0-9A-Fa-f]{64}\z' -or $entry.Length -lt 0) { throw 'Invalid export verification manifest.' }
            $expectedFiles.Add($entry.RelativePath,$entry)
        }
        if (!$expectedFiles.Count) { throw 'Empty export verification manifest.' }
        $basePath = [IO.Path]::GetFullPath($verificationRoot).TrimEnd('\') + '\'
        $pending = New-Object 'Collections.Generic.Stack[string]'
        $pending.Push($basePath)
        $verifiedFiles = 0
        $verifiedBytes = [long]0
        while ($pending.Count) {
            $folder = $pending.Pop()
            if ((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked export directory refused.' }
            foreach ($file in Get-ChildItem -LiteralPath $folder -Force) {
                if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked export entry refused.' }
                if ($file.PSIsContainer) { $pending.Push($file.FullName); continue }
                $absolute = [IO.Path]::GetFullPath($file.FullName)
                if (!$absolute.StartsWith($basePath,[StringComparison]::OrdinalIgnoreCase)) { throw 'Export entry leaves its mounted volume.' }
                $relative = $absolute.Substring($basePath.Length)
                if (!$expectedFiles.ContainsKey($relative)) { throw ('Unexpected exported file: ' + $relative) }
                $expected = $expectedFiles[$relative]
                if ($file.Length -ne $expected.Length -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $expected.Sha256) { throw ('Exported file failed its size/checksum verification: ' + $relative) }
                $verifiedFiles++
                $verifiedBytes += $file.Length
                $expectedFiles.Remove($relative) | Out-Null
            }
        }
        if ($expectedFiles.Count) { throw ('Exported ISO is missing ' + $expectedFiles.Count + ' source files.') }
        'Full ISO readback verified ' + $verifiedFiles + ' files / ' + $verifiedBytes + ' bytes.'
        """;
}
