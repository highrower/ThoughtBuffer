# Twilio Batch Call Recording

This phase adds the first Twilio source adapter skeleton. It can answer a Twilio voice webhook with TwiML that forwards a call and enables call recording. It does not download recordings or process calls yet.

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

Twilio will later call the recording status callback with form-encoded fields such as:

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
- accepts completed callbacks as ready for the next batch phase

It does not download the recording yet.

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
