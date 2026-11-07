using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TechLoungeOS;

public record SetupRegional(string UiLanguage,string SystemLocale,string UserLocale,string InputLocale,bool OverrideRegion=false,bool OverrideKeyboard=false)
{
    public static void ValidateSelections(BuildOptions options)
    {
        if(options.SetupRegion!=null)RequireCulture(options.SetupRegion);
        if(options.SetupKeyboard!=null && !Regex.IsMatch(options.SetupKeyboard,@"\A[0-9A-Fa-f]{8}\z"))throw new ArgumentException("Choose a supported Windows keyboard layout.");
        if(!options.AnimatedWinPe && (options.SetupRegion!=null || options.SetupKeyboard!=null))throw new ArgumentException("Enable the WinPE account wizard to apply its region and keyboard choices.");
    }
    public static string RequireCulture(string name)
    {
        var culture=CultureInfo.GetCultures(CultureTypes.SpecificCultures).FirstOrDefault(c=>string.Equals(c.Name,name,StringComparison.OrdinalIgnoreCase));
        if(culture==null)throw new ArgumentException("Choose a supported region and date format.");
        return culture.Name;
    }
    private static string Language(string value)
    {
        if(!uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var lcid) || lcid>ushort.MaxValue)
            throw new IOException("The image has an invalid Windows language identifier.");
        try{return RequireCulture(CultureInfo.GetCultureInfo((int)lcid).Name);}
        catch(CultureNotFoundException ex){throw new IOException("The image's language identifier is unsupported.",ex);}
    }
    public static SetupRegional FromImage(string mount,BuildOptions options)
    {
        ValidateSelections(options);
        var system=Path.Combine(mount,@"Windows\System32\config\SYSTEM");
        var user=Path.Combine(mount,@"Users\Default\NTUSER.DAT");
        var current=SourcePreservation.ReadDwordValue(system,"Select","Current")??SourcePreservation.ReadDwordValue(system,"Select","Default");
        if(current is null or 0 or >999)throw new IOException("The image has no unambiguous active control set for regional defaults.");
        var root=$"ControlSet{current:000}\\Control\\";
        var install=SourcePreservation.ReadStringValue(system,root+@"Nls\Language","InstallLanguage")??throw new IOException("The image's installed display language could not be read.");
        var ui=Language(install);
        var systemLocale=Language(SourcePreservation.ReadStringValue(system,root+@"Nls\Language","Default")??install);
        var userLocale=RequireCulture(SourcePreservation.ReadStringValue(user,@"Control Panel\International","LocaleName")??systemLocale);
        var keyboards=new List<string>();
        for(var index=1;index<=64;index++)
        {
            var id=SourcePreservation.ReadStringValue(user,@"Keyboard Layout\Preload",index.ToString(CultureInfo.InvariantCulture));
            if(id==null)break;
            if(!Regex.IsMatch(id,@"\A[0-9A-Fa-f]{8}\z"))throw new IOException("The image has an unsupported keyboard preload identifier.");
            var actual=SourcePreservation.ReadStringValue(user,@"Keyboard Layout\Substitutes",id)??id;
            keyboards.Add(ComposeInputLocale(actual,id[4..]));
        }
        if(SourcePreservation.ReadStringValue(user,@"Keyboard Layout\Preload","65")!=null)
            throw new IOException("The image's keyboard list exceeds the supported limit; it was left unchanged.");
        var input=keyboards.Count>0?string.Join(';',keyboards):ui;
        if(options.SetupRegion!=null)systemLocale=userLocale=RequireCulture(options.SetupRegion);
        if(options.SetupKeyboard!=null)
        {
            var id=options.SetupKeyboard.ToUpperInvariant();
            var file=SourcePreservation.ReadStringValue(system,root+@"Keyboard Layouts\"+id,"Layout File");
            if(string.IsNullOrWhiteSpace(file) || Path.GetFileName(file)!=file || !file.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))
                throw new IOException("The selected keyboard is absent from this image. Choose another layout or keep its existing keyboard.");
            SourcePreservation.RequireImageFile(Path.Combine(mount,@"Windows\System32",file),1024,64L<<20);
            input=ComposeInputLocale(id);
        }
        return new(ui,systemLocale,userLocale,input,options.SetupRegion!=null,options.SetupKeyboard!=null);
    }
    public static string ComposeInputLocale(string id,string? language=null)
    {
        if(!Regex.IsMatch(id,@"\A[0-9A-Fa-f]{8}\z"))throw new IOException("The image has an unsupported keyboard identifier.");
        language??=id[4..];
        if(!Regex.IsMatch(language,@"\A[0-9A-Fa-f]{4}\z"))throw new IOException("Invalid input language identifier.");
        _=Language(language);
        return language.ToUpperInvariant()+":"+id.ToUpperInvariant();
    }
    public record Choice(string? Id,string Label);
    public static Choice[] Regions()=>[new(null,"Keep the ISO’s regional defaults"),..CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Where(c=>!string.IsNullOrEmpty(c.Name)).OrderBy(c=>c.DisplayName,StringComparer.CurrentCultureIgnoreCase).Select(c=>new Choice(c.Name,c.DisplayName))];
    public static Choice[] Preferred(IEnumerable<Choice> choices,string uk,string us)=>
        [new(uk,"English (United Kingdom)"),new(us,"English (United States)"),..choices.Where(c=>c.Id!=uk && c.Id!=us)];
    public static Choice[] Keyboards()
    {
        var result=new List<Choice>{new(null,"Keep the ISO’s keyboard layouts")};
        using var layouts=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts");
        if(layouts==null)return result.ToArray();
        var choices=new List<Choice>();
        foreach(var id in layouts.GetSubKeyNames().Where(id=>Regex.IsMatch(id,@"\A[0-9A-Fa-f]{8}\z")))
        {
            using var key=layouts.OpenSubKey(id);
            var label=key?.GetValue("Layout Text",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            var file=key?.GetValue("Layout File",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if(!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(file) && Path.GetFileName(file)==file && file.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))
                choices.Add(new(id.ToUpperInvariant(),label));
        }
        result.AddRange(choices.OrderBy(c=>c.Label,StringComparer.CurrentCultureIgnoreCase));return result.ToArray();
    }
}
