using TechLoungeOS;

internal static class PeImageChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        const string image = "<IMAGE INDEX='1'><WINDOWS><ARCH>9</ARCH><INSTALLATIONTYPE>WindowsPE</INSTALLATIONTYPE></WINDOWS></IMAGE>";
        var single = "<WIM>" + image + "</WIM>";
        var dual = "<WIM>" + image + image.Replace("INDEX='1'", "INDEX='2'") + "</WIM>";
        check(WinPeImage.SelectBootIndex(1, 1, 1, 1, single) == 1, "a one-image Windows PE WIM selects its marked first image");
        check(WinPeImage.SelectBootIndex(2, 2, 1, 1, dual) == 2, "standard two-image media selects its marked setup image");
        check(WinPeImage.SelectBootIndex(2, 1, 1, 1, dual) == 1, "an explicitly marked first image is not replaced by an assumed index 2");
        var saved=dual.Replace("<IMAGE INDEX='2'>","<IMAGE INDEX='2'><FILECOUNT>12345</FILECOUNT>");
        var start=saved.LastIndexOf("<WINDOWS>",StringComparison.Ordinal);var stop=saved.IndexOf("</WINDOWS>",start,StringComparison.Ordinal)+10;
        saved=saved[..start]+"<WINDOWS><ARCH>9</ARCH></WINDOWS>"+saved[stop..];
        var retainedXml=WinPeImage.RetainWindowsMetadata(2,2,dual,saved);
        check(WinPeImage.SelectBootIndex(2,2,1,1,retainedXml)==2 && retainedXml.Contains("<FILECOUNT>12345</FILECOUNT>"),"Retaining the original PE description preserves the saved file count and boot index");
        check(System.Xml.Linq.XNode.DeepEquals(System.Xml.Linq.XElement.Parse(retainedXml).Elements("IMAGE").First(),System.Xml.Linq.XElement.Parse(dual).Elements("IMAGE").First()),"Non-boot image metadata is retained unchanged");
        try{WinPeImage.RetainWindowsMetadata(2,2,dual,saved.Replace("<ARCH>9</ARCH></WINDOWS>","<ARCH>12</ARCH></WINDOWS>"));throw new Exception("Changed architecture accepted");}
        catch(IOException){check(true,"Metadata retention never relabels a changed ARM image as x64 PE");}
        void Reject(uint count, uint index, ushort part, ushort parts, string xml, string name)
        {
            try { WinPeImage.SelectBootIndex(count, index, part, parts, xml); throw new Exception("Invalid boot metadata accepted"); }
            catch (IOException) { check(true, name); }
        }
        Reject(2, 0, 1, 1, dual, "an unmarked WIM cannot silently select a different boot image");
        Reject(2, 3, 1, 1, dual, "an out-of-range boot index is rejected");
        Reject(1, 1, 1, 2, single, "split boot images are rejected before mounting");
        Reject(65, 1, 1, 1, single, "pathological image counts are bounded");
        Reject(2, 2, 1, 1, single, "a header/XML count disagreement cannot select an image");
        Reject(2, 1, 1, 1, "<WIM>" + image + image + "</WIM>", "duplicate image indexes are rejected");
        Reject(1, 1, 1, 1, single.Replace("<ARCH>9", "<ARCH>12"), "ARM64 PE is not assigned the x64 native wizard");
        Reject(1, 1, 1, 1, single.Replace("WindowsPE", "Client"), "an installed desktop image is not treated as a setup environment");
        Reject(1, 1, 1, 1, "<!DOCTYPE WIM [<!ENTITY external SYSTEM 'file:///never-open'>]>" + single,
            "boot metadata cannot resolve external entities");
        Reject(1, 1, 1, 1, new string(' ', 2 * 1024 * 1024 + 1) + single, "oversized boot metadata is bounded");
        var invalid = Path.Combine(root, "damaged-boot.wim"); var original = new byte[1024]; File.WriteAllBytes(invalid, original);
        try { WinPeImage.ReadBootIndex(invalid); throw new Exception("Damaged WIM accepted"); }
        catch (IOException)
        {
            using var exclusive = new FileStream(invalid, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var retained = new byte[original.Length]; exclusive.ReadExactly(retained);
            check(retained.SequenceEqual(original), "failed native metadata inspection leaves the fixture unchanged and releases its WIM handle");
        }
    }
}
