# Next Steps

Recommended order:

1. Keep Twilio batch recording stable and observable.
2. Decide speaker/channel strategy for Twilio recordings.
3. Add SQL/EF metadata model for customers and calls when query needs are clear.
4. Design streaming ingestion as the next Twilio architecture phase.
5. Implement streaming only after the design clarifies lifecycle, storage, transcript partials, and failure handling.

## Notes

Twilio batch recording has proven the adapter pattern with a real call:

```text
Twilio source adapter
  -> IngestionSession
  -> BatchAudioInput
  -> ProcessBatchAudioAsync
```

SQL/EF should wait until there is enough call/customer metadata to justify a queryable model.

Streaming is the next design phase, not an immediate blind implementation. Treat it as a separate ingestion mode with its own lifecycle and operational requirements. The design should answer:

- how live audio sessions are opened, correlated, and closed
- whether partial transcripts are stored and where
- how final transcript and summary artifacts are produced
- how retries and dropped media frames are represented
- what observability is required for real-time failures

Do not add Twilio live media streams directly into the batch recording adapter.

Deferred features:

```text
Twilio live media streams
SQL/EF persistence
queues/workers
auth
speaker diarization or channel separation
```
