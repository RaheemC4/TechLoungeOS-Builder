using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;

namespace TechLoungeOS;

internal static class WimVersionMetadata
{
    internal static XElement WithVersion(XElement image,Version source,Version verified)
    {
        var changed=new XElement(image);
        var windows=changed.Elements("WINDOWS").SingleOrDefault()??throw new IOException("WIM Windows metadata is missing.");
        var version=windows.Elements("VERSION").SingleOrDefault()??throw new IOException("WIM version metadata is missing.");
        int Number(string name)=>int.TryParse((string?)version.Element(name),out var n)?n:throw new IOException("Invalid WIM version field: "+name);
        var existing=new Version(Number("MAJOR"),Number("MINOR"),Number("BUILD"),Number("SPBUILD"));
        if(image.Name!="IMAGE" || (string?)windows.Element("ARCH")!="9" || verified<=source || verified.Major!=10 || verified.Minor!=0 ||
            (existing!=source && existing!=verified))throw new IOException("WIM metadata does not match the selected source or verified serviced version.");
        version.SetElementValue("BUILD",verified.Build);version.SetElementValue("SPBUILD",verified.Revision);
        return changed;
    }
    // Called only on the private, committed single-edition WIM after the offline registry/version checks passed.
    internal static void Update(string path,string scratch,Version source,Version verified)
    {
        Policy.RequireUnlinkedPath(path);SourcePreservation.RequireImageFile(path,1,long.MaxValue);Directory.CreateDirectory(scratch);
        using var wim=WIMCreateFile(Path.GetFullPath(path),0xC0000000,3,0,0,out _);if(wim.IsInvalid)throw Failure();
        Check(WIMSetTemporaryPath(wim,Path.GetFullPath(scratch)));
        if(WIMGetImageCount(wim)!=1)throw new IOException("Version refresh requires the private single-edition WIM.");
        using var image=WIMLoadImage(wim,1);if(image.IsInvalid)throw Failure();
        var before=Read(image);var updated=WithVersion(before,source,verified);
        if(!XNode.DeepEquals(before,updated))
        {
            var bytes=Encoding.Unicode.GetBytes("\uFEFF"+updated.ToString(SaveOptions.DisableFormatting)+"\0");var data=Marshal.AllocHGlobal(bytes.Length);
            try{Marshal.Copy(bytes,0,data,bytes.Length);Check(WIMSetImageInformation(image,data,(uint)bytes.Length));}
            finally{Marshal.FreeHGlobal(data);}
        }
        if(!XNode.DeepEquals(updated,Read(image)))throw new IOException("The verified Windows version did not persist in WIM metadata. No ISO will be exported.");
    }
    private static XElement Read(WimHandle image)
    {
        Check(WIMGetImageInformation(image,out var data,out var size));
        try
        {
            if(data==0 || size is <2 or >4194304 || size%2!=0)throw new IOException("WIM metadata exceeds its supported size.");
            var text=Marshal.PtrToStringUni(data,(int)size/2)!.TrimStart('\uFEFF').TrimEnd('\0');
            using var xml=XmlReader.Create(new StringReader(text),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4194304});
            return XElement.Load(xml);
        }
        finally{if(data!=0)_=LocalFree(data);}
    }
    private static IOException Failure()=>new("Windows imaging version metadata could not be refreshed.",new Win32Exception(Marshal.GetLastWin32Error()));
    private static void Check(bool success){if(!success)throw Failure();}
    private sealed class WimHandle:SafeHandleZeroOrMinusOneIsInvalid
    {
        public WimHandle():base(true){}
        protected override bool ReleaseHandle()=>WIMCloseHandle(handle);
    }
    [DllImport("wimgapi.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern WimHandle WIMCreateFile(string path,uint access,uint disposition,uint flags,uint compression,out uint creation);
    [DllImport("wimgapi.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool WIMSetTemporaryPath(WimHandle wim,string path);
    [DllImport("wimgapi.dll",ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern uint WIMGetImageCount(WimHandle wim);
    [DllImport("wimgapi.dll",ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern WimHandle WIMLoadImage(WimHandle wim,uint index);
    [DllImport("wimgapi.dll",ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool WIMGetImageInformation(WimHandle image,out nint data,out uint size);
    [DllImport("wimgapi.dll",ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool WIMSetImageInformation(WimHandle image,nint data,uint size);
    [DllImport("wimgapi.dll",ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool WIMCloseHandle(nint image);
    [DllImport("kernel32.dll",ExactSpelling=true)]private static extern nint LocalFree(nint data);
}
