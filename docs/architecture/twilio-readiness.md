# Twilio Readiness

Twilio should be added as a source adapter, not as the core domain.

The core pipeline should continue to work with:

```text
IngestionSession
BatchIngestionRequest
BatchAudioInput
ProcessBatchAudioAsync
```

## First Twilio Phase

Start with post-call recording webhooks, not streaming.

Expected batch flow:

```text
Twilio recording completed webhook
  -> validate webhook
  -> read CallSid and RecordingSid
  -> create IngestionSession
  -> map CallSid/RecordingSid to ExternalId or equivalent metadata
  -> download recording
  -> create BatchAudioInput
  -> ProcessBatchAudioAsync
  -> store artifacts in Blob Storage
```

Twilio-specific code should live in an integration layer, for example:

```text
Integrations/Twilio/
```

It should not change the pipeline into a Twilio-specific workflow.

## Deferred

Streaming is deferred.

Live media streams should use a separate live ingestion model later because chunked audio, partial transcripts, and finalization have different behavior than batch recordings.

Speaker/channel handling is also deferred. The batch recording phase should not assume the final speaker strategy.
