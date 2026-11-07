using System;
using System.Text;

internal static class ProcessOutputFixture
{
    private static int Main(string[] args)
    {
        var encoding = args[0] == "utf8" ? new UTF8Encoding(false) : Encoding.GetEncoding(437);
        var text = args[0] == "utf8" ? "HWiNFO\u00ae O'Brien \u6d4b\u8bd5 \u00e9" : "Native \u00e9";
        var bytes = encoding.GetBytes(text + Environment.NewLine);
        Console.OpenStandardOutput().Write(bytes, 0, bytes.Length);
        Console.OpenStandardError().Write(bytes, 0, bytes.Length);
        return args[1] == "error" ? 7 : 0;
    }
}
