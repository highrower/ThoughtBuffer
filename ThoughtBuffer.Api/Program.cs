using ThoughtBuffer.Application;
using ThoughtBuffer.Models;
using ThoughtBuffer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton(CreateApiPaths);
builder.Services.AddScoped<ITranscriptionService>(_ => new OpenAiTranscriptionService(GetOpenAiApiKey()));
builder.Services.AddScoped<ISummarizationService>(_ => new OpenAiSummarizationService(GetOpenAiApiKey()));
builder.Services.AddScoped<IIngestionPipeline, IngestionPipeline>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    name = "ThoughtBuffer.Api",
    endpoints = new[] { "POST /api/ingestions/audio" }
}));

app.MapPost("/api/ingestions/audio", async (
    HttpRequest request,
    IIngestionPipeline pipeline,
    AppPaths paths,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Expected multipart/form-data with an audio file." });

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No uploaded audio file was found." });

    var session = new IngestionSession(
        Guid.NewGuid().ToString("N"),
        IngestionMode.AudioFile,
        SourceSystem.ManualUpload,
        DateTime.UtcNow,
        DisplayName: Path.GetFileName(file.FileName)
    );

    var storedFileName = $"{session.Id}-{Path.GetFileName(file.FileName)}";
    var storedPath = Path.Combine(paths.copyFileFolder, storedFileName);

    await using (var stream = File.Create(storedPath))
    {
        await file.CopyToAsync(stream, cancellationToken);
    }

    var fileInfo = new FileInfo(storedPath);
    var audioAsset = new AudioAsset(
        Guid.NewGuid().ToString("N"),
        session.Id,
        storedFileName,
        storedPath,
        storedPath,
        storedPath,
        fileInfo.Length,
        fileInfo.LastWriteTimeUtc,
        DateTime.UtcNow,
        file.ContentType
    );

    var results = await pipeline.ProcessLocalAudioFilesAsync(
        session,
        new[] { audioAsset },
        paths,
        cancellationToken);

    return Results.Ok(new AudioIngestionResponse(
        session.Id,
        session.Source.ToString(),
        results.Select(result => new AudioIngestionFileResponse(
            result.Recording.FileName,
            result.TranscriptPath,
            result.NotePath,
            result.Transcript?.Text,
            result.Summary
        )).ToList()
    ));
});

app.Run();

static AppPaths CreateApiPaths(IServiceProvider _)
{
    var baseAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var appFolder = Path.Combine(baseAppData, "ThoughtBuffer", "Api");
    var recordingsPath = Path.Combine(appFolder, "Uploads");
    var copyFileFolder = Path.Combine(appFolder, "Uploads");
    var filteredFolder = Path.Combine(appFolder, "Filtered");
    var archivePath = Path.Combine(appFolder, "Archive");
    var transcriptFolder = Path.Combine(appFolder, "Transcripts");
    var notesFolder = Path.Combine(appFolder, "Notes");

    Directory.CreateDirectory(appFolder);
    Directory.CreateDirectory(recordingsPath);
    Directory.CreateDirectory(copyFileFolder);
    Directory.CreateDirectory(filteredFolder);
    Directory.CreateDirectory(archivePath);
    Directory.CreateDirectory(transcriptFolder);
    Directory.CreateDirectory(notesFolder);

    return new AppPaths(
        appFolder,
        recordingsPath,
        copyFileFolder,
        filteredFolder,
        archivePath,
        transcriptFolder,
        notesFolder
    );
}

static string GetOpenAiApiKey() =>
    Environment.GetEnvironmentVariable("THOUGHT_BUFFER_OPENAI_KEY")
    ?? throw new InvalidOperationException("API key not found in environment variables.");

public record AudioIngestionResponse(
    string SessionId,
    string Source,
    IReadOnlyList<AudioIngestionFileResponse> Files
);

public record AudioIngestionFileResponse(
    string FileName,
    string? TranscriptPath,
    string? NotePath,
    string? Transcript,
    SummaryResult? Summary
);
