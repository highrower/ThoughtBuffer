using ThoughtBuffer.Models;

namespace ThoughtBuffer.Options;

public sealed class LocalStorageOptions
{
    public const string SectionName = "LocalStorage";

    public string RootPath { get; set; } = "";
    public string RecordingsFolder { get; set; } = "Recordings";
    public string FilteredFolder { get; set; } = "Filtered";
    public string ArchiveFolder { get; set; } = "Archive";
    public string TranscriptFolder { get; set; } = "Transcripts";
    public string NotesFolder { get; set; } = "Notes";

    public string GetRootPath(string defaultHostFolder)
    {
        if (!string.IsNullOrWhiteSpace(RootPath))
            return Environment.ExpandEnvironmentVariables(RootPath);

        var baseAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseAppData, "ThoughtBuffer", defaultHostFolder);
    }

    public AppPaths ToAppPaths(string recordingsPath, string archivePath, string defaultHostFolder)
    {
        var rootPath = GetRootPath(defaultHostFolder);
        return new AppPaths(
            rootPath,
            recordingsPath,
            Path.Combine(rootPath, RecordingsFolder),
            Path.Combine(rootPath, FilteredFolder),
            archivePath,
            Path.Combine(rootPath, TranscriptFolder),
            Path.Combine(rootPath, NotesFolder)
        );
    }

    public AppPaths ToAppPaths(string defaultHostFolder)
    {
        var rootPath = GetRootPath(defaultHostFolder);
        return ToAppPaths(
            Path.Combine(rootPath, RecordingsFolder),
            Path.Combine(rootPath, ArchiveFolder),
            defaultHostFolder
        );
    }
}
