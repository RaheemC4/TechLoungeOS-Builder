using System.Text.Json;

namespace TechLoungeOS;

public static class IsoExport
{
    public static async Task Create(ICommandRunner run, string media, string output, string oscdimg, Action<string> log, CancellationToken ct, Action<string>? phase = null)
    {
        if (File.Exists(output)) throw new IOException("Output ISO already exists.");
        var bios = Path.Combine(media, @"boot\etfsboot.com");
        var efi = Path.Combine(media, @"efi\microsoft\boot\efisys.bin");
        if (!File.Exists(bios) || !File.Exists(efi)) throw new IOException("Required BIOS/UEFI boot images are missing.");
        // Keep the recognised media type throughout staging and finalisation.
        // Windows storage providers cannot infer ISO from a bare .partial suffix.
        var partial = output + "." + Guid.NewGuid().ToString("N") + ".partial.iso";
        var manifest = partial + ".files.json";
        var manifestOwned = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            log("Recording all installation-media file checksums before ISO export.");
            var expected = IsoMediaVerification.Snapshot(media, ct);
            using (var file = new FileStream(manifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                manifestOwned = true;
                JsonSerializer.Serialize(file, expected);
            }
            await run.Run(oscdimg, ["-m", "-o", "-h", "-u2", "-udfver102", "-bootdata:2#p0,e,b" + bios + "#pEF,e,b" + efi, media, partial]);
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(partial) || new FileInfo(partial).Length == 0) throw new IOException("ISO export produced no output.");
            Policy.RequireUnlinkedPath(partial);
            SourcePreservation.RequireImageFile(partial, 1, long.MaxValue);
            log("Verifying every exported file, including the complete Windows installation image. Keep the app open.");
            phase?.Invoke("Verifying ISO contents");
            await IsoMediaVerification.Verify(run, partial, manifest, ct);
            ct.ThrowIfCancellationRequested();
            log("All ISO files verified. Finalising the output filename; Windows storage or security checks can take additional time. Keep the app open.");
            phase?.Invoke("Finalising output filename");
            File.Move(partial, output, false);
            log("Output filename finalised.");
        }
        finally
        {
            try { if (File.Exists(partial)) File.Delete(partial); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log("Could not remove incomplete ISO " + partial + ": " + ex.Message); }
            try { if (manifestOwned && File.Exists(manifest)) File.Delete(manifest); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log("Could not remove export verification manifest " + manifest + ": " + ex.Message); }
        }
    }
}
