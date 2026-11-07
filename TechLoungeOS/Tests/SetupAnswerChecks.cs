using System.Text;
using System.Xml;
using System.Xml.Linq;
using TechLoungeOS;

static class SetupAnswerChecks
{
    public static void Run(Action<bool,string> check)
    {
        var ns=SetupAnswerFile.Namespace;
        var account=new SetupAnswerFile.Account("Raheem & Test","WORK-PC-01","",false);
        var answer=SetupAnswerFile.Create(account);
        var roundTrip=XDocument.Parse(answer.ToString());
        var local=roundTrip.Descendants(ns+"LocalAccount").Single();
        check(local.Element(ns+"Name")!.Value=="Raheem & Test" && local.Element(ns+"Group")!.Value=="Users","setup account values round-trip as XML text and retain the chosen standard-user permission");
        var password=local.Element(ns+"Password")!;
        check(password.Element(ns+"PlainText")!.Value=="false" && Encoding.Unicode.GetString(Convert.FromBase64String(password.Element(ns+"Value")!.Value))=="Password","a blank optional password is encoded as the actual empty Windows password, not a generated password");
        check(roundTrip.Descendants(ns+"ComputerName").Single().Value=="WORK-PC-01" && !roundTrip.Descendants(ns+"AutoLogon").Any() && !roundTrip.Descendants(ns+"DiskConfiguration").Any(),"account preparation changes neither automatic sign-in nor disk partitioning implicitly");
        check(roundTrip.Descendants(ns+"HideOnlineAccountScreens").Single().Value=="true" && roundTrip.Descendants(ns+"ProtectYourPC").Single().Value=="3","answer file selects local-account OOBE and declines Express settings");
        var protectedPassword=SetupAnswerFile.Create(account with{Password="fixture<&\"'password",Administrator=true});
        check(!(account with{Password="sensitive-fixture"}).ToString().Contains("sensitive-fixture"),"account diagnostics redact the optional password");
        var savedPassword=protectedPassword.Descendants(ns+"Password").Single();
        check(Encoding.Unicode.GetString(Convert.FromBase64String(savedPassword.Element(ns+"Value")!.Value))=="fixture<&\"'passwordPassword" && protectedPassword.Descendants(ns+"Group").Single().Value=="Administrators","explicit password and administrator choices survive answer-file serialization; encoding is not encryption");
        foreach(var user in new[]{"Administrator","guest","DefaultUser0","SYSTEM","bad/name","name."," name","a\nname",new string('x',21),"..."})
        {
            try{SetupAnswerFile.Create(account with{UserName=user});throw new Exception("Expected invalid username rejection");}
            catch(ArgumentException){check(true,"setup rejects reserved/invalid username: "+user.Replace('\n',' '));}
        }
        check(SetupAnswerFile.ComputerName("Raheem",null)=="RaheemPC" && SetupAnswerFile.ComputerName("user","")=="UserPC","an optional PC name defaults to the capitalised username plus PC");
        check(SetupAnswerFile.Create(account with{UserName="user",ComputerName=""}).Descendants(ns+"ComputerName").Single().Value=="UserPC","blank PC-name choice is resolved into the Windows specialize answer file");
        check(SetupAnswerFile.ComputerName("verylongusernametest",null).Length==15 && SetupAnswerFile.ComputerName("张伟",null)=="UserPC" && SetupAnswerFile.ComputerName("123",null)=="123PC","automatic PC names handle length, Unicode-only usernames and numeric usernames");
        foreach(var pc in new[]{"12345","1-2","-PC","PC-","PC NAME",new string('x',16)})
        {
            try{SetupAnswerFile.Create(account with{ComputerName=pc});throw new Exception("Expected invalid PC name rejection");}
            catch(ArgumentException){check(true,"setup rejects invalid PC name: "+pc);}
        }
        var source=$"<unattend xmlns='{ns}'><settings pass='specialize'><component name='Vendor-Setup'><Preserve>original</Preserve></component><component name='Microsoft-Windows-Shell-Setup' processorArchitecture='amd64'><ComputerName>*</ComputerName><TimeZone>GMT Standard Time</TimeZone></component></settings><settings pass='windowsPE'><DiskConfiguration>source-specific</DiskConfiguration></settings></unattend>";
        var merged=SetupAnswerFile.Create(account,source);
        check(merged.Descendants(ns+"Preserve").Single().Value=="original" && merged.Descendants(ns+"TimeZone").Single().Value=="GMT Standard Time" && merged.Descendants(ns+"DiskConfiguration").Single().Value=="source-specific","source setup extensions, time zone and existing disk choices are retained during the narrow merge");
        var inheritedAccount=$"<unattend xmlns='{ns}'><settings pass='oobeSystem'><component name='Microsoft-Windows-Shell-Setup' processorArchitecture='amd64'><UserAccounts><AdministratorPassword><Value>source-value</Value></AdministratorPassword></UserAccounts><AutoLogon><Username>Administrator</Username></AutoLogon><FirstLogonCommands><SynchronousCommand><Order>1</Order><CommandLine>original-tool.exe /preserve</CommandLine></SynchronousCommand></FirstLogonCommands><TimeZone>GMT Standard Time</TimeZone></component></settings></unattend>";
        var template=XDocument.Parse(SetupAnswerFile.Template(true,inheritedAccount));
        check(template.Descendants(ns+"CommandLine").First().Value=="original-tool.exe /preserve" && template.Descendants(ns+"TimeZone").Single().Value=="GMT Standard Time" && !template.ToString().Contains("source-value"),"explicit wizard account selection replaces inherited credentials but preserves source commands and unrelated settings");
        check(template.Descendants(ns+"SynchronousCommand").Select(c=>c.Element(ns+"Order")!.Value).SequenceEqual(new[]{"1","2"}) && template.Descendants(ns+"LogonCount").Single().Value=="2","wizard adds a distinct first-sign-in command without overwriting an inherited command");
        check(template.Descendants(ns+"Password").Select(p=>p.Element(ns+"Value")!.Value).SequenceEqual(new[]{"TLOS_PASSWORD_7D91","TLOS_AUTO_PASSWORD_7D91"}),"ISO templates contain inert credential markers, never a supplied or generated password");
        var manual=XDocument.Parse(SetupAnswerFile.Template(false));
        check(manual.Descendants(ns+"AutoLogon").Single().Element(ns+"Enabled")!.Value=="false" && !manual.ToString().Contains("TLOS_AUTO_PASSWORD_7D91"),"manual sign-in explicitly disables inherited automatic sign-in and has no auto-sign-in password marker");
        foreach(var automatic in new[]{false,true})
        {
            var setupTemplate=XDocument.Parse(SetupAnswerFile.Template(automatic));
            var setup=setupTemplate.Elements(ns+"unattend").Elements(ns+"settings").Single(s=>(string?)s.Attribute("pass")=="windowsPE").Elements(ns+"component").Single(c=>(string?)c.Attribute("name")=="Microsoft-Windows-Setup");
            var key=setup.Element(ns+"UserData")!.Element(ns+"ProductKey")!;
            check(key.Element(ns+"WillShowUI")!.Value=="Always" && !key.Elements(ns+"Key").Any(),"account wizard without a source key retains Windows Setup's key/skip screen without supplying a licence or changing edition");
        }
        var licensing=$"<unattend xmlns='{ns}'><settings pass='windowsPE'><component name='Microsoft-Windows-Setup' processorArchitecture='amd64'><UserData><AcceptEula>true</AcceptEula><ProductKey><Key>AAAAA-BBBBB-CCCCC-DDDDD-EEEEE</Key><WillShowUI>Never</WillShowUI></ProductKey></UserData><ImageInstall><OSImage><InstallFrom><MetaData><Key>/IMAGE/NAME</Key><Value>Vendor edition</Value></MetaData></InstallFrom></OSImage></ImageInstall></component></settings></unattend>";
        var licensed=XDocument.Parse(SetupAnswerFile.Template(true,licensing));
        check(licensed.Descendants(ns+"ProductKey").Single().ToString()==XDocument.Parse(licensing).Descendants(ns+"ProductKey").Single().ToString() && licensed.Descendants(ns+"AcceptEula").Single().Value=="true" && licensed.Descendants(ns+"ImageInstall").Single().ToString()==XDocument.Parse(licensing).Descendants(ns+"ImageInstall").Single().ToString(),"wizard preserves supplied installation-key policy, licence agreement choice and selected source edition exactly");
        foreach(var conflicting in new[]{"<UserData/><UserData/>","<UserData><ProductKey/><ProductKey/></UserData>"})
        {
            var conflict=$"<unattend xmlns='{ns}'><settings pass='windowsPE'><component name='Microsoft-Windows-Setup' processorArchitecture='amd64'>{conflicting}</component></settings></unattend>";
            try{SetupAnswerFile.Template(true,conflict);throw new Exception("Expected ambiguous licensing rejection");}
            catch(IOException){check(true,"wizard rejects ambiguous source licensing rather than choosing or replacing a key");}
        }
        var rendered=SetupAnswerFile.Template(true).Replace("TLOS_USER_7D91","Raheem &amp; Test").Replace("TLOS-PC-7D91","RaheemTestPC").Replace("TLOS_PASSWORD_7D91","UABhAHMAcwB3AG8AcgBkAA==").Replace("TLOS_AUTO_PASSWORD_7D91","UABhAHMAcwB3AG8AcgBkAA==").Replace("TLOS_HELLO_7D91","0");
        var deployment=XDocument.Parse(rendered);
        check(deployment.Descendants(ns+"CommandLine").Single().Value=="\"%ProgramData%\\TechLoungeOS\\AccountSetup.exe\" --user \"Raheem & Test\" --automatic 1 --hello 0" && deployment.Descendants(ns+"Name").Single().Value=="Raheem & Test","rendered XML and quoted helper arguments preserve a username containing spaces and an ampersand");
        foreach(var conflict in new[]{"<UserAccounts/>","<AutoLogon><Enabled>true</Enabled></AutoLogon>","<OOBE/><OOBE/>"})
        {
            var inherited=$"<unattend xmlns='{ns}'><settings pass='oobeSystem'><component name='Microsoft-Windows-Shell-Setup' processorArchitecture='amd64'>{conflict}</component></settings></unattend>";
            try{SetupAnswerFile.Create(account,inherited);throw new Exception("Expected source conflict rejection");}
            catch(IOException){check(true,"existing account/sign-in or ambiguous OOBE requires review instead of overwriting source settings");}
        }
        try{SetupAnswerFile.Create(account,"<!DOCTYPE unattend [<!ENTITY a SYSTEM 'file:///c:/fixture'>]><unattend>&a;</unattend>");throw new Exception("Expected DTD rejection");}
        catch(XmlException){check(true,"setup answer parser rejects DTDs and external entities");}
    }
}
