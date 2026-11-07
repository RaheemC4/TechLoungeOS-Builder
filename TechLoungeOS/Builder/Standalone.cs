namespace TechLoungeOS;

// The supplied original remains untouched for the owner's records. It is not
// embedded in Builder packages or scheduled by generated Windows installations.
public static class Standalone
{
    public const string SourceHash="F9D6C117B77B2BE56DC7745594818FB690EAF4415F176B4CF3614658F427C853";
    private static NotSupportedException Unavailable() => new("The supplied Standalone includes an Office activation bypass and is not part of Builder deployment. Install Office normally and activate with your own account or licence.");
    public static void Stage(string mount,string[] apps,bool office) => throw Unavailable();
    public static string Queue => throw Unavailable();
    public static string FirstLogin(string[] apps) => throw Unavailable();
}