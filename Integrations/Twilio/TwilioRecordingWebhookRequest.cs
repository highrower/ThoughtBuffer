namespace ThoughtBuffer.Integrations.Twilio;

public sealed record TwilioRecordingWebhookRequest(
    string CallSid,
    string RecordingSid,
    string RecordingUrl,
    string RecordingStatus,
    string? AccountSid = null,
    string? RecordingDuration = null,
    string? RecordingChannels = null,
    string? RecordingSource = null
);
