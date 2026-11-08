using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TechLoungeOS;

// Builds account/device/OOBE settings and retains Windows Setup's key/skip UI.
// Disk selection, edition, activation, existing commands and source extensions
// remain owned by Windows Setup and the inherited answer file.
public static class SetupAnswerFile
{
    public record Account(string UserName, string ComputerName, string Password, bool Administrator)
    {
        public override string ToString() => $"Account {{ UserName = {UserName}, ComputerName = {ComputerName}, Password = [redacted], Administrator = {Administrator} }}";
    }
    public static readonly XNamespace Namespace = "urn:schemas-microsoft-com:unattend";
    private static readonly XNamespace Wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";
    public static string ComputerName(string userName,string? chosen)
    {
        if(!string.IsNullOrWhiteSpace(chosen))return chosen;
        var stem=new string(userName.Where(c=>char.IsAsciiLetterOrDigit(c)||c=='-').ToArray()).Trim('-');
        if(stem.Length==0)stem="User";
        stem=char.ToUpperInvariant(stem[0])+stem[1..];
        if(stem.Length>13)stem=stem[..13].TrimEnd('-');
        return stem+"PC";
    }
    public static void Validate(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var user=account.UserName;
        if(string.IsNullOrWhiteSpace(user) || user.Length>20 || user!=user.Trim() || user.EndsWith('.') || user.All(c=>c=='.') ||
            user.Any(c=>char.IsControl(c) || "\"/\\[]:;|=,+*?<>".Contains(c)) ||
            new[]{"Administrator","Guest","DefaultAccount","WDAGUtilityAccount","DefaultUser0","SYSTEM","LOCAL SERVICE","NETWORK SERVICE"}.Contains(user,StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a local username of 1–20 characters without reserved names or Windows account punctuation.");
        var pc=ComputerName(account.UserName,account.ComputerName);
        if(string.IsNullOrWhiteSpace(pc) || pc.Length>15 || pc.StartsWith('-') || pc.EndsWith('-') || !pc.Any(char.IsAsciiLetter) ||
            pc.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-'))
            throw new ArgumentException("PC name must be 1–15 letters, digits or hyphens, contain a letter, and start and end with a letter or digit.");
        if(account.Password==null || account.Password.Length>127 || account.Password.Any(char.IsControl)) throw new ArgumentException("The password contains unsupported characters or exceeds 127 characters.");
        XmlConvert.VerifyXmlChars(user);XmlConvert.VerifyXmlChars(account.Password);
    }
    public static XDocument Create(Account account,string? source=null,bool replaceAccounts=false)
    {
        Validate(account);
        XDocument document;
        if(source==null) document=new(new XElement(Namespace+"unattend"));
        else
        {
            if(source.Length>2<<20) throw new IOException("Existing setup answer file is too large to merge safely.");
            using var reader=XmlReader.Create(new StringReader(source),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
            document=XDocument.Load(reader,LoadOptions.PreserveWhitespace);
            if(document.Root?.Name!=Namespace+"unattend") throw new IOException("Unsupported source setup answer file namespace.");
        }
        var specialize=Shell(document,"specialize");
        Set(specialize,"ComputerName",ComputerName(account.UserName,account.ComputerName));
        var shell=Shell(document,"oobeSystem");
        // An inherited automatic sign-in or local-account definition must be
        // reviewed in the wizard rather than silently changing its credentials.
        if(!replaceAccounts && (shell.Elements(Namespace+"UserAccounts").Any() || shell.Elements(Namespace+"AutoLogon").Any()))
            throw new IOException("This image already configures accounts or automatic sign-in. Review its existing setup before adding a local account.");
        if(replaceAccounts)
        {
            foreach(var name in new[]{"UserAccounts","AutoLogon"})
            {
                var inherited=shell.Elements(Namespace+name).ToArray();
                if(inherited.Length>1)throw new IOException("Ambiguous inherited account setting: "+name);
                inherited.SingleOrDefault()?.Remove();
            }
        }
        var oobes=shell.Elements(Namespace+"OOBE").ToArray();
        if(oobes.Length>1) throw new IOException("Ambiguous source OOBE settings.");
        var oobe=oobes.SingleOrDefault()??new XElement(Namespace+"OOBE");
        if(oobe.Parent==null)shell.Add(oobe);
        Set(oobe,"HideOnlineAccountScreens","true");
        Set(oobe,"HideWirelessSetupInOOBE","true");
        Set(oobe,"ProtectYourPC","3");
        shell.Add(new XElement(Namespace+"UserAccounts",new XElement(Namespace+"LocalAccounts",
            new XElement(Namespace+"LocalAccount",new XAttribute(Wcm+"action","add"),
                new XElement(Namespace+"Name",account.UserName),
                new XElement(Namespace+"DisplayName",account.UserName),
                new XElement(Namespace+"Group",account.Administrator?"Administrators":"Users"),
                new XElement(Namespace+"Password",
                    // Windows answer-file hiding is obfuscation, not encryption.
                    // The PE wizard must keep this per-install file off the ISO/USB.
                    new XElement(Namespace+"Value",Convert.ToBase64String(Encoding.Unicode.GetBytes(account.Password+"Password"))),
                    new XElement(Namespace+"PlainText","false"))))));
        return document;
    }
    // These inert templates contain no credentials. The native wizard fills them in
    // on the WinPE RAM drive only, after the user explicitly chooses an account.
    public static string Template(bool automaticSignin,string? source=null,SetupRegional? regional=null,string? initialWallpaper=null)
    {
        const string user="TLOS_USER_7D91",pc="TLOS-PC-7D91";
        if(source!=null && new[]{user,pc,"TLOS_PASSWORD_7D91","TLOS_AUTO_PASSWORD_7D91","TLOS_HELLO_7D91"}.Any(token=>source.Contains(token,StringComparison.Ordinal)))
            throw new IOException("Source answer file collides with account template markers.");
        var document=Create(new Account(user,pc,"",true),source,true);
        if(regional!=null)ApplyRegional(document,regional);
        if(initialWallpaper!=null) {
            if(!System.Text.RegularExpressions.Regex.IsMatch(initialWallpaper,@"\A%WINDIR%\\Web\\Wallpaper\\TechLoungeOS\\first-desktop\.(png|jpg|jpeg|bmp)\z"))
                throw new IOException("Unsupported initial wallpaper path.");
            foreach(var pass in new[]{"specialize","oobeSystem"}) {
                var component=Shell(document,pass);
                var themes=component.Elements(Namespace+"Themes").ToArray();
                if(themes.Length>1)throw new IOException("Ambiguous source theme settings.");
                var theme=themes.SingleOrDefault()??new XElement(Namespace+"Themes");
                if(theme.Parent==null)component.Add(theme);
                theme.Elements(Namespace+"CustomDefaultThemeFile").Remove();
                Set(theme,"ThemeName","TechLoungeOS");Set(theme,"DesktopBackground",initialWallpaper);
            }
        }
        // An explicit answer file without a ProductKey instruction can stop
        // Windows Setup before edition selection. Keep its normal key/skip UI;
        // never replace an inherited installation key or add an activation key.
        var setup=Component(document,"windowsPE","Microsoft-Windows-Setup");
        var users=setup.Elements(Namespace+"UserData").ToArray();
        if(users.Length>1)throw new IOException("Ambiguous source setup UserData.");
        var userData=users.SingleOrDefault()??new XElement(Namespace+"UserData");
        if(userData.Parent==null)setup.Add(userData);
        var productKeys=userData.Elements(Namespace+"ProductKey").ToArray();
        if(productKeys.Length>1)throw new IOException("Ambiguous source setup ProductKey.");
        if(productKeys.Length==0)userData.Add(new XElement(Namespace+"ProductKey",new XElement(Namespace+"WillShowUI","Always")));
        var shell=Shell(document,"oobeSystem");
        document.Descendants(Namespace+"Password").Single().Element(Namespace+"Value")!.Value="TLOS_PASSWORD_7D91";
        if(automaticSignin)
            shell.Add(new XElement(Namespace+"AutoLogon",
                new XElement(Namespace+"Password",new XElement(Namespace+"Value","TLOS_AUTO_PASSWORD_7D91"),new XElement(Namespace+"PlainText","false")),
                new XElement(Namespace+"Username",user),new XElement(Namespace+"Enabled","true"),new XElement(Namespace+"LogonCount","2")));
        // Setup validates LogonCount even when automatic sign-in is disabled.
        // Manual sign-in must not carry an active-logon count or account credential settings.
        else shell.Add(new XElement(Namespace+"AutoLogon",new XElement(Namespace+"Enabled","false")));
        var lists=shell.Elements(Namespace+"FirstLogonCommands").ToArray();
        if(lists.Length>1)throw new IOException("Ambiguous source first-sign-in commands.");
        var commands=lists.SingleOrDefault()??new XElement(Namespace+"FirstLogonCommands");
        if(commands.Parent==null)shell.Add(commands);
        var orders=commands.Elements(Namespace+"SynchronousCommand").Select(c=>(int?)c.Element(Namespace+"Order")??throw new IOException("Source sign-in command has no order.")).ToArray();
        if(orders.Any(n=>n<1||n>=500)||orders.Distinct().Count()!=orders.Length)
            throw new IOException("Source sign-in command orders cannot be safely extended.");
        var order=Enumerable.Range(1,499).First(n=>!orders.Contains(n));
        commands.Add(new XElement(Namespace+"SynchronousCommand",new XAttribute(Wcm+"action","add"),
            new XElement(Namespace+"Order",order),new XElement(Namespace+"Description","Finish TechLoungeOS account setup"),
            new XElement(Namespace+"CommandLine","\"%ProgramData%\\TechLoungeOS\\AccountSetup.exe\" --user \""+user+"\" --automatic "+(automaticSignin?"1":"0")+" --hello TLOS_HELLO_7D91")));
        return document.ToString(SaveOptions.DisableFormatting);
    }
    public static string WithRegional(string template,SetupRegional regional)
    {
        if(template.Length>2<<20)throw new IOException("The setup template exceeds its size limit.");
        using var reader=XmlReader.Create(new StringReader(template),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
        var document=XDocument.Load(reader,LoadOptions.PreserveWhitespace);
        if(document.Root?.Name!=Namespace+"unattend")throw new IOException("Unsupported setup template namespace.");
        ApplyRegional(document,regional);return document.ToString(SaveOptions.DisableFormatting);
    }
    private static void ApplyRegional(XDocument document,SetupRegional regional)
    {
        _=SetupRegional.RequireCulture(regional.UiLanguage);_=SetupRegional.RequireCulture(regional.SystemLocale);_=SetupRegional.RequireCulture(regional.UserLocale);
        if(string.IsNullOrWhiteSpace(regional.InputLocale) || regional.InputLocale.Length>1024)throw new IOException("Invalid setup keyboard selection.");
        foreach(var part in regional.InputLocale.Split(';'))
            if(!System.Text.RegularExpressions.Regex.IsMatch(part,@"\A[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}\z"))_=SetupRegional.RequireCulture(part);
        foreach(var component in new[]{Component(document,"oobeSystem","Microsoft-Windows-International-Core"),Component(document,"windowsPE","Microsoft-Windows-International-Core-WinPE")})
        {
            void KeepOrSet(string name,string value,bool replace)
            {
                if(component.Elements(Namespace+name).Skip(1).Any())throw new IOException("Ambiguous source regional setting: "+name);
                if(replace || component.Element(Namespace+name)==null)Set(component,name,value);
            }
            KeepOrSet("UILanguage",regional.UiLanguage,false);
            KeepOrSet("SystemLocale",regional.SystemLocale,regional.OverrideRegion);
            KeepOrSet("UserLocale",regional.UserLocale,regional.OverrideRegion);
            KeepOrSet("InputLocale",regional.InputLocale,regional.OverrideKeyboard);
            if((string?)component.Attribute("name")=="Microsoft-Windows-International-Core-WinPE")
            {
                var existing=component.Elements(Namespace+"SetupUILanguage").ToArray();
                if(existing.Length>1)throw new IOException("Ambiguous source Setup UI language.");
                var setup=existing.SingleOrDefault()??new XElement(Namespace+"SetupUILanguage");
                if(setup.Parent==null)component.Add(setup);
                if(setup.Elements(Namespace+"UILanguage").Skip(1).Any())throw new IOException("Ambiguous source Setup UI language value.");
                if(setup.Element(Namespace+"UILanguage")==null)Set(setup,"UILanguage",component.Element(Namespace+"UILanguage")!.Value);
            }
        }
    }
    private static XElement Shell(XDocument document,string pass)
        => Component(document,pass,"Microsoft-Windows-Shell-Setup");
    private static XElement Component(XDocument document,string pass,string name)
    {
        var root=document.Root!;
        var settings=root.Elements(Namespace+"settings").Where(e=>(string?)e.Attribute("pass")==pass).ToArray();
        if(settings.Length>1)throw new IOException("Ambiguous source setup pass: "+pass);
        var setting=settings.SingleOrDefault();
        if(setting==null){setting=new(Namespace+"settings",new XAttribute("pass",pass));root.Add(setting);}
        var components=setting.Elements(Namespace+"component").Where(e=>(string?)e.Attribute("name")==name).ToArray();
        if(components.Length>1)throw new IOException("Ambiguous source setup component: "+name);
        if(components.Length==1)
        {
            var component=components[0];
            if((string?)component.Attribute("processorArchitecture")!="amd64")throw new IOException("The setup wizard requires an x64 component: "+name);
            return component;
        }
        var shell=new XElement(Namespace+"component",new XAttribute("name",name),new XAttribute("processorArchitecture","amd64"),
            new XAttribute("publicKeyToken","31bf3856ad364e35"),new XAttribute("language","neutral"),new XAttribute("versionScope","nonSxS"));
        setting.Add(shell);return shell;
    }
    private static void Set(XElement parent,string name,string value)
    {
        var elements=parent.Elements(Namespace+name).ToArray();
        if(elements.Length>1)throw new IOException("Ambiguous source setup setting: "+name);
        if(elements.Length==1)elements[0].Value=value;
        else parent.Add(new XElement(Namespace+name,value));
    }
}
