namespace TechLoungeOS;

public static class RecommendedApps
{
    public const string Discord = "TechLounge.DiscordOpenAsar";
    public const string ChatGPT = "TechLounge.ChatGPT";
    public const string ChatGPTStore = "9PLM9XGG6VKS";
    public const string HatchZip = "TechLounge.HatchZip";
    public static bool IsDiscord(string id) => id.Equals(Discord,StringComparison.OrdinalIgnoreCase)||id.Equals("Discord.Discord",StringComparison.OrdinalIgnoreCase);
    public static bool IsChatGPT(string id) => id.Equals(ChatGPT,StringComparison.OrdinalIgnoreCase)||id.Equals("OpenAI.ChatGPT",StringComparison.OrdinalIgnoreCase);
    public static bool UnsupportedDiscord(string id) => !IsDiscord(id) && (id.Contains("discord",StringComparison.OrdinalIgnoreCase)||new[]{"Vencord.","Equicord.","ArmCord.","Vesktop."}.Any(p=>id.StartsWith(p,StringComparison.OrdinalIgnoreCase)));
    public static string NormalizeId(string id)
    {
        if(UnsupportedDiscord(id))throw new InvalidOperationException("Choose Discord from Recommended apps. Other Discord editions are not supported.");
        return IsDiscord(id)?Discord:IsChatGPT(id)?ChatGPT:id.Equals(HatchZip,StringComparison.OrdinalIgnoreCase)?HatchZip:id;
    }
    public static string[] NormalizeIds(IEnumerable<string> ids)=>ids.Select(NormalizeId).ToArray();
    public static string[] ForBuild(IEnumerable<string> ids)=>NormalizeIds(ids.Append(HatchZip)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static bool IncludedInIso(string id) => id.Equals("Microsoft.Office",StringComparison.OrdinalIgnoreCase) || id.Equals("Microsoft.Edge",StringComparison.OrdinalIgnoreCase) || id.Equals(HatchZip,StringComparison.OrdinalIgnoreCase);
    public static string Identity(string id) => IsDiscord(id) ? "Discord.Discord" : IsChatGPT(id)?"OpenAI.ChatGPT":id;
    public static CatalogApp[] Selectable(IEnumerable<CatalogApp> apps) => apps
        .Where(a=>!UnsupportedDiscord(a.Id) && (IsDiscord(a.Id)||!a.Name.Contains("Discord",StringComparison.OrdinalIgnoreCase)))
        .Select(a=>IsDiscord(a.Id)?All[0]:IsChatGPT(a.Id)?All.Single(x=>x.Id==ChatGPT):a)
        .DistinctBy(a=>Identity(a.Id),StringComparer.OrdinalIgnoreCase).ToArray();
    public static readonly string[] PresetIds=[Discord,"Brave.Brave","Google.Chrome","Valve.Steam","Guru3D.Afterburner","Stremio.Stremio","Proton.ProtonVPN",ChatGPT,"REALiX.HWiNFO"];
    public static CatalogApp[] ApplyPreset(IEnumerable<CatalogApp> selected) => Selectable(selected.Concat(PresetIds.Select(id=>All.Single(a=>a.Id==id))));
    public static readonly CatalogApp[] All = [
        new("Discord", Discord, "Latest", "Chat and voice for your communities"),
        new("HatchZip", "TechLounge.HatchZip", "TechLounge setup", "File tools · Extract ZIP, 7z and tar archives. Extraction consumes the source archive."),
        new("Shiftd", "TechLounge.Shiftd", "TechLounge setup", "File tools · Offline file conversion, compression, image resizing and audio/video trimming."),
        new("Steam", "Valve.Steam", "Latest", "Game clients"),
        new("Epic Games Launcher", "EpicGames.EpicGamesLauncher", "Latest", "Game clients"),
        new("Ubisoft Connect", "Ubisoft.Connect", "Latest", "Game clients"),
        new("EA App", "ElectronicArts.EADesktop", "Latest", "Game clients"),
        new("GOG Galaxy", "GOG.Galaxy", "Latest", "Game clients"),
        new("Battle.net", "Blizzard.BattleNet", "Latest", "Game clients"),
        new("Rockstar Games Launcher", "RockstarGames.Launcher", "Latest", "Game clients"),
        new("Amazon Games", "Amazon.Games", "Latest", "Game clients"),
        new("Brave", "Brave.Brave", "Latest", "Browsers"),
        new("Google Chrome", "Google.Chrome", "Latest", "Browsers"),
        new("Mozilla Firefox", "Mozilla.Firefox", "Latest", "Browsers"),
        new("Opera GX", "Opera.OperaGX", "Latest", "Browsers"),
        new("Vivaldi", "Vivaldi.Vivaldi", "Latest", "Browsers"),
        new("Microsoft Edge", "Microsoft.Edge", "Included in ISO", "Browsers · Offline installer; default for new Windows users"),
        new("Microsoft Edge WebView2", "Microsoft.EdgeWebView2Runtime", "Latest", "Browsers · Optional shared runtime; separate from the private Tweaks runtime"),
        new("MSI Afterburner", "Guru3D.Afterburner", "Latest", "Tuning tools · Publisher package; bundled options depend on the installer"),
        new("Stremio", "Stremio.Stremio", "Latest", "Media centre"),
        new("Proton VPN", "Proton.ProtonVPN", "Latest", "VPN · Sign in with your own Proton account"),
        new("ChatGPT", ChatGPT, "Latest", "Official OpenAI app · Microsoft Store access required"),
        new("HWiNFO64", "REALiX.HWiNFO", "Latest", "Hardware monitoring · Free for non-commercial use")
    ];
}
