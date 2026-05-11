# Twilio Batch Recording Webhook

This phase adds the first Twilio source adapter skeleton. It does not download recordings or process calls yet.

## Webhook URL

Use this URL for Twilio recording status callbacks:

```text
https://thoughtbuffer-api-jairo-dev-62217.azurewebsites.net/api/twilio/recording-status
```

## Twilio Setup Overview

In Twilio, configure a recording status callback for completed call recordings. The callback should send form-encoded fields such as:

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

- accepts Twilio recording status callbacks
- validates required fields
- optionally validates `X-Twilio-Signature`
- ignores non-completed recording statuses
- accepts completed callbacks as ready for the next batch phase

It does not download the recording yet.

## Azure App Service Settings

Configure these under App Service application settings:

```text
Twilio__AccountSid=<your Twilio account SID>
Twilio__AuthToken=<your Twilio auth token>
Twilio__ValidateSignatures=true
Twilio__DefaultProcessingMode=TranscribeAndSummarize
Twilio__DefaultSummarizationProfile=IntakeCall
```

For local unsigned tests, use:

```text
Twilio__ValidateSignatures=false
```

Do not commit Twilio secrets.

## Future Batch Flow

The next Twilio phase should be:

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

## Deferred

Streaming is deferred. Twilio live media streams need a separate live ingestion model later.

Python/Silero VAD filtering is also deferred for hosted/API ingestion. It remains local/worker-future behavior, not part of Twilio batch v1.
