Current architecture

Repo: highrower/ThoughtBuffer.

Current app is a C# console application with a hardcoded local recorder workflow. Program.cs reads the OpenAI key from THOUGHT_BUFFER_OPENAI_KEY, defines hardcoded local paths such as E:\REC_FILE, FOLDER01, Archive, and G:\Projects\Dotnet\ThoughtBuffer\, then manually constructs concrete services and calls ThoughtBuffer.Main(...).

Current menu options are:

- 1: filter down all audio
- 2: send filtered audio to be transcribed/summarized
- 3: 1 + 2
- Q: Quit

This is defined directly in Program.cs.

Current main workflow lives in ThoughtBuffer.cs. It scans paths.recordingsPath for *.wav, falls back to *.mp3, then branches based on the selected CLI option.

Current processing flow:

- Find local files
- → optionally filter files with IAudioFilterService
- → copy files into local app folder
- → create RecordingEntry metadata
- → transcribe each file
- → write transcript .txt
- → summarize transcript
- → write markdown note
- → write recordings.json

Current transcription interface:

```csharp
public interface ITranscriptionService
{
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default);
}
```

Confirmed in Services/ITranscriptionService.cs.

Current summarization interface:

```csharp
public interface ISummarizationService
{
    Task<Models.SummaryResult> SummarizeAsync(string transcript, CancellationToken cancellationToken = default);
}
```

Confirmed in Services/ISummarizationService.cs.

Current metadata model is file-centric:

```csharp
public record RecordingEntry(
    string FileName,
    string OriginalPath,
    string? CopiedPath,
    string? FilteredPath,
    long FileSize,
    DateTime LastWriteTimeUtc,
    DateTime ImportedAtUtc
);
```

Confirmed in Models/RecordingEntry.cs.

Current path model is local-filesystem-centric:

```csharp
public record AppPaths(
    string appFolder,
    string recordingsPath,
    string copyFileFolder,
    string filteredFolder,
    string archivePath,
    string transcriptFolder,
    string notesFolder
);
```

Confirmed in Models/AppPaths.cs.

Existing important files/classes found in repo include ThoughtBuffer.cs, Program.cs, Models/SummaryResult.cs, Services/ISummarizationService.cs, Services/ITranscriptionService.cs, Services/FfmpegAudioPreprocessor.cs, Services/OpenAiTranscriptionService.cs, Services/OpenAiSummarizationService.cs, Services/IAudioFilterService.cs, Models/RecordingEntry.cs, Models/AppPaths.cs, Formatting/MarkdownNoteBuilder.cs, and Services/PythonAudioFilterService.cs.

## Product Direction

ThoughtBuffer should not remain a console app long-term.

The target architecture is API-first:
- ASP.NET Core Web API as the primary host
- CLI/console behavior may remain temporarily for local development
- Current console workflow should be preserved only until equivalent API endpoints exist
- Future integrations like Twilio should call the API/webhook endpoints directly
- Azure hosting should target the API/worker shape, not the console app

The app should evolve from:
Local recorder CLI → generic ingestion pipeline → ASP.NET API → Azure-hosted API/worker → Twilio integrations.

Do not over-optimize the console app. Refactor it only enough to keep current behavior working while the API becomes the real entrypoint.

## Future Direction: Call Ingestion

ThoughtBuffer is evolving into a generic ingestion system for audio/text sessions.

The current manual upload endpoint is only the first source adapter. Future sources include:
- Twilio post-call recordings
- Twilio live media streams
- Browser uploads
- Local recorder ingestion
- Potential future voice/message integrations

The core domain should remain source-agnostic:
- IngestionSession is the center.
- SourceSystem identifies where content came from.
- AudioAsset / Transcript / Summary / Note are artifacts of a session.
- Twilio-specific behavior must stay in Twilio integration code, not the core pipeline.

Call ingestion will eventually support two-party conversations. The system should prepare for:
- caller/callee or customer/agent roles
- multi-channel or speaker-separated transcripts
- configurable summarization strategies
- optional summarization
- live transcription later
- batch transcription now

Do not assume every ingestion needs the same summary prompt.
Do not bake “thought note” summarization into the pipeline as the only mode.
The pipeline should allow processing options such as:
- transcribeOnly
- transcribeAndSummarize
- summarizationProfile
- sourceSystem
- externalId, such as Twilio CallSid or RecordingSid

For now, Blob Storage is the next step. SQL/Entity Framework is intentionally deferred until we need queryable call/customer metadata.