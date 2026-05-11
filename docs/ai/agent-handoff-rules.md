GOAL:
Refactor ThoughtBuffer from a local recorder CLI into a generic ingestion/transcription/summarization pipeline that can support local files now and Twilio recordings/live streams later.

CURRENT STATE:
- Repo: highrower/ThoughtBuffer
- Console app.
- Program.cs hardcodes local recorder paths and manually wires services.
- ThoughtBuffer.cs scans local WAV/MP3 files, optionally filters audio, copies files, transcribes, summarizes, writes transcript .txt, markdown note, and recordings.json.
- Existing service interfaces:
    - ITranscriptionService.TranscribeAsync(string audioFilePath)
    - ISummarizationService.SummarizeAsync(string transcript)
    - IAudioFilterService exists.
- Existing metadata is RecordingEntry, which is file-centric.
- Existing path config is AppPaths, which is local-filesystem-centric.

DESIRED DESIGN:
Make IngestionSession the central domain concept.

Add:
- IngestionSession
- IngestionMode enum: Text, AudioFile, AudioUrl, LiveAudioStream
- SourceSystem enum: ThoughtBuffer, LocalRecorder, ManualUpload, TwilioRecording, TwilioMediaStream, BrowserMic, WhatsApp
- AudioAsset
- Transcript
- TranscriptSegment
- AudioChunk
- IngestionPipeline

Keep current CLI working by wrapping the existing local recorder flow into a LocalRecorder ingestion adapter.

DO NOT:
- Do not make Twilio the core domain.
- Do not implement live streaming first.
- Do not remove current local workflow until equivalent pipeline behavior exists.
- Do not hardcode Azure/Twilio/OpenAI secrets.
- Do not mix batch recording and live stream processing into the same class.

PHASE 1:
Refactor current code into pipeline shape without changing external behavior.

Steps:
1. Create Domain/Models:
    - IngestionSession
    - IngestionMode
    - SourceSystem
    - AudioAsset
    - Transcript
2. Create Application/IIngestionPipeline.cs.
3. Create Application/IngestionPipeline.cs.
4. Move the current logic from ThoughtBuffer.TranscribeAndSummarize into IngestionPipeline.ProcessAudioAsync.
5. Keep Program.cs as CLI entrypoint, but make it call the pipeline.
6. Preserve current transcript and markdown outputs.
7. Keep RecordingEntry temporarily as compatibility metadata for MarkdownNoteBuilder.
8. Add cancellation token threading where missing.

PHASE 2:
Introduce artifact storage abstraction.

Steps:
1. Add IAudioStorage, ITranscriptStorage, ISummaryStorage.
2. Implement LocalFileArtifactStorage using current AppPaths folders.
3. Replace direct File.WriteAllTextAsync calls in pipeline with storage interfaces.
4. Keep local filesystem behavior identical.

PHASE 3:
Prepare hosted API.

Steps:
1. Add ThoughtBuffer.Api project.
2. Add /api/ingestions endpoint for audio upload/text input.
3. Add dependency injection registrations.
4. Move hardcoded config to appsettings/environment variables.
5. Prepare for Azure App Service or Azure Container Apps.

PHASE 4:
Add Twilio batch recording support.

Steps:
1. Add TwilioRecordingWebhookController.
2. Validate webhook signature.
3. Accept recording completed webhook.
4. Create IngestionSession with SourceSystem.TwilioRecording and ExternalId = RecordingSid/CallSid.
5. Download recording from Twilio using configured credentials.
6. Store raw audio through IAudioStorage.
7. Queue or directly run transcription depending on hosting stage.
8. Save transcript and summary through storage abstractions.

PHASE 5:
Add Twilio live stream support after batch path works.

Steps:
1. Add WebSocket endpoint for Twilio media stream.
2. Parse connected/media/stopped events.
3. Map stream SID/call SID to LiveIngestionSession.
4. Decode audio chunks.
5. Forward chunks to IStreamingTranscriptionService.
6. Store partial transcript events.
7. On stopped event, finalize transcript and enqueue summarization.

ACCEPTANCE CRITERIA FOR PHASE 1:
- Existing local recorder flow still works.
- CLI menu still supports filter/transcribe/summarize options.
- Transcription and summarization still use existing OpenAI services.
- Output transcript files and markdown notes are still produced.
- Core processing no longer depends directly on local recorder path scanning.
- New pipeline accepts an audio input/session abstraction.

ACCEPTANCE CRITERIA FOR PHASE 4:
- Twilio recording webhook can create an ingestion session.
- Recording can be downloaded and stored.
- Transcript and summary are generated using the same pipeline as local files.
- Twilio-specific code is isolated under Integrations/Twilio or Infrastructure/Twilio.

ARCHITECTURAL NORTH STAR:
Input source adapters should normalize everything into IngestionSession + AudioAsset/TextInput. The pipeline should not know whether the source was a Sony recorder, Twilio, browser upload, or future integration.