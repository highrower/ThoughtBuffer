namespace ThoughtBuffer.Options;

public sealed class ThoughtBufferOptions
{
    public const string SectionName = "ThoughtBuffer";

    public string DevicePath { get; set; } = "";
    public string RecordingFolder { get; set; } = "FOLDER01";
    public string ArchiveFolder { get; set; } = "Archive";
    public string SolutionRoot { get; set; } = "";
    public string PythonExePath { get; set; } = "";
    public string FilterScriptPath { get; set; } = "";
}
