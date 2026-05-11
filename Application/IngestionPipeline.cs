using System.Text.Json;
using ThoughtBuffer.Formatting;
using ThoughtBuffer.Models;
using ThoughtBuffer.Services;
using ThoughtBuffer.Storage;

namespace ThoughtBuffer.Application;

public sealed class IngestionPipeline(
    ITranscriptionService transcriber,
    ISummarizationService summarizer,
    IArtifactStorage? artifactStorage = null) : IIngestionPipeline
{
    public Task<IReadOnlyList<IngestionPipelineResult>> ProcessBatchAudioAsync(
        BatchIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Session.Mode != IngestionMode.AudioFile)
            throw new InvalidOperationException("Batch audio processing requires an audio file ingestion session.");

        return ProcessBatchAudioCoreAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<IngestionPipelineResult>> ProcessLocalAudioFilesAsync(
        IngestionSession session,
        IReadOnlyList<AudioAsset> audioAssets,
        AppPaths paths,
        IngestionProcessingOptions? processingOptions = null,
        CancellationToken cancellationToken = default)
    {
        var request = new BatchIngestionRequest(
            session,
            audioAssets.Select(asset => new BatchAudioInput(asset)).ToList(),
            processingOptions ?? new IngestionProcessingOptions(),
            paths.transcriptFolder,
            paths.notesFolder,
            Path.Combine(paths.appFolder, "recordings.json")
        );

        return await ProcessBatchAudioAsync(request, cancellationToken);
    }

    async Task<IReadOnlyList<IngestionPipelineResult>> ProcessBatchAudioCoreAsync(
        BatchIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var imported = request.AudioInputs
            .Select(input => ToRecordingEntry(input.AudioAsset))
            .ToList();
        var results = new List<IngestionPipelineResult>();

        foreach (var entry in imported)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var audioPath = entry.FilteredPath
                ?? throw new InvalidOperationException($"Filtered path missing for {entry.FileName}");

            var outputBaseName = Path.GetFileNameWithoutExtension(entry.FileName)
                .Replace(".trimmed", "", StringComparison.OrdinalIgnoreCase);

            var transcriptPath = request.LegacyTranscriptFolder is null
                ? null
                : Path.Combine(request.LegacyTranscriptFolder, $"{outputBaseName}.txt");
            var notePath = request.LegacyNotesFolder is null
                ? null
                : Path.Combine(request.LegacyNotesFolder, $"{outputBaseName}.md");
            var sessionArtifactRoot = $"sessions/{request.Session.Id}";

            if (artifactStorage is null && transcriptPath is not null && notePath is not null && File.Exists(transcriptPath) && File.Exists(notePath))
            {
                Console.WriteLine($"Skipping already transcribed/summarized file: {entry.FileName}");
                results.Add(new IngestionPipelineResult(entry, transcriptPath, notePath, null, null));
                continue;
            }

            var fileInfo = new FileInfo(audioPath);
            Console.WriteLine($"Uploading for transcription: {audioPath}");
            Console.WriteLine($"Size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB");

            var transcript = await transcriber.TranscribeAsync(audioPath, cancellationToken);
            Console.WriteLine($"Transcript generated for {entry.FileName}.");

            SummaryResult? summary = null;
            string? markdown = null;
            if (request.ProcessingOptions.ProcessingMode == ProcessingMode.TranscribeAndSummarize)
            {
                summary = await summarizer.SummarizeAsync(
                    transcript,
                    request.ProcessingOptions.SummarizationProfile,
                    cancellationToken);

                markdown = MarkdownNoteBuilder.Build(entry, summary, transcript);
            }

            ArtifactWriteResult? audioArtifact = null;
            ArtifactWriteResult? transcriptArtifact = null;
            ArtifactWriteResult? noteArtifact = null;

            if (artifactStorage is null)
            {
                if (transcriptPath is null)
                    throw new InvalidOperationException("Legacy transcript path is required when artifact storage is not configured.");

                await File.WriteAllTextAsync(transcriptPath, transcript, cancellationToken);
                if (markdown is not null)
                {
                    if (notePath is null)
                        throw new InvalidOperationException("Legacy note path is required when artifact storage is not configured.");

                    await File.WriteAllTextAsync(notePath, markdown, cancellationToken);
                }
            }
            else
            {
                audioArtifact = await artifactStorage.SaveFileAsync(
                    ArtifactKind.Audio,
                    $"{sessionArtifactRoot}/audio/{entry.FileName}",
                    audioPath,
                    cancellationToken);

                transcriptArtifact = await artifactStorage.SaveTextAsync(
                    ArtifactKind.Transcript,
                    $"{sessionArtifactRoot}/transcripts/{outputBaseName}.txt",
                    transcript,
                    cancellationToken);

                if (markdown is not null)
                {
                    noteArtifact = await artifactStorage.SaveTextAsync(
                        ArtifactKind.Note,
                        $"{sessionArtifactRoot}/notes/{outputBaseName}.md",
                        markdown,
                        cancellationToken);
                }
            }

            results.Add(new IngestionPipelineResult(
                entry,
                transcriptArtifact?.Path ?? transcriptPath,
                noteArtifact?.Path ?? (markdown is null ? null : notePath),
                new Transcript(
                    Guid.NewGuid().ToString("N"),
                    request.Session.Id,
                    entry.FileName,
                    transcript,
                    DateTime.UtcNow
                ),
                summary,
                audioArtifact,
                transcriptArtifact,
                noteArtifact
            ));
        }

        var metadata = new
        {
            session = request.Session,
            processingOptions = request.ProcessingOptions,
            recordings = imported
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        if (artifactStorage is null)
        {
            if (request.LegacyMetadataPath is null)
                throw new InvalidOperationException("Legacy metadata path is required when artifact storage is not configured.");

            await File.WriteAllTextAsync(request.LegacyMetadataPath, json, cancellationToken);
        }
        else
        {
            var metadataArtifact = await artifactStorage.SaveTextAsync(
                ArtifactKind.Metadata,
                $"sessions/{request.Session.Id}/metadata/recording.json",
                json,
                cancellationToken);

            results = results
                .Select(result => result with { MetadataArtifact = metadataArtifact })
                .ToList();
        }

        return results;
    }

    static RecordingEntry ToRecordingEntry(AudioAsset asset) =>
        new(
            asset.FileName,
            asset.OriginalPath,
            asset.StoredPath,
            asset.FilteredPath,
            asset.SizeBytes,
            asset.LastWriteTimeUtc,
            asset.ImportedAtUtc
        );
}
