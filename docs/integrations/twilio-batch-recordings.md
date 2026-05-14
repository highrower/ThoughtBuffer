# Twilio Batch Call Recording

Twilio batch call recording is implemented as a source adapter for the existing batch ingestion pipeline.

The API answers Twilio voice webhooks with TwiML, forwards calls to `Twilio__ForwardToPhoneNumber`, records the bridged call through Twilio's `<Dial>` verb, accepts completed recording callbacks, downloads the recording with Twilio account credentials, and processes the local audio file through `ProcessBatchAudioAsync`.

## Webhook URLs

Voice webhook:

```text
https://thoughtbuffer-api-jairo-dev-62217.azurewebsites.net/api/twilio/voice
```

Recording status callback:

```text
https://thoughtbuffer-api-jairo-dev-62217.azurewebsites.net/api/twilio/recording-status
```

## Twilio Setup Overview

In Twilio Console:

1. Buy or choose a Twilio phone number.
2. Open the number configuration.
3. Under voice incoming calls, set webhook method to `POST`.
4. Set the voice webhook URL to:

```text
https://thoughtbuffer-api-jairo-dev-62217.azurewebsites.net/api/twilio/voice
```

The voice endpoint returns TwiML that:

- says a short recording notice
- dials `Twilio__ForwardToPhoneNumber`
- enables recording on the dial
- sets `recordingStatusCallback` to `/api/twilio/recording-status`
- requests `recordingStatusCallbackEvent=completed`

Twilio calls the recording status callback with form-encoded fields such as:

```text
CallSid
RecordingSid
RecordingUrl
RecordingStatus
AccountSid
RecordingDuration
RecordingChannels
RecordingSource
```

The API currently:

- returns TwiML for inbound calls
- accepts Twilio recording status callbacks
- validates required fields
- optionally validates `X-Twilio-Signature`
- ignores non-completed recording statuses
- downloads completed recordings with `Twilio__AccountSid` and `Twilio__AuthToken`
- creates an `IngestionSession` with `SourceSystem.TwilioRecording`
- maps `IngestionSession.ExternalId` to `CallSid:RecordingSid`
- creates a local `AudioAsset` and `BatchAudioInput`
- processes the recording through `ProcessBatchAudioAsync`
- writes audio, transcript, note, and metadata artifacts through the configured `IArtifactStorage`

Completed webhook responses include status, session ID, CallSid, RecordingSid, processing mode, summarization profile, and artifact references when available.

## Verified Real-Call Flow

Phase 6B was verified with a real Twilio call against the deployed API:

1. The Twilio number receives an inbound call.
2. `POST /api/twilio/voice` validates the Twilio signature and returns TwiML.
3. Twilio dials `Twilio__ForwardToPhoneNumber`.
4. Twilio records the bridged call through `DialVerb`.
5. Twilio sends a completed recording callback to `POST /api/twilio/recording-status`.
6. The API validates the callback and required recording fields.
7. The Twilio adapter downloads the recording to temporary local API storage.
8. The adapter creates an `IngestionSession` with `SourceSystem.TwilioRecording`.
9. The adapter creates `AudioAsset` and `BatchAudioInput`.
10. `ProcessBatchAudioAsync` transcribes and summarizes the recording.
11. Azure Blob Storage receives artifacts under `sessions/{sessionId}/`:
    - `audio/`
    - `transcripts/`
    - `notes/`
    - `metadata/`

The real-call transcript closely matched the spoken phrase: `Thought Buffer Twilio ingestion test one two three.`

## Azure App Service Settings

Configure these under App Service application settings:

```text
Twilio__AccountSid=<your Twilio account SID>
Twilio__AuthToken=<your Twilio auth token>
Twilio__ValidateSignatures=true
Twilio__ForwardToPhoneNumber=<phone number to forward calls to>
Twilio__DefaultProcessingMode=TranscribeAndSummarize
Twilio__DefaultSummarizationProfile=IntakeCall
```

For local unsigned tests, use:

```text
Twilio__ValidateSignatures=false
```

Do not commit Twilio secrets.

## Batch Flow

Current flow:

```text
Twilio recording completed webhook
  -> validate Twilio signature
  -> map CallSid/RecordingSid to IngestionSession.ExternalId
  -> download RecordingUrl
  -> create BatchAudioInput
  -> call ProcessBatchAudioAsync
  -> write artifacts to Blob Storage
```

Twilio remains a source adapter. It should not become the center of the domain model.

## Observability

The API emits structured logs for the Twilio batch flow:

- Twilio voice webhook received
- Twilio recording callback received
- Twilio ingestion session created
- Twilio recording download started
- Twilio recording download completed
- Twilio recording ingestion started
- Twilio recording ingestion completed
- Artifact write completed

Logs include correlation IDs such as `SessionId`, `CallSid`, and `RecordingSid`. Logs must not include Twilio credentials, full transcript text, note content, or private blob contents.

To enable App Service logs for callback diagnostics:

```powershell
az webapp log config `
  --name thoughtbuffer-api-jairo-dev-62217 `
  --resource-group rg-thoughtbuffer-dev `
  --application-logging filesystem `
  --level Information `
  --web-server-logging filesystem
```

To download logs after a test:

```powershell
az webapp log download `
  --name thoughtbuffer-api-jairo-dev-62217 `
  --resource-group rg-thoughtbuffer-dev `
  --log-file appservice-logs.zip
```

For routine production operation, keep retention modest and avoid increasing log verbosity beyond what is needed for diagnostics.

## Deferred

Streaming is deferred. Twilio live media streams need a separate live ingestion model later.

Python/Silero VAD filtering is also deferred for hosted/API ingestion. It remains local/worker-future behavior, not part of Twilio batch v1.
