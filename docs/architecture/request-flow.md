# Request Flow

## Manual Upload

```text
Client
  -> POST /api/ingestions/audio
  -> API validates file and form options
  -> API stores temporary local upload for processing
  -> API creates IngestionSession
  -> API creates BatchIngestionRequest
  -> IngestionPipeline.ProcessBatchAudioAsync
  -> OpenAI transcription
  -> optional OpenAI summarization
  -> artifact storage
  -> API response
```

## Processing Options

Default behavior:

```text
processingMode = TranscribeAndSummarize
summarizationProfile = ThoughtNote
```

Supported processing modes:

```text
TranscribeOnly
TranscribeAndSummarize
```

Supported summarization profiles:

```text
ThoughtNote
SalesCall
SupportCall
IntakeCall
```

## Artifact Outputs

For `TranscribeOnly`:

```text
audio
transcript
metadata
```

For `TranscribeAndSummarize`:

```text
audio
transcript
note
metadata
```

In Azure, artifacts are written to private blobs under:

```text
sessions/{sessionId}/audio/
sessions/{sessionId}/transcripts/
sessions/{sessionId}/notes/
sessions/{sessionId}/metadata/
```

The API returns artifact references, not public blob access.
