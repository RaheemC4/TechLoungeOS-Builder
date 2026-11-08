using System.Globalization;
using System.Text.RegularExpressions;

namespace TechLoungeOS;

// Estimates describe the current measured sub-operation, never an invented whole-build percentage.
internal sealed class WorkProgress
{
    private readonly object gate=new();
    private DateTimeOffset started,lastSample;
    private double? firstPercent,lastPercent;
    private DateTimeOffset firstSample;
    internal WorkProgress(DateTimeOffset now){started=now;}
    internal void Phase(DateTimeOffset now){lock(gate){firstPercent=lastPercent=null;}}
    internal void Observe(string text,DateTimeOffset now)
    {
        var match=Regex.Match(text,@"(?:^|[\r\n])\s*(?:\[[=> \-]*)?(\d{1,3}(?:\.\d+)?)%\s*(?:complete|[= >\-]*\])",RegexOptions.IgnoreCase);
        if(!match.Success||!double.TryParse(match.Groups[1].Value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var percent)||percent<0||percent>100)return;
        lock(gate)
        {
            if(lastPercent is double previous && percent<previous){firstPercent=null;}
            if(firstPercent is null){firstPercent=percent;firstSample=now;}
            if(lastPercent is null||percent!=lastPercent){lastPercent=percent;lastSample=now;}
        }
    }
    internal TimeSpan Elapsed(DateTimeOffset now)=>now>started?now-started:TimeSpan.Zero;
    internal TimeSpan? Remaining(DateTimeOffset now)
    {
        lock(gate)
        {
            if(firstPercent is not double first||lastPercent is not double last||last<=first||last>=100||now-lastSample>TimeSpan.FromSeconds(30)||lastSample-firstSample<TimeSpan.FromSeconds(5))return null;
            var seconds=(lastSample-firstSample).TotalSeconds/(last-first)*(100-last)-(now-lastSample).TotalSeconds;
            return seconds>0&&seconds<86400?TimeSpan.FromSeconds(seconds):null;
        }
    }
    internal static int BuildStep(string status)=>status switch
    {
        var s when s.StartsWith("Preparing the required Microsoft")||s.StartsWith("Checking the selected")||s.StartsWith("Preparing build")||s.StartsWith("Checking Microsoft Windows updates")||s.StartsWith("Downloading and verifying")||s.StartsWith("Preparing compact NVIDIA")=>1,
        var s when s.StartsWith("Copying Windows")=>2,
        var s when s.StartsWith("Creating your bootable ISO")||s.StartsWith("Creating ISO")||s.StartsWith("Checking every file")||s.StartsWith("Verifying ISO")||s.StartsWith("All files verified")||s.StartsWith("Finalising output")||s.StartsWith("USB media ready")||s=="Complete"=>4,
        _=>3
    };
    internal static string Duration(TimeSpan time)=>time.TotalHours>=1?((long)time.TotalHours).ToString(CultureInfo.InvariantCulture)+":"+time.ToString(@"mm\:ss"):time.ToString(@"mm\:ss");
    internal static string Caption(WorkProgress progress,DateTimeOffset now)
    {
        var estimate=progress.Remaining(now);
        return "Elapsed "+Duration(progress.Elapsed(now))+(estimate is { } remaining?" · About "+Duration(remaining)+" left for this part":"");
    }
}

// Phase changes are marshalled to the window; raw worker output feeds only the rate estimator.
internal sealed class MeasuredBuildProgress(Action<string> phase,Action<string> output):IProgress<string>
{
    private readonly IProgress<string> phases=new Progress<string>(phase);
    public void Report(string value)=>phases.Report(value);
    internal void ObserveOutput(string line)=>output(line);
}
