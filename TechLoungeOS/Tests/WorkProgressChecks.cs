using TechLoungeOS;
internal static class WorkProgressChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var now=DateTimeOffset.Parse("2026-11-07T20:00:00Z");var progress=new WorkProgress(now);
        check(progress.Elapsed(now.AddSeconds(75))==TimeSpan.FromSeconds(75),"Elapsed time is measured independently of phase progress");
        check(progress.Remaining(now) is null,"No invented estimate before measurable progress");
        progress.Observe("1% complete",now.AddSeconds(1));check(progress.Remaining(now.AddSeconds(1)) is null,"A single sample cannot create an ETA");
        progress.Observe("11% complete",now.AddSeconds(11));check(progress.Remaining(now.AddSeconds(11))==TimeSpan.FromSeconds(89),"Two native progress samples produce a current-step rate estimate");
        check(progress.Remaining(now.AddSeconds(42)) is null,"An old progress sample cannot imply a live remaining estimate");
        progress.Observe("[===== 100.0% =====]",now.AddSeconds(20));check(progress.Remaining(now.AddSeconds(20)) is null,"100 percent does not invent progress for later stages");
        progress.Phase(now.AddSeconds(25));check(progress.Remaining(now.AddSeconds(25)) is null,"A new phase resets the estimate while preserving elapsed time");
        progress.Observe("A package ID contains 50% elsewhere",now.AddSeconds(26));check(progress.Remaining(now.AddSeconds(26)) is null,"Arbitrary text is not treated as tool progress");
        check(WorkProgress.BuildStep("Copying Windows installation media. Full logs are saved in the workspace.")==2 && WorkProgress.BuildStep("Exporting selected edition. Full logs are saved in the workspace.")==3 && WorkProgress.BuildStep("Creating your bootable ISO…")==4,"Known build milestones map to stable declared steps");
        check(WorkProgress.BuildStep("Downloading and verifying offline Office payload")==1 && WorkProgress.BuildStep("Preparing compact NVIDIA setup tools; drivers download after installation")==1,"Payload preparation stays before the copy milestone instead of skipping step two");
        var routed=new WorkProgress(now);var sampleTime=now;var reporter=new MeasuredBuildProgress(_=>throw new IOException("Raw output must never become a visible phase"),line=>routed.Observe(line,sampleTime));
        reporter.ObserveOutput("[====10.0%====]");sampleTime=now.AddSeconds(10);reporter.ObserveOutput("[====20.0%====]");
        check(routed.Remaining(sampleTime)==TimeSpan.FromSeconds(80),"Actual raw worker samples reach the estimator without becoming visible phase text");
        check(WorkProgress.Caption(new WorkProgress(now),now.AddSeconds(62))=="Elapsed 01:02","Unknown remaining time is omitted instead of displayed as a placeholder");
        check(WorkProgress.Duration(TimeSpan.FromSeconds(3675))=="1:01:15","Long operations keep an unambiguous elapsed clock");
    }
}
