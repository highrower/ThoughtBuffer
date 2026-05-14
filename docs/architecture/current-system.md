# Current System

ThoughtBuffer is now an API-first ingestion app. The console app still exists for local recorder compatibility, but the deployed host is `ThoughtBuffer.Api`.

## Project Structure

```text
ThoughtBuffer.Api/       ASP.NET Core API host and HTTP contracts
Application/             Source-agnostic ingestion pipeline
Integrations/Twilio/     Twilio voice and completed-recording source adapter
Models/                  Domain models and processing options
Services/                OpenAI transcription and summarization adapters
Storage/                 Artifact storage abstraction and implementations
Summarization/           Named summarization profile instructions
Formatting/              Markdown note rendering
Program.cs               Temporary console/dev entrypoint
ThoughtBuffer.cs         Local recorder compatibility flow
```

## Responsibilities

`ThoughtBuffer.Api` handles HTTP uploads, request validation, processing option parsing, and API responses.

The Twilio integration handles inbound voice webhooks and completed recording callbacks. It stays at the source-adapter boundary: Twilio-specific request validation, recording download, and source normalization happen in `Integrations/Twilio`, then the adapter hands an `IngestionSession`, `AudioAsset`, and `BatchAudioInput` to the source-agnostic batch pipeline.

The console app handles the old local recorder menu and should not become the long-term product center.

`Application/IngestionPipeline` handles batch audio processing through `ProcessBatchAudioAsync(BatchIngestionRequest)`.

The pipeline currently:
- transcribes audio
- optionally summarizes
- creates transcript, note, and metadata artifacts
- writes artifacts through `IArtifactStorage`

Storage is provider-based:
- `LocalFileArtifactStorage` for local development
- `AzureBlobArtifactStorage` for hosted durable artifacts

Implemented ingestion sources:
- manual audio upload through `POST /api/ingestions/audio`
- Twilio completed batch recordings through `POST /api/twilio/recording-status`

Summarization supports named profiles:
- `ThoughtNote`
- `SalesCall`
- `SupportCall`
- `IntakeCall`

## Azure Resources

```text
Resource group: rg-thoughtbuffer-dev
App Service: thoughtbuffer-api-jairo-dev-62217
Storage account: sttb86433590
Blob container: thoughtbuffer-artifacts
```

Current hosted storage is Azure Blob Storage. SQL/EF, queues, auth, and streaming are not implemented yet. Twilio batch recording ingestion is implemented; Twilio live media streaming is not.
