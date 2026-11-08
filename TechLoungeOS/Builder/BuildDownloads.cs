using System.Diagnostics;

namespace TechLoungeOS;

// Only independent payload folders share this batch. WinGet's runtime downloads
// stay serial inside their one job; image servicing never runs through this helper.
internal static class BuildDownloads
{
    internal sealed record Job(string Name,Func<CancellationToken,Task> Prepare);

    internal static Task Prepare(IEnumerable<Job> jobs,Action<string> log,CancellationToken ct,int concurrency=2)
    {
        if(concurrency is <1 or >2)throw new ArgumentOutOfRangeException(nameof(concurrency));
        return Parallel.ForEachAsync(jobs,new ParallelOptions{MaxDegreeOfParallelism=concurrency,CancellationToken=ct},async (job,token)=>
        {
            var elapsed=Stopwatch.StartNew();
            log("Preparing required files: "+job.Name);
            await job.Prepare(token);
            token.ThrowIfCancellationRequested();
            log($"Required files verified: {job.Name} ({elapsed.Elapsed.TotalSeconds:F3}s).");
        });
    }
}
