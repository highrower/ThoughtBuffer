using ThoughtBuffer.Models;

namespace ThoughtBuffer.Integrations.Twilio;

public sealed class TwilioRecordingIngestionService(TwilioOptions options)
{
    public TwilioRecordingWebhookResult Inspect(TwilioRecordingWebhookRequest request)
    {
        var normalizedStatus = request.RecordingStatus.Trim();

        if (!normalizedStatus.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            return new TwilioRecordingWebhookResult(
                "ignored",
                $"Recording status '{request.RecordingStatus}' is not ready for batch ingestion.",
                null,
                options.DefaultProcessingMode,
                options.DefaultSummarizationProfile);
        }

        var session = new IngestionSession(
            Guid.NewGuid().ToString("N"),
            IngestionMode.AudioUrl,
            SourceSystem.TwilioRecording,
            DateTime.UtcNow,
            ExternalId: $"{request.CallSid}:{request.RecordingSid}",
            DisplayName: $"Twilio recording {request.RecordingSid}");

        return new TwilioRecordingWebhookResult(
            "accepted",
            "Completed Twilio recording webhook accepted. Download and processing are deferred to the next phase.",
            session,
            options.DefaultProcessingMode,
            options.DefaultSummarizationProfile);
    }
}

public sealed record TwilioRecordingWebhookResult(
    string Status,
    string Message,
    IngestionSession? Session,
    ProcessingMode ProcessingMode,
    SummarizationProfile SummarizationProfile
);
