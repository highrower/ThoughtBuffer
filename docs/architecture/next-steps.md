# Next Steps

Recommended order:

1. Add Twilio batch recording webhook.
2. Download Twilio recordings and store them as Blob artifacts.
3. Process Twilio batch calls through `ProcessBatchAudioAsync`.
4. Decide speaker/channel strategy.
5. Add SQL/EF metadata model for customers and calls.
6. Design streaming ingestion.
7. Implement streaming ingestion.

## Notes

Twilio batch recording should prove the adapter pattern first.

```text
Twilio source adapter
  -> IngestionSession
  -> BatchAudioInput
  -> ProcessBatchAudioAsync
```

SQL/EF should wait until there is enough call/customer metadata to justify a queryable model.

Streaming should wait until batch ingestion, storage, metadata, and summarization behavior are stable.

Deferred features:

```text
Twilio live media streams
SQL/EF persistence
queues/workers
auth
speaker diarization or channel separation
```
