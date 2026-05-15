using ThoughtBuffer.Application;
using ThoughtBuffer.Models;
using ThoughtBuffer.Services;

namespace ThoughtBuffer;

public class ThoughtBuffer
{
    static string[] _sourceMp3Files = [];

    public static async Task Main(
        string? choice,
        AppPaths paths,
        IAudioFilterService filtrationService,
        ITranscriptionService transcriber,
        ISummarizationService summarizer,
        CancellationToken cancellationToken = default)
    {
        if (GetChoice(choice) == 'Q')
            return;

        if (!Directory.Exists(paths.recordingsPath) || !Directory.Exists(paths.archivePath))
            throw new DirectoryNotFoundException("Recorder or Archive not found.");

        _sourceMp3Files = Directory.GetFiles(paths.recordingsPath, "*.mp3");

        switch (GetChoice(choice))
        {
            case '1':
                await FilterFiles(paths, filtrationService, cancellationToken);
                break;

            case '2':
                await TranscribeAndSummarize(paths, transcriber, summarizer, cancellationToken);
                break;

            case '3':
                await FilterFiles(paths, filtrationService, cancellationToken);
                await TranscribeAndSummarize(paths, transcriber, summarizer, cancellationToken);
                break;

            default:
                return;
        }
    }

    static char GetChoice(string? choice) =>
        choice is "1" or "2" or "3" ? choice[0] : 'Q';

    static async Task FilterFiles(
        AppPaths paths,
        IAudioFilterService filterer,
        CancellationToken cancellationToken = default)
    {
        foreach (var file in _sourceMp3Files)
        {
            var baseName = Path.GetFileNameWithoutExtension(file);
            var destinationPath = Path.Combine(paths.filteredFolder, $"{baseName}.trimmed.mp3");

            if (File.Exists(destinationPath))
            {
                Console.WriteLine($"Skipping already filtered file: {destinationPath}");
                continue;
            }

            var filteredPath = await filterer.FilterFile(file, destinationPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(filteredPath))
            {
                Console.WriteLine($"Skipped {file} because no speech was detected.");
                continue;
            }

            Console.WriteLine($"Filtered {file} to {filteredPath}");
        }
    }

    static async Task TranscribeAndSummarize(
        AppPaths paths,
        ITranscriptionService transcriber,
        ISummarizationService summarizer,
        CancellationToken cancellationToken = default)
    {
        var filteredFiles = Directory.GetFiles(paths.filteredFolder, "*.trimmed.mp3");

        if (filteredFiles.Length == 0)
            throw new InvalidOperationException("No filtered .trimmed.mp3 files were found. Run option 1 first.");

        var session = new IngestionSession(
            Guid.NewGuid().ToString("N"),
            IngestionMode.AudioFile,
            SourceSystem.LocalRecorder,
            DateTime.UtcNow,
            DisplayName: "Local recorder batch"
        );

        var audioAssets = new List<AudioAsset>();

        foreach (var file in filteredFiles)
        {
            var fileName = Path.GetFileName(file);
            var info = new FileInfo(file);
            const long maxBytes = 25 * 1024 * 1024;

            if (info.Length > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Audio file is {info.Length / (1024.0 * 1024.0):F2} MB, which exceeds the 25 MB Audio API limit.");
            }

            audioAssets.Add(new AudioAsset(
                Guid.NewGuid().ToString("N"),
                session.Id,
                fileName,
                file,
                null,
                file,
                info.Length,
                info.LastWriteTimeUtc,
                DateTime.UtcNow
            ));
        }

        IIngestionPipeline pipeline = new IngestionPipeline(transcriber, summarizer);
        await pipeline.ProcessLocalAudioFilesAsync(
            session,
            audioAssets,
            paths,
            new IngestionProcessingOptions(),
            cancellationToken);
    }
}
