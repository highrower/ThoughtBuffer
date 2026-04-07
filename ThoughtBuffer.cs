using System.Text.Json;
using ThoughtBuffer.Formatting;
using ThoughtBuffer.Models;
using ThoughtBuffer.Services;

namespace ThoughtBuffer;

public class ThoughtBuffer
{
    static string[] _files = [];

    public static async Task Main(
        string? choice,
        AppPaths paths,
        IAudioFilterService filtrationService,
        ITranscriptionService transcriber,
        ISummarizationService summarizer)
    {
        if (GetChoice(choice) == 'Q')
            return;

        _files = Directory.GetFiles(paths.recordingsPath, "*.mp3");

        switch (GetChoice(choice))
        {
            case '1':
                await FilterFiles(paths, filtrationService);
                break;

            case '2':
                await TranscribeAndSummarize(paths, transcriber, summarizer, filtered: true);
                break;

            case '3':
                await FilterFiles(paths, filtrationService);
                await TranscribeAndSummarize(paths, transcriber, summarizer, filtered: true);
                break;

            default:
                return;
        }

        if (!Directory.Exists(paths.recordingsPath) || !Directory.Exists(paths.archivePath))
            throw new DirectoryNotFoundException("Recorder or Archive not found.");
    }

    static char GetChoice(string? choice) =>
        choice is "1" or "2" or "3" ? choice[0] : 'Q';

    static async Task FilterFiles(AppPaths paths, IAudioFilterService filterer)
    {
        foreach (var file in _files)
        {
            var baseName = Path.GetFileNameWithoutExtension(file);
            var destinationPath = Path.Combine(paths.filteredFolder, $"{baseName}.trimmed.wav");

            var filteredPath = await filterer.FilterFile(file, destinationPath);
            Console.WriteLine($"Filtered {file} to {filteredPath}");
        }
    }

static async Task TranscribeAndSummarize(
    AppPaths paths,
    ITranscriptionService transcriber,
    ISummarizationService summarizer,
    bool filtered)
{
    var imported = new List<RecordingEntry>();

    string[] sourceFiles = filtered
        ? Directory.GetFiles(paths.filteredFolder, "*.wav")
        : Directory.GetFiles(paths.recordingsPath, "*.mp3");

    foreach (var file in sourceFiles)
    {
        var        fileName = Path.GetFileName(file);
        var        info     = new FileInfo(file);
        const long maxBytes = 25 * 1024 * 1024;

        if (info.Length > maxBytes)
        {
            throw new InvalidOperationException(
                $"Audio file is {info.Length / (1024.0 * 1024.0):F2} MB, which exceeds the 25 MB Audio API limit.");
        }

        string? copiedPath = null;
        string? filteredPath = null;

        if (filtered)
        {
            filteredPath = file;
        }
        else
        {
            copiedPath = Path.Combine(paths.copyFileFolder, fileName);
            File.Copy(file, copiedPath, true);
            Console.WriteLine($"Copied {fileName} to {copiedPath}");
        }

        imported.Add(new RecordingEntry(
            fileName,
            file,
            copiedPath,
            filteredPath,
            info.Length,
            info.LastWriteTimeUtc,
            DateTime.UtcNow
        ));
    }

    foreach (var entry in imported)
    {
        string audioPath = filtered
            ? entry.FilteredPath ?? throw new InvalidOperationException($"Filtered path missing for {entry.FileName}")
            : entry.CopiedPath ?? throw new InvalidOperationException($"Copied path missing for {entry.FileName}");

        var        fileInfo = new FileInfo(audioPath);
        const long maxBytes = 25 * 1024 * 1024;

        if (fileInfo.Length > maxBytes)
        {
            throw new InvalidOperationException(
                $"Audio file is {fileInfo.Length / (1024.0 * 1024.0):F2} MB, which exceeds the 25 MB Audio API limit.");
        }
        
        Console.WriteLine($"Uploading for transcription: {audioPath}");
        Console.WriteLine($"Size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB");
        var transcript = await transcriber.TranscribeAsync(audioPath);
        Console.WriteLine($"Transcript for {entry.FileName}:");
        Console.WriteLine(transcript);

        var baseName = Path.GetFileNameWithoutExtension(entry.FileName);

        var transcriptPath = Path.Combine(paths.transcriptFolder, $"{baseName}.txt");
        await File.WriteAllTextAsync(transcriptPath, transcript);

        var summary = await summarizer.SummarizeAsync(transcript);

        var notePath = Path.Combine(paths.notesFolder, $"{baseName}.md");
        var markdown = MarkdownNoteBuilder.Build(entry, summary, transcript);
        await File.WriteAllTextAsync(notePath, markdown);
    }

    var json = JsonSerializer.Serialize(imported, new JsonSerializerOptions
    {
        WriteIndented = true,
    });

    var jsonPath = Path.Combine(paths.appFolder, "recordings.json");
    await File.WriteAllTextAsync(jsonPath, json);
}
}