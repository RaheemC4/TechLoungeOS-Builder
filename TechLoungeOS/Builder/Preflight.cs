using System.Security.Principal;
using System.Runtime.InteropServices;

namespace TechLoungeOS;

public static class Preflight
{
    public static void ValidatePackage()
    {
        WallpaperPresets.ValidateLibrary();
        WimPacking.ValidatePackage();
        foreach(var name in new[]{"FirstLogin.exe","AccountSetup.exe","Personalise.exe","DiscordLauncher.exe"})
            SourcePreservation.RequireImageFile(Path.Combine(AppContext.BaseDirectory,name),512,1<<20);
    }

    public static void Check(BuildOptions options)
    {
        if (!Environment.Is64BitProcess || RuntimeInformation.OSArchitecture != Architecture.X64 || Environment.OSVersion.Version.Build < 22000)
            throw new InvalidOperationException("Run the x64 builder on Windows 11 x64. ARM64 hosts and other build platforms are not supported in this milestone.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Windows servicing requires administrator rights. Launch TechLoungeOS.Builder.exe and approve its Windows elevation prompt.");
        if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "offreg.dll")))
            throw new InvalidOperationException("The servicing host needs Microsoft's Offline Registry library (offreg.dll) in System32 for source-preservation checks.");
        var workspace = new DriveInfo(Path.GetPathRoot(options.Workspace)!);
        var output = new DriveInfo(Path.GetPathRoot(options.Output)!);
        if (workspace.DriveType == DriveType.Network || output.DriveType == DriveType.Network)
            throw new InvalidOperationException("Use local workspace and output drives. Mapped network drives are not supported.");
        var sourceBytes = new FileInfo(options.Iso).Length;
        // Estimate cannot predict unusually large/custom WIM expansion.
        var workRequired = checked(sourceBytes * 3 + 10L * 1024 * 1024 * 1024);
        var outputRequired = checked(sourceBytes * 2 + 1024L * 1024 * 1024);
        if (options.IncludeOffice) { workRequired += 20L << 30; outputRequired += 8L << 30; }
        if (options.IncludeEdge) { workRequired += 1L << 30; outputRequired += 400L << 20; }
        if (options.WindowsUpdateFolder != null)
            workRequired += checked(WindowsImageUpdates.ReadPlan(options.WindowsUpdateFolder).Packages.Sum(p=>p.Bytes) * 2);
        if (options.UpdateWindows) workRequired += 40L << 30;
        if (options.ExportIso && workspace.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase)) workRequired += outputRequired;
        Policy.ValidateStorage(workspace.DriveFormat, workspace.AvailableFreeSpace, workRequired, true);
        if(options.ExportIso)Policy.ValidateStorage(output.DriveFormat, output.AvailableFreeSpace, outputRequired, false);
        ProbeParent(options.Workspace);
        if(options.ExportIso)ProbeParent(options.Output);
    }

    private static void ProbeParent(string destination)
    {
        var parent = new DirectoryInfo(Path.GetDirectoryName(destination)!);
        while (!parent.Exists) parent = parent.Parent ?? throw new IOException("No accessible destination parent.");
        WorkspaceStorage.ProbeDirectory(parent.FullName);
    }
}
