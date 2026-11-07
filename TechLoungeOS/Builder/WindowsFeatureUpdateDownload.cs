using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TechLoungeOS;

public static class WindowsFeatureUpdateDownload
{
    public static async Task<WindowsImageUpdates.Plan> Prepare(string folder,Version source,WindowsUpgradeRules.Offer offer,
        Action<string> log,CancellationToken ct,HttpClient? client=null)
    {
        if(!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Windows upgrade downloads require a local private folder.");
        var target=offer.Target.Version;var baseline=offer.Baseline.Version;
        var rule=WindowsUpgradeRules.RequireTransition(offer.RuleId,source,baseline,target);
        if(offer.Target.Name!=rule.Name || Directory.Exists(folder)||File.Exists(folder))throw new IOException("The Windows upgrade requires an exact supported target and a fresh private folder.");
        Policy.RequireUnlinkedPath(folder);Directory.CreateDirectory(folder);
        var packages=new List<WindowsImageUpdates.Payload>();
        if(baseline>source)
        {
            var prerequisite=Path.Combine(folder,"prerequisite");
            var cumulative=await WindowsUpdateDownload.Prepare(prerequisite,source,offer.Baseline,log,ct,client);
            foreach(var package in cumulative.Packages)
            {
                File.Move(Path.Combine(prerequisite,package.File),Path.Combine(folder,package.File));packages.Add(package);
            }
        }
        using var owned=client is null?new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(60)}:null;
        client??=owned!;
        ct.ThrowIfCancellationRequested();log("Downloading the Microsoft Windows "+rule.Name+" enablement package.");
        using var request=new HttpRequestMessage(HttpMethod.Get,rule.Address);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
        if(response.RequestMessage?.RequestUri!=rule.Address || response.Content.Headers.ContentLength!=rule.Bytes)
            throw new IOException("The enablement package address or size differs from the reviewed Microsoft payload.");
        // Small enablement packages have a pinned size (at most 4 MiB); never buffer an unbounded response.
        await using var input=await response.Content.ReadAsStreamAsync(ct);using var bytes=new MemoryStream();var buffer=new byte[16384];int read;
        while((read=await input.ReadAsync(buffer,ct))!=0)
        {if(bytes.Length+read>rule.Bytes)throw new IOException("The enablement response exceeded its reviewed size.");await bytes.WriteAsync(buffer.AsMemory(0,read),ct);}
        if(bytes.Length!=rule.Bytes || Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0,(int)bytes.Length)))!=rule.Sha256)
            throw new IOException("The Windows enablement package checksum differs from the reviewed Microsoft-signed file.");
        var path=Path.Combine(folder,rule.File);
        await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))await output.WriteAsync(bytes.GetBuffer().AsMemory(0,(int)bytes.Length),ct);
        packages.Add(new(rule.File,rule.Bytes,rule.Sha256));
        var plan=new WindowsImageUpdates.Plan(2,source.ToString(),target.ToString(),packages.ToArray(),new(rule.Id,baseline.ToString()));WindowsImageUpdates.ValidatePlan(plan);
        await using(var output=new FileStream(Path.Combine(folder,"update-plan.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await JsonSerializer.SerializeAsync(output,plan,cancellationToken:ct);
        return plan;
    }
}
