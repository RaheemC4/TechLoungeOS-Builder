sealed class RecordingBuildProgress : IProgress<string>
{
    public readonly List<string> Phases = [];
    public void Report(string phase) => Phases.Add(phase);
}
