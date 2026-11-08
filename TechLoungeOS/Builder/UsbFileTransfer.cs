using System.Buffers;
using System.Security.Cryptography;

namespace TechLoungeOS;

public record UsbTransferProgress(bool Verifying,long CompletedBytes,long TotalBytes)
{
    public double Percent=>TotalBytes<=0?0:Math.Clamp(100d*CompletedBytes/TotalBytes,0,100);
    public string Caption=>$"{(Verifying?"Checked":"Copied")} {CompletedBytes/1_000_000_000d:0.00} of {TotalBytes/1_000_000_000d:0.00} GB · {Percent:0}%";
}

internal static class UsbFileTransfer
{
    private const int BufferSize=1<<20;
    internal static void Copy(string source,string destination,long expectedBytes,CancellationToken ct,Action<long> progress)
    {
        ct.ThrowIfCancellationRequested();
        var written=File.GetLastWriteTimeUtc(source);
        using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,1,FileOptions.SequentialScan))
        {
            if(input.Length!=expectedBytes)throw new IOException("A prepared file changed before copying. Recreate the USB.");
            using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.SequentialScan);
            var buffer=ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                long copied=0;int count;
                while((count=input.Read(buffer,0,BufferSize))!=0)
                {
                    ct.ThrowIfCancellationRequested();output.Write(buffer,0,count);copied+=count;progress(copied);
                }
                if(copied!=expectedBytes)throw new IOException("A prepared file changed while copying. Recreate the USB.");
            }
            finally{ArrayPool<byte>.Shared.Return(buffer);}
        }
        File.SetLastWriteTimeUtc(destination,written);
    }
    internal static string Hash(string file,CancellationToken ct,Action<long> progress)
    {
        ct.ThrowIfCancellationRequested();
        using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,1,FileOptions.SequentialScan);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long checkedBytes=0;int count;
            while((count=input.Read(buffer,0,BufferSize))!=0)
            {
                ct.ThrowIfCancellationRequested();hash.AppendData(buffer,0,count);checkedBytes+=count;progress(checkedBytes);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally{ArrayPool<byte>.Shared.Return(buffer);}
    }
}
