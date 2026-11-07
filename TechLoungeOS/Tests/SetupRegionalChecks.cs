using System.Text;
using System.Xml.Linq;
using TechLoungeOS;

static class SetupRegionalChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var mount=Path.Combine(root,"regional-source");
        var system=Path.Combine(mount,@"Windows\System32\config\SYSTEM");
        var user=Path.Combine(mount,@"Users\Default\NTUSER.DAT");
        static byte[] Text(string value)=>Encoding.Unicode.GetBytes(value+"\0");
        SettingsChecks.WriteValues(system,[
            ("Select","Current",4,BitConverter.GetBytes(2u)),
            (@"ControlSet002\Control\Nls\Language","InstallLanguage",1,Text("0409")),
            (@"ControlSet002\Control\Nls\Language","Default",1,Text("0407")),
            (@"ControlSet002\Control\Keyboard Layouts\00000809","Layout File",1,Text("kbduk.dll"))]);
        SettingsChecks.WriteValues(user,[
            (@"Control Panel\International","LocaleName",1,Text("fr-CA")),
            (@"Keyboard Layout\Preload","1",1,Text("0000040c")),
            (@"Keyboard Layout\Preload","2",1,Text("d0010409")),
            (@"Keyboard Layout\Substitutes","d0010409",1,Text("00000809"))]);
        var dll=Path.Combine(mount,@"Windows\System32\kbduk.dll");File.WriteAllBytes(dll,new byte[2048]);
        var beforeSystem=BootableDrive.Hash(system);var beforeUser=BootableDrive.Hash(user);
        var options=new BuildOptions("unused.iso",1,"unused-work","unused-output.iso","unused-tool",[],[],false,false,null){AnimatedWinPe=true};
        var inherited=SetupRegional.FromImage(mount,options);
        check(inherited.UiLanguage=="en-US" && inherited.SystemLocale=="de-DE" && inherited.UserLocale=="fr-CA" && inherited.InputLocale=="040C:0000040C;0409:00000809",
            "regional defaults retain image UI, source formats, multiple keyboards and substitutions from the actual selected control set");
        var chosen=SetupRegional.FromImage(mount,options with{SetupRegion="en-GB",SetupKeyboard="00000809"});
        check(chosen.UiLanguage=="en-US" && chosen.SystemLocale=="en-GB" && chosen.UserLocale=="en-GB" && chosen.InputLocale=="0809:00000809",
            "UK region and keyboard choices retain the installed English US display language");
        check(BootableDrive.Hash(system)==beforeSystem && BootableDrive.Hash(user)==beforeUser,"reading setup defaults never modifies either offline source hive");
        var ns=SetupAnswerFile.Namespace;
        var template=SetupAnswerFile.Template(true,regional:chosen);
        var document=XDocument.Parse(template);
        var intl=document.Descendants(ns+"component").Single(c=>(string?)c.Attribute("name")=="Microsoft-Windows-International-Core");
        check(intl.Element(ns+"UILanguage")!.Value=="en-US" && intl.Element(ns+"UserLocale")!.Value=="en-GB" && intl.Element(ns+"SystemLocale")!.Value=="en-GB" && intl.Element(ns+"InputLocale")!.Value=="0809:00000809",
            "actual account template contains all four Microsoft-documented unattended OOBE regional values");
        var pe=document.Root!.Elements(ns+"settings").Single(e=>(string?)e.Attribute("pass")=="windowsPE")
            .Elements(ns+"component").Single(c=>(string?)c.Attribute("name")=="Microsoft-Windows-International-Core-WinPE");
        check(pe.Element(ns+"UILanguage")!.Value=="en-US" && pe.Element(ns+"UserLocale")!.Value=="en-GB" && pe.Element(ns+"SystemLocale")!.Value=="en-GB" && pe.Element(ns+"InputLocale")!.Value=="0809:00000809" &&
            pe.Element(ns+"SetupUILanguage")!.Element(ns+"UILanguage")!.Value=="en-US" && pe.Element(ns+"SetupUILanguage")!.Element(ns+"WillShowUI")==null,
            "UK selections also configure the initial Windows PE language page without requesting unavailable UK display resources");
        var sourceCommand=new XElement(ns+"RunSynchronous",new XElement(ns+"RunSynchronousCommand",new XElement(ns+"Order",1),new XElement(ns+"Path",@"X:\source-tool.exe /keep")));
        pe.Parent!.Add(new XElement(ns+"component",new XAttribute("name","Microsoft-Windows-Setup"),new XAttribute("processorArchitecture","amd64"),sourceCommand));
        var preserved=XDocument.Parse(SetupAnswerFile.WithRegional(document.ToString(),inherited));
        check(XNode.DeepEquals(sourceCommand,preserved.Descendants(ns+"RunSynchronous").Single()) &&
            preserved.Descendants(ns+"SetupUILanguage").Single().Element(ns+"UILanguage")!.Value=="en-US",
            "adding regional defaults retains existing Windows PE setup commands and the source Setup display language");
        check(document.Descendants(ns+"AutoLogon").Single().Element(ns+"Enabled")!.Value=="true" && document.Descendants(ns+"ComputerName").Single().Value=="TLOS-PC-7D91",
            "regional setup retains automatic sign-in and the per-install PC-name marker");
        var merged=SetupAnswerFile.WithRegional(template,inherited);
        check(XDocument.Parse(merged).Descendants(ns+"UserLocale").All(e=>e.Value=="en-GB"),"keeping image defaults preserves an already explicit source answer-file regional choice");
        var changed=SetupAnswerFile.WithRegional(template,chosen with{UserLocale="de-DE",SystemLocale="de-DE",InputLocale="0407:00000407"});
        check(XDocument.Parse(changed).Descendants(ns+"UserLocale").All(e=>e.Value=="de-DE") && changed.Contains("TLOS_PASSWORD_7D91") && changed.Contains("TLOS_HELLO_7D91"),
            "changing regional choices does not replace account/password/Hello provisioning markers");
        foreach(var bad in new[]{options with{SetupRegion="en"},options with{SetupRegion="zz-ZZ"},options with{SetupKeyboard="../bad"},options with{AnimatedWinPe=false,SetupRegion="en-GB"}})
        {
            try{SetupRegional.ValidateSelections(bad);throw new Exception("Expected invalid regional choice rejection");}
            catch(ArgumentException){check(true,"invalid or inactive-wizard regional choices are rejected before servicing");}
        }
        var utility=Path.Combine(mount,@"Windows\System32\wpeutil.exe");File.WriteAllBytes(utility,new byte[2048]);
        check(WinPeBackground.KeyboardForImage(mount,chosen)=="0809:00000809","WinPE accepts a selected keyboard only when its own registry and DLL support it");
        var shell=System.Text.Encoding.ASCII.GetBytes("[LaunchApps]\r\n%SYSTEMROOT%\\System32\\LaunchBar_x64.exe\r\n");
        var launch=System.Text.Encoding.ASCII.GetString(WinPeBackground.AddLaunch(shell,"0809:00000809"));
        check(launch=="[LaunchApps]\r\n%SYSTEMROOT%\\System32\\wpeutil.exe, SetKeyboardLayout 0809:00000809\r\n"+WinPeBackground.LaunchLine+"\r\n","selected WinPE keyboard is applied before the account wizard starts");
        check(WinPeBackground.KeyboardForImage(mount,inherited)==null,"keeping ISO keyboard defaults does not change WinPE input");
        File.Delete(dll);
        try{WinPeBackground.KeyboardForImage(mount,chosen);throw new Exception("Expected missing WinPE keyboard rejection");}
        catch(IOException){check(true,"a stripped WinPE keyboard stops preparation instead of risking a password layout mismatch");}
        try{SetupRegional.FromImage(mount,options with{SetupKeyboard="00000809"});throw new Exception("Expected missing keyboard file rejection");}
        catch(IOException){check(true,"a stripped keyboard payload cannot be selected merely because the host lists its name");}
        var duplicate=template.Replace("</InputLocale>","</InputLocale><InputLocale>en-US</InputLocale>",StringComparison.Ordinal);
        try{SetupAnswerFile.WithRegional(duplicate,chosen);throw new Exception("Expected duplicate field rejection");}
        catch(IOException){check(true,"ambiguous source regional answer settings stop the private transaction");}
        check(SetupRegional.Keyboards().Any(k=>k.Id=="00000809") && SetupRegional.Regions().Any(r=>r.Id=="en-GB"),"Builder selectors include actual Windows UK keyboard metadata and regional formats");
    }
}
