using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;

namespace TechLoungeOS;

// Read the bootable index and its architecture without mounting or changing the WIM.
internal static class WinPeImage
{
    private const int MaximumXml = 2 * 1024 * 1024;

    internal static int ReadBootIndex(string path)
    {
        Policy.RequireUnlinkedPath(path);
        SourcePreservation.RequireImageFile(path, 1, long.MaxValue);
        var data=ReadMetadata(path);return SelectBootIndex(data.Info.ImageCount,data.Info.BootIndex,data.Info.PartNumber,data.Info.TotalParts,data.Xml);
    }

    private static (WimInfo Info,string Xml) ReadMetadata(string path)
    {
        using var wim=WIMCreateFile(path,0x80000000,3,0,0,out _);
        if(wim.IsInvalid)throw NativeFailure();
        if(!WIMGetAttributes(wim,out var info,(uint)Marshal.SizeOf<WimInfo>()))throw NativeFailure();
        return (info,ReadXml(wim));
    }
    private static string ReadXml(WimHandle wim)
    {
        if(!WIMGetImageInformation(wim,out var data,out var size))throw NativeFailure();
        try
        {
            if(data==0||size<2||size>MaximumXml||size%2!=0)throw new IOException("Windows Setup image metadata has an unsupported size or encoding.");
            var bytes=new byte[size];Marshal.Copy(data,bytes,0,bytes.Length);
            return new UnicodeEncoding(false,true,true).GetString(bytes).TrimStart('\uFEFF').TrimEnd('\0');
        }
        finally{if(data!=0)_=LocalFree(data);}
    }
    private static XElement Document(string xml)
    {
        using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaximumXml});
        var root=XDocument.Load(reader,LoadOptions.PreserveWhitespace).Root;
        return root?.Name=="WIM"?root:throw new IOException("Windows Setup WIM metadata is missing.");
    }
    internal static string RetainWindowsMetadata(uint count,uint index,string original,string saved)
    {
        _=SelectBootIndex(count,index,1,1,original);
        var source=Document(original);var candidate=Document(saved);
        var before=source.Elements("IMAGE").Single(i=>(string?)i.Attribute("INDEX")==index.ToString(CultureInfo.InvariantCulture)).Element("WINDOWS")!;
        var selected=candidate.Elements("IMAGE").SingleOrDefault(i=>(string?)i.Attribute("INDEX")==index.ToString(CultureInfo.InvariantCulture))??throw new IOException("Saved WinPE image index is missing.");
        var after=selected.Element("WINDOWS");
        if((string?)after?.Element("ARCH")!="9" || after.Element("INSTALLATIONTYPE") is { } type&&!type.Value.Equals("WindowsPE",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Saved Windows PE architecture or image type changed.");
        // Only the shell and our additional files were changed and verified before commit.
        // DISM may rebuild descriptive OS fields from a stripped image's incomplete registry.
        // Keep the original WINDOWS description; retain new counts, sizes and timestamps.
        after.ReplaceWith(new XElement(before));
        var result=candidate.ToString(SaveOptions.DisableFormatting);
        _=SelectBootIndex(count,index,1,1,result);return result;
    }
    internal static void PreserveWindowsMetadata(string source,string candidate)
    {
        Policy.RequireUnlinkedPath(source);Policy.RequireUnlinkedPath(candidate);
        if(Path.GetFullPath(source).Equals(Path.GetFullPath(candidate),StringComparison.OrdinalIgnoreCase))throw new IOException("A separate WinPE staging image is required.");
        var original=ReadMetadata(source);_=SelectBootIndex(original.Info.ImageCount,original.Info.BootIndex,original.Info.PartNumber,original.Info.TotalParts,original.Xml);
        using(var wim=WIMCreateFile(candidate,0xC0000000,3,0,0,out _))
        {
            if(wim.IsInvalid||!WIMGetAttributes(wim,out var current,(uint)Marshal.SizeOf<WimInfo>()))throw NativeFailure();
            if(current.ImageCount!=original.Info.ImageCount||current.BootIndex!=original.Info.BootIndex||current.PartNumber!=1||current.TotalParts!=1)throw new IOException("Saved WinPE boot selection changed.");
            var saved=ReadXml(wim);var retained=RetainWindowsMetadata(current.ImageCount,current.BootIndex,original.Xml,saved);
            if(!XNode.DeepEquals(Document(retained),Document(saved)))
            {
                var scratch=Path.Combine(Path.GetDirectoryName(candidate)!,"pe-metadata-scratch");Policy.RequireUnlinkedPath(scratch);Directory.CreateDirectory(scratch);
                if(!WIMSetTemporaryPath(wim,scratch))throw NativeFailure();
                using var image=WIMLoadImage(wim,current.BootIndex);if(image.IsInvalid)throw NativeFailure();
                var selected=Document(retained).Elements("IMAGE").Single(i=>(string?)i.Attribute("INDEX")==current.BootIndex.ToString(CultureInfo.InvariantCulture));
                var bytes=Encoding.Unicode.GetBytes("\uFEFF"+selected.ToString(SaveOptions.DisableFormatting)+"\0");
                if(!WIMSetImageInformation(image,bytes,(uint)bytes.Length))throw NativeFailure();
            }
        }
        var verified=ReadMetadata(candidate);var expected=RetainWindowsMetadata(verified.Info.ImageCount,verified.Info.BootIndex,original.Xml,verified.Xml);
        if(!XNode.DeepEquals(Document(expected),Document(verified.Xml)))throw new IOException("Saved WinPE description did not retain the source settings.");
    }

    internal static int SelectBootIndex(uint count, uint index, ushort part, ushort parts, string xml)
    {
        if (count is 0 or > 64 || index == 0 || index > count || part != 1 || parts != 1)
            throw new IOException("Windows Setup needs one complete WIM with an explicitly marked boot image.");
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumXml });
            var root = XDocument.Load(reader).Root;
            if (root?.Name != "WIM") throw new IOException("Windows Setup WIM metadata is missing.");
            var images = new Dictionary<uint, XElement>();
            foreach (var image in root.Elements("IMAGE"))
                if (!uint.TryParse((string?)image.Attribute("INDEX"), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    || number == 0 || number > count || !images.TryAdd(number, image))
                    throw new IOException("Windows Setup image indexes are inconsistent or ambiguous.");
            if (images.Count != count || !images.TryGetValue(index, out var selected))
                throw new IOException("Windows Setup boot-image metadata does not match its WIM header.");
            var windows = selected.Element("WINDOWS");
            if ((string?)windows?.Element("ARCH") != "9" ||
                !string.Equals((string?)windows?.Element("INSTALLATIONTYPE"), "WindowsPE", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The boot image must be a supported 64-bit Windows PE setup environment.");
            return checked((int)index);
        }
        catch (XmlException ex) { throw new IOException("Windows Setup image metadata could not be read safely.", ex); }
    }

    private static IOException NativeFailure() => new("Windows Setup boot-image metadata cannot be read.", new Win32Exception(Marshal.GetLastWin32Error()));

    // Microsoft WIM_INFO: WCHAR[260], GUID, DWORDs, USHORT part fields, then DWORDs.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WimInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? WimPath;
        public Guid Guid;
        public uint ImageCount, CompressionType;
        public ushort PartNumber, TotalParts;
        public uint BootIndex, WimAttributes, WimFlagsAndAttr;
    }
    private sealed class WimHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public WimHandle() : base(true) { }
        protected override bool ReleaseHandle() => WIMCloseHandle(handle);
    }
    [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern WimHandle WIMCreateFile(string path, uint access, uint disposition, uint flags, uint compression, out uint creation);
    [DllImport("wimgapi.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WIMGetAttributes(WimHandle wim, out WimInfo info, uint size);
    [DllImport("wimgapi.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WIMGetImageInformation(WimHandle wim, out nint data, out uint size);
    [DllImport("wimgapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WIMCloseHandle(nint wim);
    [DllImport("wimgapi.dll",ExactSpelling=true,SetLastError=true,CharSet=CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool WIMSetTemporaryPath(WimHandle wim,string path);
    [DllImport("wimgapi.dll",ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern WimHandle WIMLoadImage(WimHandle wim,uint index);
    [DllImport("wimgapi.dll",ExactSpelling=true,SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool WIMSetImageInformation(WimHandle wim,byte[] xml,uint size);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nint LocalFree(nint memory);
}
