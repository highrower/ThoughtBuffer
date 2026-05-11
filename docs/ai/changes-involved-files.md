Existing files likely touched first:

- Program.cs
- ThoughtBuffer.cs
- Models/RecordingEntry.cs
- Models/AppPaths.cs
- Models/SummaryResult.cs
- Formatting/MarkdownNoteBuilder.cs
- Services/ITranscriptionService.cs
- Services/ISummarizationService.cs
- Services/IAudioFilterService.cs
- Services/OpenAiTranscriptionService.cs
- Services/OpenAiSummarizationService.cs
- Services/PythonAudioFilterService.cs
- Services/FfmpegAudioPreprocessor.cs

Proposed new domain/application classes:

- Models/IngestionSession.cs
- Models/IngestionMode.cs
- Models/SourceSystem.cs
- Models/AudioAsset.cs
- Models/Transcript.cs
- Models/TranscriptSegment.cs
- Models/SummaryResult.cs
- Models/IngestionArtifact.cs

- Application/IIngestionPipeline.cs
- Application/IngestionPipeline.cs
- Application/ProcessTextInput.cs
- Application/ProcessAudioFileInput.cs
- Application/ProcessAudioUrlInput.cs
- Application/ProcessLiveAudioChunk.cs

Proposed new service interfaces:

``` c#
public interface IIngestionPipeline
{
Task<ThoughtBufferResult> ProcessTextAsync(TextInput input, CancellationToken ct);
Task<ThoughtBufferResult> ProcessAudioAsync(AudioInput input, CancellationToken ct);
}

public interface IAudioStorage
{
Task<AudioAsset> SaveAudioAsync(Stream audio, AudioMetadata metadata, CancellationToken ct);
Task<Stream> OpenReadAsync(string audioAssetId, CancellationToken ct);
}

public interface ITranscriptStorage
{
Task SaveTranscriptAsync(Transcript transcript, CancellationToken ct);
Task<Transcript?> GetTranscriptAsync(string sessionId, CancellationToken ct);
}

public interface ISummaryStorage
{
Task SaveSummaryAsync(SummaryResult summary, CancellationToken ct);
}

public interface IStreamingTranscriptionService
{
Task StartAsync(string sessionId, TranscriptionOptions options, CancellationToken ct);
Task SendAudioChunkAsync(string sessionId, AudioChunk chunk, CancellationToken ct);
Task<Transcript> FinishAsync(string sessionId, CancellationToken ct);
}
```

Proposed Twilio classes:

``` c#
Integrations/Twilio/TwilioRecordingWebhookController.cs
Integrations/Twilio/TwilioMediaStreamEndpoint.cs
Integrations/Twilio/TwilioRecordingIngestionService.cs
Integrations/Twilio/TwilioMediaStreamIngestionService.cs
Integrations/Twilio/TwilioWebhookValidator.cs
Integrations/Twilio/TwilioRecordingWebhook.cs
Integrations/Twilio/TwilioStreamConnectedMessage.cs
Integrations/Twilio/TwilioStreamMediaMessage.cs
Integrations/Twilio/TwilioStreamStoppedMessage.cs

Proposed Azure infrastructure classes:

Infrastructure/Storage/BlobAudioStorage.cs
Infrastructure/Storage/BlobArtifactStorage.cs
Infrastructure/Persistence/ThoughtBufferDbContext.cs
Infrastructure/Queues/IngestionQueue.cs
Infrastructure/Queues/TranscriptionQueue.cs
Infrastructure/Queues/SummarizationQueue.cs
Workers/DownloadTwilioRecordingJob.cs
Workers/TranscribeAudioJob.cs
Workers/SummarizeTranscriptJob.cs

Recommended future solution layout:

ThoughtBuffer.Cli
ThoughtBuffer.Api
ThoughtBuffer.Application
ThoughtBuffer.Domain
ThoughtBuffer.Infrastructure
ThoughtBuffer.Worker