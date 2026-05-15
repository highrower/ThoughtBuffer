# Live Streaming Design

Phase 7A produced the design. Phase 7B adds a disabled-by-default walking skeleton for Twilio live media streams. It accepts WebSocket events, separates inbound and outbound track counters, decodes base64 media payloads, and stores metadata-only artifacts. It does not implement streaming transcription, queues, workers, SQL/EF, auth, or VAD filtering.

## Why This Is Separate

Batch ingestion and live ingestion have different lifecycles.

Batch ingestion receives a completed audio file, creates an `IngestionSession`, creates `BatchAudioInput`, and calls `ProcessBatchAudioAsync`. That pipeline is file-oriented: transcription starts after the full recording exists, artifacts are written after processing, and retry semantics can operate at the file/session level.

Live ingestion receives WebSocket events over time. It must handle connection state, start/stop events, media chunk ordering, partial transcripts, incomplete streams, duplicate events, and finalization. It should be a separate ingestion mode, not an extension of `ProcessBatchAudioAsync`.

## Proposed Endpoint

```text
GET /api/twilio/media-stream
```

The public Twilio URL would be:

```text
wss://thoughtbuffer-api-jairo-dev-62217.azurewebsites.net/api/twilio/media-stream
```

The endpoint accepts only WebSocket upgrade requests from Twilio Media Streams. HTTP requests return a clear non-WebSocket error.

## Proposed Flow

```text
Twilio voice webhook
  -> if Twilio__EnableLiveMediaStreams=true, TwiML starts media stream
  -> WebSocket receives events
  -> LiveIngestionSession created
  -> media chunks decoded
  -> metadata counters update by track
  -> stop or WebSocket close finalizes metadata
  -> live-stream metadata artifact is stored
```

For the current forwarded-call shape, prefer unidirectional `<Start><Stream>` before `<Dial>` so Twilio can continue to execute the existing dial instructions. `<Connect><Stream>` blocks subsequent TwiML until the WebSocket disconnects, so it is a poor fit for "start stream and then forward the call" unless Phase 7B intentionally changes the call flow into a bidirectional bot/conversation model.

## Twilio Stream Shape

Twilio supports starting unidirectional streams with `<Start><Stream>` and bidirectional streams with `<Connect><Stream>`. Unidirectional streams can receive `inbound_track`, `outbound_track`, or `both_tracks`; bidirectional streams can receive only the inbound track.

For a forwarded `<Dial>` call, the first Phase 7B candidate should be:

```xml
<Response>
  <Start>
    <Stream
      name="thoughtbuffer-live"
      url="wss://thoughtbuffer-api-jairo-dev-62217.azurewebsites.net/api/twilio/media-stream"
      track="both_tracks">
      <Parameter name="source" value="twilio-live-media-stream" />
    </Stream>
  </Start>
  <Dial>
    <Number>...</Number>
  </Dial>
</Response>
```

Open design choice: `both_tracks` gives caller-side and forwarded-party-side audio but increases event volume. If Phase 7B only needs caller speech, `inbound_track` is simpler and should be tested first against the exact call forwarding behavior.

The current skeleton keeps the existing `<Dial record="record-from-answer-dual">` recording fallback enabled when streaming is enabled.

## Configuration

Production default is streaming disabled:

```text
Twilio__EnableLiveMediaStreams=false
Twilio__LiveStreamTrack=both_tracks
Twilio__LiveStreamName=thoughtbuffer-live
Twilio__LiveStreamStoreMetadata=true
Twilio__LiveStreamStoreRawChunks=false
```

When `Twilio__EnableLiveMediaStreams=false`, `/api/twilio/voice` returns the existing batch-recording TwiML with no `<Start><Stream>`.

When `Twilio__EnableLiveMediaStreams=true`, `/api/twilio/voice` adds a unidirectional `<Start><Stream>` before `<Dial>`, requests `both_tracks`, and still keeps the batch recording callback.

## Proposed Models

These are design models. Phase 7B implements equivalent internal state for metadata only; public contracts can still evolve before real transcription is added.

```csharp
public sealed record LiveIngestionSession(
    string Id,
    string StreamSid,
    string CallSid,
    SourceSystem Source,
    DateTime StartedAtUtc,
    DateTime? StoppedAtUtc,
    LiveIngestionStatus Status);

public sealed record LiveAudioChunk(
    string SessionId,
    string StreamSid,
    string Track,
    long SequenceNumber,
    long ChunkNumber,
    long TimestampMilliseconds,
    byte[] MulawPayload,
    DateTime ReceivedAtUtc);

public sealed record LiveTranscriptSegment(
    string SessionId,
    string SegmentId,
    string Text,
    TimeSpan? Start,
    TimeSpan? End,
    bool IsFinal,
    DateTime CreatedAtUtc);

public sealed record PartialTranscript(
    string SessionId,
    string Text,
    long SegmentIndex,
    DateTime UpdatedAtUtc);

public sealed record LiveIngestionResult(
    LiveIngestionSession Session,
    IReadOnlyList<LiveTranscriptSegment> FinalSegments,
    string? TranscriptArtifactPath,
    string? NoteArtifactPath,
    string? MetadataArtifactPath);
```

## Proposed Interfaces

These interfaces are intentionally source-agnostic after the Twilio adapter translates WebSocket events into live session events.

```csharp
public interface ILiveIngestionCoordinator
{
    Task<LiveIngestionSession> StartAsync(TwilioStreamStarted started, CancellationToken cancellationToken);
    Task AcceptAudioAsync(LiveAudioChunk chunk, CancellationToken cancellationToken);
    Task<LiveIngestionResult> StopAsync(string streamSid, CancellationToken cancellationToken);
    Task FailAsync(string streamSid, string reason, CancellationToken cancellationToken);
}

public interface IStreamingTranscriptionService
{
    Task StartSessionAsync(LiveIngestionSession session, CancellationToken cancellationToken);
    Task AcceptAudioAsync(LiveAudioChunk chunk, CancellationToken cancellationToken);
    Task<IReadOnlyList<LiveTranscriptSegment>> CompleteAsync(string sessionId, CancellationToken cancellationToken);
}

public interface ILiveTranscriptStore
{
    Task SavePartialAsync(PartialTranscript partial, CancellationToken cancellationToken);
    Task SaveFinalSegmentsAsync(string sessionId, IReadOnlyList<LiveTranscriptSegment> segments, CancellationToken cancellationToken);
}
```

`TwilioStreamStarted` can be either a Twilio-specific DTO inside `Integrations/Twilio` or a normalized internal event with `StreamSid`, `CallSid`, tracks, media format, and custom parameters.

## Artifact Strategy

Phase 7B artifact approach:

- Do not store every raw chunk by default. Chunk-per-blob storage will create many small blobs and operational noise.
- Decode each base64 payload to verify audio byte handling and maintain byte counters by track.
- Do not feed audio to a transcription provider yet.
- Do not reconstruct final audio yet.
- Do not create transcript, note, or real-time UI artifacts yet.
- Store metadata under `sessions/{sessionId}/metadata/`, including stream IDs, call ID, tracks, media format, chunk counts, stop/finalization status, and failure reason when present.

The storage abstraction can stay `IArtifactStorage` for final artifacts, but live partial state likely needs a separate `ILiveTranscriptStore` or in-memory first implementation.

## Failure Modes

- WebSocket disconnect: mark the live session as interrupted. Finalize from received audio only if transcription provider can complete cleanly.
- Missing `stop` event: use connection close as a finalization signal. Metadata records `stopEventReceived=false`.
- Transcription provider failure: preserve stream metadata and any partial transcript state. Return/log a failed finalization status without full transcript text.
- Partial transcript only: store metadata and partial/final segments that are available, clearly marking the session incomplete.
- Duplicate or retried events: use `streamSid`, `sequenceNumber`, `track`, and `chunk` to detect duplicates. Ignore exact duplicates and log sequence gaps.
- Out-of-order chunks: buffer briefly by sequence/chunk number, then process with gap metadata if the missing chunk does not arrive.
- Call completed but stream incomplete: Twilio call completion does not guarantee the stream finalized correctly. Treat stream stop/finalization as its own acceptance gate.
- App recycle during call: on Free/low tiers this is a realistic risk. Phase 7B should document expected behavior before relying on live streams for production use.

## Observability

Log structured fields, not audio or transcript content:

- `streamSid`
- `callSid`
- `sessionId`
- stream event type
- tracks requested and received
- media format
- chunk count by track
- sequence gaps
- duplicate count
- partial transcript count
- final transcript segment count
- finalization status
- failure reason/error code

Do not log Twilio auth token, full transcript text, base64 audio payloads, raw audio bytes, or private blob contents.

## Azure Hosting Notes

Azure App Service supports WebSockets, but it must be enabled for the app. The Free tier is not a good target for reliable live streaming: Azure documents only 5 WebSocket connections per instance on Free for both Windows and Linux, Free/Shared plans run on shared compute, and Free has CPU quota limits. For real streaming tests, plan to move at least to Basic before acceptance testing multiple or longer calls.

Phase 7B should verify the current App Service plan, enable WebSockets, and test one live call before making any product claims.

## Sources

- Twilio Media Streams overview: https://www.twilio.com/docs/voice/media-streams
- Twilio `<Stream>` TwiML reference: https://www.twilio.com/docs/voice/twiml/stream
- Twilio Media Streams WebSocket messages: https://www.twilio.com/docs/voice/media-streams/websocket-messages
- Azure App Service WebSocket limits: https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits#azure-app-service-limits
- Azure WebSockets configuration guidance: https://learn.microsoft.com/en-us/aspnet/signalr/overview/deployment/using-signalr-with-azure-web-sites

## Phase 7B Scope

Phase 7B implements only the smallest walking skeleton:

1. Add a disabled-by-default Twilio live stream mode/config flag.
2. Add `/api/twilio/media-stream` WebSocket endpoint.
3. Parse `connected`, `start`, `media`, and `stop` messages.
4. Decode base64 `audio/x-mulaw` payloads without calling a transcription provider.
5. Track chunk counts and stream metadata.
6. Store metadata-only artifacts for a test stream.
7. Keep existing batch recording behavior unchanged.

Phase 7C or later should add provider selection and real streaming transcription after the metadata skeleton is proven with a real call.
