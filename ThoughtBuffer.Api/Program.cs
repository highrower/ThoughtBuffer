using ThoughtBuffer.Application;
using ThoughtBuffer.Models;
using ThoughtBuffer.Options;
using ThoughtBuffer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton(sp =>
{
    var options = builder.Configuration.GetLocalStorageOptions();

    return CreateApiPaths(options);
});
builder.Services.AddScoped<ITranscriptionService>(_ =>
{
    var options = ResolveOpenAiOptions(builder.Configuration);
    return new OpenAiTranscriptionService(options.ApiKey, options.TranscriptionModel);
});
builder.Services.AddScoped<ISummarizationService>(_ =>
{
    var options = ResolveOpenAiOptions(builder.Configuration);
    return new OpenAiSummarizationService(options.ApiKey, options.SummarizationModel);
});
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

static AppPaths CreateApiPaths(LocalStorageOptions options)
{
    var appPaths = options.ToAppPaths("Api");

    Directory.CreateDirectory(appPaths.appFolder);
    Directory.CreateDirectory(appPaths.recordingsPath);
    Directory.CreateDirectory(appPaths.copyFileFolder);
    Directory.CreateDirectory(appPaths.filteredFolder);
    Directory.CreateDirectory(appPaths.archivePath);
    Directory.CreateDirectory(appPaths.transcriptFolder);
    Directory.CreateDirectory(appPaths.notesFolder);

    return appPaths;
}

static OpenAiOptions ResolveOpenAiOptions(IConfiguration configuration)
{
    var options = configuration
        .GetOpenAiOptions();

    if (string.IsNullOrWhiteSpace(options.ApiKey))
        options.ApiKey = Environment.GetEnvironmentVariable("THOUGHT_BUFFER_OPENAI_KEY") ?? "";

    if (string.IsNullOrWhiteSpace(options.ApiKey))
        throw new InvalidOperationException("OpenAI API key not found. Set OpenAI:ApiKey or THOUGHT_BUFFER_OPENAI_KEY.");

    return options;
}

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
