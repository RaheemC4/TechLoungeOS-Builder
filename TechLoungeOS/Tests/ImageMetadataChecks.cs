using System.Xml;
using TechLoungeOS;

internal static class ImageMetadataChecks
{
    internal static void Run(Action<bool,string> check)
    {
        const string image="<IMAGE INDEX='3'><NAME>Édition &amp; tools</NAME><WINDOWS><ARCH>9</ARCH><EDITIONID>IoTEnterpriseS</EDITIONID><VERSION><MAJOR>10</MAJOR><MINOR>0</MINOR><BUILD>26300</BUILD><SPBUILD>9457</SPBUILD></VERSION></WINDOWS></IMAGE>";
        var editions=ImageMetadata.Parse("\uFEFF<WIM>"+image+image.Replace("INDEX='3'","INDEX='8'").Replace("<ARCH>9</ARCH>","<ARCH>12</ARCH>")+"</WIM>");
        check(editions.Length==2 && editions[0]==new Edition(3,"Édition & tools","9","10.0.26300.9457","IoTEnterpriseS"),"Single metadata read preserves non-contiguous index, Unicode name, exact version and LTSC edition identity");
        check(editions[1].ImageIndex==8 && editions[1].Architecture=="12","ARM image metadata remains ARM rather than becoming x64");
        foreach(var invalid in new[]{"<WIM/>","<OTHER/>","<WIM>"+image+image+"</WIM>","<WIM>"+image.Replace("<ARCH>9</ARCH>","")+"</WIM>","<WIM>"+image.Replace("<SPBUILD>9457</SPBUILD>","<SPBUILD>oops</SPBUILD>")+"</WIM>","<!DOCTYPE WIM [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><WIM>&x;</WIM>"})
        {
            try{ImageMetadata.Parse(invalid);check(false,"Malformed or ambiguous metadata is rejected");}
            catch(Exception ex)when(ex is IOException or XmlException){check(true,"Malformed or ambiguous metadata is rejected");}
        }
    }
}
