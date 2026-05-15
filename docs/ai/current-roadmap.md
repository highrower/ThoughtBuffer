ThoughtBuffer should become a generic ingestion/transcription/summarization pipeline, not a recorder-specific CLI app.

The central abstraction should be an IngestionSession, not a RecordingEntry. A session can come from local recorder files, uploaded audio, raw text, Twilio post-call recordings, Twilio live media streams, browser mic input, WhatsApp voice notes, or future sources.

Twilio should be treated as one source adapter, not as the core domain. There should be two separate Twilio flows:

Batch recording flow:

Twilio call ends
- → Twilio recording webhook arrives
- → app downloads recording
- → stores raw audio
- → queues transcription
- → stores transcript
- → queues summarization
- → stores summary/note

Live stream flow:

Twilio call starts
- → Twilio opens WebSocket/media stream
- → app receives audio chunks
- → chunks are forwarded to streaming transcription
- → partial transcript events are persisted
- → call ends
- → final transcript is produced
- → summary is generated

Do not start with live streaming first. Implement the batch Twilio recording webhook first because it proves the storage, metadata, transcription, and summary model. Then live streaming becomes another ingestion mode.

Azure target architecture should use:

- Azure App Service or Azure Container Apps
- Azure Blob Storage
- Azure SQL/Postgres/SQLite early-stage metadata store
- Azure Service Bus or Storage Queue
- Azure Key Vault
- Application Insights

The CLI/local recorder path should remain usable, but it should become only one adapter into the same pipeline.