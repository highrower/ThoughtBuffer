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
    public async Task<IReadOnlyList<IngestionPipelineResult>> ProcessLocalAudioFilesAsync(
        IngestionSession session,
        IReadOnlyList<AudioAsset> audioAssets,
        AppPaths paths,
        CancellationToken cancellationToken = default)
    {
        if (session.Mode != IngestionMode.AudioFile)
            throw new InvalidOperationException("Local audio processing requires an audio file ingestion session.");

        var imported = audioAssets
            .Select(asset => new RecordingEntry(
                asset.FileName,
                asset.OriginalPath,
                asset.StoredPath,
                asset.FilteredPath,
                asset.SizeBytes,
                asset.LastWriteTimeUtc,
                asset.ImportedAtUtc
            ))
            .ToList();
        var results = new List<IngestionPipelineResult>();

        foreach (var entry in imported)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var audioPath = entry.FilteredPath
                ?? throw new InvalidOperationException($"Filtered path missing for {entry.FileName}");

            var outputBaseName = Path.GetFileNameWithoutExtension(entry.FileName)
                .Replace(".trimmed", "", StringComparison.OrdinalIgnoreCase);

            var transcriptPath = Path.Combine(paths.transcriptFolder, $"{outputBaseName}.txt");
            var notePath = Path.Combine(paths.notesFolder, $"{outputBaseName}.md");
            var sessionArtifactRoot = $"sessions/{session.Id}";

            if (artifactStorage is null && File.Exists(transcriptPath) && File.Exists(notePath))
            {
                Console.WriteLine($"Skipping already transcribed/summarized file: {entry.FileName}");
                results.Add(new IngestionPipelineResult(entry, transcriptPath, notePath, null, null));
                continue;
            }

            var fileInfo = new FileInfo(audioPath);
            Console.WriteLine($"Uploading for transcription: {audioPath}");
            Console.WriteLine($"Size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB");

            var transcript = await transcriber.TranscribeAsync(audioPath, cancellationToken);
            Console.WriteLine($"Transcript for {entry.FileName}:");
            Console.WriteLine(transcript);

            var summary = await summarizer.SummarizeAsync(transcript, cancellationToken);

            var markdown = MarkdownNoteBuilder.Build(entry, summary, transcript);
            ArtifactWriteResult? audioArtifact = null;
            ArtifactWriteResult? transcriptArtifact = null;
            ArtifactWriteResult? noteArtifact = null;

            if (artifactStorage is null)
            {
                await File.WriteAllTextAsync(transcriptPath, transcript, cancellationToken);
                await File.WriteAllTextAsync(notePath, markdown, cancellationToken);
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

                noteArtifact = await artifactStorage.SaveTextAsync(
                    ArtifactKind.Note,
                    $"{sessionArtifactRoot}/notes/{outputBaseName}.md",
                    markdown,
                    cancellationToken);
            }

            results.Add(new IngestionPipelineResult(
                entry,
                transcriptArtifact?.Path ?? transcriptPath,
                noteArtifact?.Path ?? notePath,
                new Transcript(
                    Guid.NewGuid().ToString("N"),
                    session.Id,
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

        var json = JsonSerializer.Serialize(imported, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        var jsonPath = Path.Combine(paths.appFolder, "recordings.json");
        if (artifactStorage is null)
        {
            await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
        }
        else
        {
            var metadataArtifact = await artifactStorage.SaveTextAsync(
                ArtifactKind.Metadata,
                $"sessions/{session.Id}/metadata/recording.json",
                json,
                cancellationToken);

            results = results
                .Select(result => result with { MetadataArtifact = metadataArtifact })
                .ToList();
        }

        return results;
    }
}
