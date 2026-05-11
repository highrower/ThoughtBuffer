using System.Text.Json;
using ThoughtBuffer.Formatting;
using ThoughtBuffer.Models;
using ThoughtBuffer.Services;

namespace ThoughtBuffer.Application;

public sealed class IngestionPipeline(
    ITranscriptionService transcriber,
    ISummarizationService summarizer) : IIngestionPipeline
{
    public async Task<IReadOnlyList<RecordingEntry>> ProcessLocalAudioFilesAsync(
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

        foreach (var entry in imported)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var audioPath = entry.FilteredPath
                ?? throw new InvalidOperationException($"Filtered path missing for {entry.FileName}");

            var outputBaseName = Path.GetFileNameWithoutExtension(entry.FileName)
                .Replace(".trimmed", "", StringComparison.OrdinalIgnoreCase);

            var transcriptPath = Path.Combine(paths.transcriptFolder, $"{outputBaseName}.txt");
            var notePath = Path.Combine(paths.notesFolder, $"{outputBaseName}.md");

            if (File.Exists(transcriptPath) && File.Exists(notePath))
            {
                Console.WriteLine($"Skipping already transcribed/summarized file: {entry.FileName}");
                continue;
            }

            var fileInfo = new FileInfo(audioPath);
            Console.WriteLine($"Uploading for transcription: {audioPath}");
            Console.WriteLine($"Size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB");

            var transcript = await transcriber.TranscribeAsync(audioPath, cancellationToken);
            Console.WriteLine($"Transcript for {entry.FileName}:");
            Console.WriteLine(transcript);

            await File.WriteAllTextAsync(transcriptPath, transcript, cancellationToken);

            var summary = await summarizer.SummarizeAsync(transcript, cancellationToken);

            var markdown = MarkdownNoteBuilder.Build(entry, summary, transcript);
            await File.WriteAllTextAsync(notePath, markdown, cancellationToken);
        }

        var json = JsonSerializer.Serialize(imported, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        var jsonPath = Path.Combine(paths.appFolder, "recordings.json");
        await File.WriteAllTextAsync(jsonPath, json, cancellationToken);

        return imported;
    }
}
