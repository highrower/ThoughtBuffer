using Microsoft.Extensions.Configuration;
using ThoughtBuffer.Integrations.Twilio;
using ThoughtBuffer.Models;

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

    public static ArtifactStorageOptions GetArtifactStorageOptions(this IConfiguration configuration)
    {
        var section = configuration.GetSection(ArtifactStorageOptions.SectionName);
        return new ArtifactStorageOptions
        {
            Provider = section[nameof(ArtifactStorageOptions.Provider)] ?? "Local",
            ContainerName = section[nameof(ArtifactStorageOptions.ContainerName)] ?? "thoughtbuffer-artifacts",
            ConnectionString = section[nameof(ArtifactStorageOptions.ConnectionString)] ?? "",
            LocalRootPath = section[nameof(ArtifactStorageOptions.LocalRootPath)] ?? ""
        };
    }

    public static TwilioOptions GetTwilioOptions(this IConfiguration configuration)
    {
        var section = configuration.GetSection(TwilioOptions.SectionName);
        return new TwilioOptions
        {
            AccountSid = section[nameof(TwilioOptions.AccountSid)] ?? "",
            AuthToken = section[nameof(TwilioOptions.AuthToken)] ?? "",
            ValidateSignatures = bool.TryParse(section[nameof(TwilioOptions.ValidateSignatures)], out var validate)
                ? validate
                : true,
            PublicBaseUrl = section[nameof(TwilioOptions.PublicBaseUrl)] ?? "",
            ForwardToPhoneNumber = section[nameof(TwilioOptions.ForwardToPhoneNumber)] ?? "",
            DefaultProcessingMode = Enum.TryParse(
                section[nameof(TwilioOptions.DefaultProcessingMode)],
                ignoreCase: true,
                out ProcessingMode mode)
                ? mode
                : ProcessingMode.TranscribeAndSummarize,
            DefaultSummarizationProfile = Enum.TryParse(
                section[nameof(TwilioOptions.DefaultSummarizationProfile)],
                ignoreCase: true,
                out SummarizationProfile profile)
                ? profile
                : SummarizationProfile.IntakeCall,
            EnableLiveMediaStreams = bool.TryParse(section[nameof(TwilioOptions.EnableLiveMediaStreams)], out var enableLiveMediaStreams)
                ? enableLiveMediaStreams
                : false,
            LiveStreamTrack = section[nameof(TwilioOptions.LiveStreamTrack)] ?? "both_tracks",
            LiveStreamName = section[nameof(TwilioOptions.LiveStreamName)] ?? "thoughtbuffer-live",
            LiveStreamStoreMetadata = bool.TryParse(section[nameof(TwilioOptions.LiveStreamStoreMetadata)], out var liveStreamStoreMetadata)
                ? liveStreamStoreMetadata
                : true,
            LiveStreamStoreRawChunks = bool.TryParse(section[nameof(TwilioOptions.LiveStreamStoreRawChunks)], out var liveStreamStoreRawChunks)
                ? liveStreamStoreRawChunks
                : false
        };
    }
}
