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
        using var wim = WIMCreateFile(path, 0x80000000, 3, 0, 0, out _);
        if (wim.IsInvalid) throw NativeFailure();
        if (!WIMGetAttributes(wim, out var info, (uint)Marshal.SizeOf<WimInfo>())) throw NativeFailure();
        if (!WIMGetImageInformation(wim, out var data, out var size)) throw NativeFailure();
        try
        {
            if (data == 0 || size < 2 || size > MaximumXml || size % 2 != 0)
                throw new IOException("Windows Setup image metadata has an unsupported size or encoding.");
            var bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            var xml = new UnicodeEncoding(false, true, true).GetString(bytes).TrimStart('\uFEFF').TrimEnd('\0');
            return SelectBootIndex(info.ImageCount, info.BootIndex, info.PartNumber, info.TotalParts, xml);
        }
        finally { if (data != 0) _ = LocalFree(data); }
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
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nint LocalFree(nint memory);
}
