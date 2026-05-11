# Current System

ThoughtBuffer is now an API-first ingestion app. The console app still exists for local recorder compatibility, but the deployed host is `ThoughtBuffer.Api`.

## Project Structure

```text
ThoughtBuffer.Api/       ASP.NET Core API host and HTTP contracts
Application/             Source-agnostic ingestion pipeline
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

Current hosted storage is Azure Blob Storage. SQL/EF, queues, auth, Twilio, and streaming are not implemented yet.
