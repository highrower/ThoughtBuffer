using ThoughtBuffer.Models;

namespace ThoughtBuffer.Integrations.Twilio;

public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public bool ValidateSignatures { get; set; } = true;
    public ProcessingMode DefaultProcessingMode { get; set; } = ProcessingMode.TranscribeAndSummarize;
    public SummarizationProfile DefaultSummarizationProfile { get; set; } = SummarizationProfile.IntakeCall;
}
