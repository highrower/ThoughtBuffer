using Microsoft.Extensions.Configuration;

namespace ThoughtBuffer.Options;

public static class ThoughtBufferConfiguration
{
    public static ThoughtBufferOptions GetThoughtBufferOptions(this IConfiguration configuration)
    {
        var section = configuration.GetSection(ThoughtBufferOptions.SectionName);
        return new ThoughtBufferOptions
        {
            DevicePath = section[nameof(ThoughtBufferOptions.DevicePath)] ?? "",
            RecordingFolder = section[nameof(ThoughtBufferOptions.RecordingFolder)] ?? "FOLDER01",
            ArchiveFolder = section[nameof(ThoughtBufferOptions.ArchiveFolder)] ?? "Archive",
            SolutionRoot = section[nameof(ThoughtBufferOptions.SolutionRoot)] ?? "",
            PythonExePath = section[nameof(ThoughtBufferOptions.PythonExePath)] ?? "",
            FilterScriptPath = section[nameof(ThoughtBufferOptions.FilterScriptPath)] ?? ""
        };
    }

    public static OpenAiOptions GetOpenAiOptions(this IConfiguration configuration)
    {
        var section = configuration.GetSection(OpenAiOptions.SectionName);
        return new OpenAiOptions
        {
            ApiKey = section[nameof(OpenAiOptions.ApiKey)] ?? "",
            TranscriptionModel = section[nameof(OpenAiOptions.TranscriptionModel)] ?? "gpt-4o-mini-transcribe",
            SummarizationModel = section[nameof(OpenAiOptions.SummarizationModel)] ?? "gpt-4.1-mini"
        };
    }

    public static LocalStorageOptions GetLocalStorageOptions(this IConfiguration configuration)
    {
        var section = configuration.GetSection(LocalStorageOptions.SectionName);
        return new LocalStorageOptions
        {
            RootPath = section[nameof(LocalStorageOptions.RootPath)] ?? "",
            RecordingsFolder = section[nameof(LocalStorageOptions.RecordingsFolder)] ?? "Recordings",
            FilteredFolder = section[nameof(LocalStorageOptions.FilteredFolder)] ?? "Filtered",
            ArchiveFolder = section[nameof(LocalStorageOptions.ArchiveFolder)] ?? "Archive",
            TranscriptFolder = section[nameof(LocalStorageOptions.TranscriptFolder)] ?? "Transcripts",
            NotesFolder = section[nameof(LocalStorageOptions.NotesFolder)] ?? "Notes",
            MaxUploadBytes = long.TryParse(section[nameof(LocalStorageOptions.MaxUploadBytes)], out var maxUploadBytes)
                ? maxUploadBytes
                : 25 * 1024 * 1024
        };
    }
}
