using System.Diagnostics;
using TechLoungeOS;

internal static class BuildPerformanceChecks
{
    internal static async Task Run(string root,BuildOptions options,Action<bool,string> check)
    {
        check(BuildResources.DownloadConcurrency(512UL<<20)==1&&BuildResources.DownloadConcurrency((2UL<<30)-1)==1&&BuildResources.DownloadConcurrency(2UL<<30)==2,
            "prerequisite concurrency backs off below2GiB free memory rather than launching more workers on a pressured PC");
        static TaskCompletionSource Gate()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered=Gate();var release=Gate();int active=0,started=0,finished=0;
        using(var cancel=new CancellationTokenSource())
        {
            var jobs=Enumerable.Range(0,4).Select(i=>new BuildDownloads.Job("inert "+i,async token=>
            {
                var count=Interlocked.Increment(ref active);
                if(count>2)throw new Exception("Too many prerequisite jobs started concurrently");
                if(Interlocked.Increment(ref started)==2)entered.TrySetResult();
                try{await release.Task.WaitAsync(token);Interlocked.Increment(ref finished);}
                finally{Interlocked.Decrement(ref active);}
            }));
            var batch=BuildDownloads.Prepare(jobs,_=>{},cancel.Token);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                check(started==2&&active==2&&finished==0,"two independent prerequisites really overlap, while additional jobs stay queued");
                release.SetResult();await batch;
                check(finished==4&&active==0,"the bounded prerequisite batch waits for all four successful jobs");
            }
            finally{cancel.Cancel();release.TrySetResult();try{await batch;}catch(OperationCanceledException){}}
        }
        foreach(var fail in new[]{false,true})
        {
            var both=Gate();int running=0,completedCleanup=0,calls=0;
            using var cancel=new CancellationTokenSource();
            var jobs=Enumerable.Range(0,4).Select(i=>new BuildDownloads.Job("cancel fixture "+i,async token=>
            {
                Interlocked.Increment(ref calls);if(Interlocked.Increment(ref running)==2)both.TrySetResult();
                try
                {
                    await both.Task.WaitAsync(token);
                    if(fail&&i==0)throw new IOException("Inert download failure");
                    await Task.Delay(Timeout.Infinite,token);
                }
                finally{Interlocked.Decrement(ref running);Interlocked.Increment(ref completedCleanup);}
            }));
            var batch=BuildDownloads.Prepare(jobs,_=>{},cancel.Token);
            try
            {
                await both.Task.WaitAsync(TimeSpan.FromSeconds(5));if(!fail)cancel.Cancel();
                try{await batch.WaitAsync(TimeSpan.FromSeconds(5));throw new Exception("Incomplete downloads accepted");}
                catch(Exception error) when(fail?error is IOException:error is OperationCanceledException){}
                check(calls==2&&running==0&&completedCleanup==2,fail?
                    "a failed prerequisite cancels queued work and joins its active peer before returning":
                    "cancellation joins both active prerequisites without launching queued work");
            }
            finally{cancel.Cancel();try{await batch;}catch(Exception error) when(error is IOException or OperationCanceledException){}}
        }

        foreach(var handoff in new[]{"No component store corruption detected.","unrecognised scan","The component store is repairable."})
        {
            var workspace=Path.Combine(root,"health-handoff-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(workspace);
            var mount=Path.Combine(workspace,"mount");var runner=new FixtureRunner(workspace);
            await runner.Dism("/Mount-Image","/MountDir:"+mount);
            try
            {
                await ImageServicing.Run(runner,mount,options with{Workspace=workspace},_=>{},CancellationToken.None,_=>new Dictionary<string,string>(),new WindowsImageUpdates.VerifiedHealthScan(mount,handoff));
                check(handoff.StartsWith("No component")&&runner.Commands.Count(c=>c.Contains("/ScanHealth"))==1&&
                    File.Exists(Path.Combine(workspace,@"mount\ProgramData\TechLoungeOS\first-login.ps1"))&&
                    File.ReadAllText(Path.Combine(workspace,"health-before.txt"))==handoff&&
                    File.ReadAllText(Path.Combine(workspace,"health-after.txt"))=="No component store corruption detected.",
                    "a same-transaction healthy result replaces only the adjacent pre-scan; final full scan and provisioning remain");
            }
            catch(IOException) when(!handoff.StartsWith("No component"))
            {check(!Directory.Exists(Path.Combine(mount,@"ProgramData\TechLoungeOS")),"an unhealthy or unrecognised health handoff cannot reach provisioning");}
        }
        var oneImage=Path.Combine(root,"scan-image");
        var singleUse=new WindowsImageUpdates.VerifiedHealthScan(oneImage,"No component store corruption detected.");
        try{singleUse.Take(Path.Combine(root,"another-image"));throw new Exception("Health scan crossed image boundaries");}
        catch(IOException){check(true,"a verified health handoff rejects a different image mount");}
        check(singleUse.Take(oneImage)=="No component store corruption detected.","a mismatched mount does not consume the correct image's scan");
        try{singleUse.Take(oneImage);throw new Exception("Health scan reused twice");}
        catch(IOException){check(true,"a verified health handoff is single-use, preventing stale repeated reuse");}
        var timings=new List<double>();
        foreach(var parallel in new[]{false,true})
        {
            var watch=Stopwatch.StartNew();
            var jobs=Enumerable.Range(0,4).Select(i=>new BuildDownloads.Job("timed inert "+i,token=>Task.Delay(150,token))).ToArray();
            await BuildDownloads.Prepare(jobs,_=>{},CancellationToken.None,parallel?2:1);
            timings.Add(watch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine($"Synthetic prerequisite scheduling sample: serial {timings[0]:F1}ms; bounded parallel {timings[1]:F1}ms. Not an internet or whole-build benchmark.");
    }
}
