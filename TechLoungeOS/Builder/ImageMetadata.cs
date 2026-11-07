using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace TechLoungeOS;

// Query the WIM directory once; Get-WindowsImage -Index reopens it for every edition.
public static class ImageMetadata
{
    public const string QueryScript = """
        Add-Type -TypeDefinition @'
        using System;
        using System.ComponentModel;
        using System.Runtime.InteropServices;
        public static class TlosWimMetadata {
          [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
          [DllImport("wimgapi.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
          static extern IntPtr WIMCreateFile(string path,uint access,uint disposition,uint flags,uint compression,IntPtr result);
          [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
          [DllImport("wimgapi.dll", ExactSpelling=true, SetLastError=true)]
          static extern bool WIMGetImageInformation(IntPtr image,out IntPtr xml,out uint bytes);
          [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
          [DllImport("wimgapi.dll", ExactSpelling=true)] static extern bool WIMCloseHandle(IntPtr handle);
          [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
          [DllImport("kernel32.dll", ExactSpelling=true)] static extern IntPtr LocalFree(IntPtr memory);
          public static string Read(string path) {
            IntPtr handle=WIMCreateFile(path,0,3,0,0,IntPtr.Zero);
            if(handle==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr xml=IntPtr.Zero;
            try {
              uint bytes;
              if(!WIMGetImageInformation(handle,out xml,out bytes)) throw new Win32Exception(Marshal.GetLastWin32Error());
              if(xml==IntPtr.Zero || bytes<2 || bytes>16777216 || bytes%2!=0) throw new InvalidOperationException("Invalid Windows image metadata size.");
              return Marshal.PtrToStringUni(xml,checked((int)bytes/2)).TrimEnd('\0');
            } finally { if(xml!=IntPtr.Zero)LocalFree(xml); WIMCloseHandle(handle); }
          }
        }
        '@
        [TlosWimMetadata]::Read($image)
        """;

    public static Edition[] Parse(string xml)
    {
        using var text=new StringReader(xml.TrimStart('\uFEFF'));
        using var reader=XmlReader.Create(text,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024});
        var root=XDocument.Load(reader).Root;
        if(root?.Name!="WIM")throw new IOException("Windows image metadata has no WIM directory.");
        var editions=root.Elements("IMAGE").Select(image=>{
            var windows=image.Element("WINDOWS")??throw new IOException("Windows edition metadata is missing.");
            var version=windows.Element("VERSION")??throw new IOException("Windows version metadata is missing.");
            int Number(XElement parent,string name)=>int.TryParse(parent.Element(name)?.Value,NumberStyles.None,CultureInfo.InvariantCulture,out var n)&&n>=0?n:throw new IOException("Invalid Windows metadata: "+name);
            if(!int.TryParse(image.Attribute("INDEX")?.Value,out var index)||index<1)throw new IOException("Invalid Windows edition index.");
            var name=image.Element("NAME")?.Value;
            if(string.IsNullOrWhiteSpace(name))throw new IOException("Windows edition name is missing.");
            return new Edition(index,name,Number(windows,"ARCH").ToString(CultureInfo.InvariantCulture),new Version(Number(version,"MAJOR"),Number(version,"MINOR"),Number(version,"BUILD"),Number(version,"SPBUILD")).ToString(),windows.Element("EDITIONID")?.Value);
        }).ToArray();
        if(editions.Length==0 || editions.Select(e=>e.ImageIndex).Distinct().Count()!=editions.Length)throw new IOException("Windows image has no unique edition list.");
        return editions;
    }
}
