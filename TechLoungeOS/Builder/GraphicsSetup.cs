namespace TechLoungeOS;

public static class GraphicsSetup
{
    public const string RelativeFolder=@"Windows\Setup\TechLoungeOS-Graphics";
    public static string Script
    {
        get
        {
            using var stream=typeof(GraphicsSetup).Assembly.GetManifestResourceStream("TechLoungeOS.GraphicsSetup.ps1")??throw new IOException("Graphics setup worker was not packaged.");
            using var reader=new StreamReader(stream);return reader.ReadToEnd();
        }
    }
    public static void Stage(string mount)
    {
        var folder=Path.Combine(mount,RelativeFolder);Policy.RequireUnlinkedPath(folder);
        if(Directory.Exists(folder))throw new IOException("The source already contains this graphics setup payload.");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"install.ps1"),Script);
        File.WriteAllText(Path.Combine(folder,"queue.ps1"),GraphicsQueue.Script(nvidia:false));
        File.WriteAllText(Path.Combine(folder,"driver-lock.ps1"),GraphicsQueue.LockScript);
        Provisioning.WriteHashes(folder);
        SetupHooks.Prefix(mount,"@\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"%SystemRoot%\\Setup\\TechLoungeOS-Graphics\\queue.ps1\"\r\n");
    }
    public static string FirstLogin=>GraphicsQueue.FirstLogin(false);
}
