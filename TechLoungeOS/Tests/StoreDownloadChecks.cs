using System.Xml;
using TechLoungeOS;

static class StoreDownloadChecks
{
    public static void Run(Action<bool,string> check)
    {
        var hash=new string('A',64);
        check(StorePackageDownload.ValidArchive(new Uri("https://sfdataservice.microsoft.com/packages/dependency"),123,null)
            && StorePackageDownload.ValidArchive(new Uri("http://tlu.dl.delivery.mp.microsoft.com/package"),123,hash),
            "Store dependencies require Microsoft HTTPS; original HTTP CDN archives require a catalogue SHA-256");
        foreach(var url in new[]{"http://sfdataservice.microsoft.com/package","https://evil.example/package","https://sfdataservice.microsoft.com.evil.example/a","https://user@sfdataservice.microsoft.com/a","https://sfdataservice.microsoft.com:444/a","file:///C:/payload","https://sfdataservice.microsoft.com/a#fragment"})
            check(!StorePackageDownload.ValidArchive(new Uri(url),123,hash),"Store acquisition rejects untrusted archive location: "+url);
        check(!StorePackageDownload.ValidArchive(new Uri("http://tlu.dl.delivery.mp.microsoft.com/a"),123,null)
            && !StorePackageDownload.ValidArchive(new Uri("https://sfdataservice.microsoft.com/a"),0,hash)
            && !StorePackageDownload.ValidArchive(new Uri("https://sfdataservice.microsoft.com/a"),123,"123"),
            "Store acquisition rejects unhashed HTTP, empty payloads and malformed digests");
        var xml="""
            <Result><NewUpdates><UpdateInfo><ID>42</ID><Xml><UpdateIdentity UpdateID="00000000-0000-0000-0000-000000000042" RevisionNumber="7"/></Xml></UpdateInfo></NewUpdates>
            <Updates><Update><ID>42</ID><Xml>&lt;Files&gt;
            &lt;File InstallerSpecificIdentifier="Microsoft.StorePurchaseApp_1.0.0.0_neutral_~_8wekyb3d8bbwe" FileName="original.msixbundle" Size="123" Digest="archive-digest"&gt;&lt;AdditionalDigest Algorithm="SHA256"&gt;AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=&lt;/AdditionalDigest&gt;&lt;/File&gt;
            &lt;File InstallerSpecificIdentifier="Microsoft.VCLibs.140.00_14.0.0.0_x64__8wekyb3d8bbwe" FileName="framework.appx" Size="456" Digest="framework-digest"&gt;&lt;AdditionalDigest Algorithm="SHA256"&gt;AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=&lt;/AdditionalDigest&gt;&lt;/File&gt;
            &lt;File InstallerSpecificIdentifier="Microsoft.StorePurchaseApp_9.0.0.0_neutral_~_8wekyb3d8bbwe" FileName="encrypted.eappxbundle" Size="123"/&gt;
            &lt;/Files&gt;</Xml></Update></Updates></Result>
            """;
        var document=StorePackageDownload.Xml(xml);
        var main=StorePackageDownload.SelectBundle(document,"Microsoft.StorePurchaseApp");
        check(main.FileName=="original.msixbundle" && main.Size==123 && main.Revision=="7" && main.Sha256==new string('0',64)
            && main.UpdateId=="00000000-0000-0000-0000-000000000042" && main.Digest=="archive-digest",
            "Store Purchase parser joins update identities and decodes escaped file metadata without choosing an encrypted archive");
        var dependencies=StorePackageDownload.CatalogueDependencies(document);
        check(dependencies.Length==1 && dependencies[0].FileName=="framework.appx" && dependencies[0].Digest=="framework-digest",
            "Store Purchase parser resolves the framework's own archive digest");
        bool rejected=false;
        try{StorePackageDownload.Xml("<!DOCTYPE Result [<!ENTITY x SYSTEM 'file:///C:/unread'>]><Result>&x;</Result>");}catch(XmlException){rejected=true;}
        check(rejected,"Store catalogue XML rejects external entities");
    }
}
