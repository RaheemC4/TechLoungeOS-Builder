using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

static class WimMetadata
{
    [DllImport("wimgapi.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint WIMCreateFile(string path, uint access, uint disposition, uint flags, uint compression, out uint result);
    [DllImport("wimgapi.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool WIMSetTemporaryPath(nint wim, string path);
    [DllImport("wimgapi.dll", ExactSpelling=true, SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint WIMLoadImage(nint wim, uint index);
    [DllImport("wimgapi.dll", ExactSpelling=true, SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint WIMGetImageCount(nint wim);
    [DllImport("wimgapi.dll", ExactSpelling=true, SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool WIMGetImageInformation(nint image, out nint data, out uint size);
    [DllImport("wimgapi.dll", ExactSpelling=true, SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool WIMSetImageInformation(nint image, nint data, uint size);
    [DllImport("wimgapi.dll", ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool WIMCloseHandle(nint handle);
    [DllImport("kernel32.dll", ExactSpelling=true)]
    private static extern nint LocalFree(nint data);

    private static readonly string[] Identity = ["WINDOWS", "NAME", "DESCRIPTION", "FLAGS", "DISPLAYNAME", "DISPLAYDESCRIPTION"];
    internal static XElement CopyIdentity(XElement source, XElement captured)
    {
        if (source.Name != "IMAGE" || captured.Name != "IMAGE" || source.Element("WINDOWS") == null ||
            source.Element("WINDOWS")?.Element("EDITIONID") == null || source.Element("WINDOWS")?.Element("VERSION")?.Element("BUILD") == null ||
            (string?)source.Element("WINDOWS")?.Element("ARCH") != "9" || source.Element("NAME") == null ||
            (string?)source.Element("WINDOWS")?.Element("ARCH") != (string?)captured.Element("WINDOWS")?.Element("ARCH"))
            throw new IOException("Source and captured image identity cannot be safely matched.");
        var result = new XElement(captured);
        foreach (var name in Identity)
        {
            result.Elements(name).Remove();
            foreach (var field in source.Elements(name)) result.Add(new XElement(field));
        }
        return result;
    }

    internal static void Restore(string source, string captured, string scratch)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(captured), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source and captured WIM must be separate files.");
        Directory.CreateDirectory(scratch);
        var original = Access(source, scratch, null);
        var before = Access(captured, scratch, null);
        var restored = CopyIdentity(original, before);
        _ = Access(captured, scratch, restored);
        var after = Access(captured, scratch, null);
        foreach (var name in Identity)
            if (!XNode.DeepEquals(restored.Element(name), after.Element(name))) throw new IOException("WIM identity did not persist: " + name);
        foreach (var name in new[] { "DIRCOUNT", "FILECOUNT", "TOTALBYTES", "HARDLINKBYTES" })
            if (!XNode.DeepEquals(before.Element(name), after.Element(name))) throw new IOException("Capture resource counts changed: " + name);
    }

    private static XElement Access(string path, string scratch, XElement? update)
    {
        var wim = WIMCreateFile(Path.GetFullPath(path), update == null ? 0x80000000u : 0xC0000000u, 3, 0, 0, out _);
        if (wim == 0 || wim == -1) throw new IOException("Cannot open WIM metadata.", new Win32Exception(Marshal.GetLastWin32Error()));
        try
        {
            Check(WIMSetTemporaryPath(wim, Path.GetFullPath(scratch)));
            if (WIMGetImageCount(wim) != 1) throw new IOException("Metadata restoration requires a single selected image.");
            var image = WIMLoadImage(wim, 1);
            if (image == 0 || image == -1) throw new IOException("Cannot load WIM image metadata.", new Win32Exception(Marshal.GetLastWin32Error()));
            try
            {
                if (update != null)
                {
                    var bytes = Encoding.Unicode.GetBytes("\uFEFF" + update.ToString(SaveOptions.DisableFormatting) + "\0");
                    var buffer = Marshal.AllocHGlobal(bytes.Length);
                    try { Marshal.Copy(bytes, 0, buffer, bytes.Length); Check(WIMSetImageInformation(image, buffer, (uint)bytes.Length)); }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                Check(WIMGetImageInformation(image, out var data, out var size));
                try
                {
                    if (size is 0 or > 4194304) throw new IOException("WIM metadata size exceeds inspection limits.");
                    var xml = Marshal.PtrToStringUni(data, (int)(size / 2))!.TrimStart('\uFEFF').TrimEnd('\0');
                    using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4194304 });
                    return XElement.Load(reader);
                }
                finally { _ = LocalFree(data); }
            }
            finally { _ = WIMCloseHandle(image); }
        }
        finally { _ = WIMCloseHandle(wim); }
    }
    private static void Check(bool success)
    {
        if (!success) throw new IOException("Windows imaging metadata operation failed.", new Win32Exception(Marshal.GetLastWin32Error()));
    }
}
