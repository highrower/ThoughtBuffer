using ThoughtBuffer.Models;

namespace ThoughtBuffer.Integrations.Twilio;

public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public bool ValidateSignatures { get; set; } = true;
    public string PublicBaseUrl { get; set; } = "";
    public string ForwardToPhoneNumber { get; set; } = "";
    public ProcessingMode DefaultProcessingMode { get; set; } = ProcessingMode.TranscribeAndSummarize;
    public SummarizationProfile DefaultSummarizationProfile { get; set; } = SummarizationProfile.IntakeCall;
    public bool EnableLiveMediaStreams { get; set; } = false;
    public string LiveStreamTrack { get; set; } = "both_tracks";
    public string LiveStreamName { get; set; } = "thoughtbuffer-live";
    public bool LiveStreamStoreMetadata { get; set; } = true;
    public bool LiveStreamStoreRawChunks { get; set; } = false;
}
