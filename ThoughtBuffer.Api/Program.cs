using ThoughtBuffer.Api.Contracts;
using ThoughtBuffer.Application;
using ThoughtBuffer.Models;
using ThoughtBuffer.Options;
using ThoughtBuffer.Services;
using ThoughtBuffer.Storage;

var allowedAudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".wav",
    ".mp3",
    ".m4a",
    ".mp4",
    ".mpeg",
    ".mpga",
    ".webm"
};

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton(_ => CreateApiPaths(builder.Configuration.GetLocalStorageOptions()));
builder.Services.AddSingleton(_ => CreateArtifactStorage(builder.Configuration));
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
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ThoughtBuffer.Api.Startup");
var startupPaths = app.Services.GetRequiredService<AppPaths>();
var startupStorageOptions = builder.Configuration.GetLocalStorageOptions();
var startupArtifactOptions = builder.Configuration.GetArtifactStorageOptions();
var startupOpenAiOptions = ResolveOpenAiOptionsWithoutThrowing(builder.Configuration);

startupLogger.LogInformation(
    "ThoughtBuffer.Api started in {EnvironmentName}. Local storage root configured: {LocalStorageRootConfigured}. Max upload bytes: {MaxUploadBytes}.",
    app.Environment.EnvironmentName,
    !string.IsNullOrWhiteSpace(startupStorageOptions.RootPath),
    startupStorageOptions.MaxUploadBytes);

if (string.IsNullOrWhiteSpace(startupOpenAiOptions.ApiKey))
{
    startupLogger.LogWarning("OpenAI API key is not configured. Ingestion requests will fail until OpenAI:ApiKey or THOUGHT_BUFFER_OPENAI_KEY is set.");
}

startupLogger.LogInformation(
    "Artifact storage provider is {ArtifactStorageProvider}. Local temp storage root is {StorageRoot}.",
    startupArtifactOptions.Provider,
    startupPaths.appFolder);

app.MapGet("/", () => Results.Ok(new
{
    name = "ThoughtBuffer.Api",
    endpoints = new[] { "GET /health", "GET /api/config/status", "POST /api/ingestions/audio" }
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy"
}));

app.MapGet("/api/config/status", (IWebHostEnvironment environment) =>
{
    var openAiOptions = ResolveOpenAiOptionsWithoutThrowing(builder.Configuration);
    var storageOptions = builder.Configuration.GetLocalStorageOptions();
    var artifactStorageOptions = builder.Configuration.GetArtifactStorageOptions();

    return Results.Ok(new ConfigStatusResponse(
        !string.IsNullOrWhiteSpace(openAiOptions.ApiKey),
        !string.IsNullOrWhiteSpace(storageOptions.RootPath),
        storageOptions.MaxUploadBytes,
        environment.EnvironmentName,
        artifactStorageOptions.Provider,
        IsArtifactStorageConfigured(artifactStorageOptions),
        artifactStorageOptions.ContainerName
    ));
});

app.MapPost("/api/ingestions/audio", async (
    HttpRequest request,
    IServiceProvider services,
    AppPaths paths,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    logger.LogInformation("Audio upload request received.");

    if (!request.HasFormContentType)
    {
        logger.LogWarning("Audio upload validation failed: request content type was not multipart/form-data.");
        return Results.BadRequest(new ApiErrorResponse(
            "Expected multipart/form-data with an audio file.",
            "invalid_request"
        ));
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    var processingOptionsResult = ParseProcessingOptions(form, logger);

    if (processingOptionsResult.Error is not null)
        return Results.BadRequest(processingOptionsResult.Error);

    var processingOptions = processingOptionsResult.Options;

    if (file is null || file.Length == 0)
    {
        logger.LogWarning("Audio upload validation failed: missing or empty file.");
        return Results.BadRequest(new ApiErrorResponse(
            "No uploaded audio file was found.",
            "invalid_request"
        ));
    }

    var storageOptions = builder.Configuration.GetLocalStorageOptions();

    if (file.Length > storageOptions.MaxUploadBytes)
    {
        logger.LogWarning(
            "Audio upload validation failed: file size {FileSize} exceeded max upload bytes {MaxUploadBytes}.",
            file.Length,
            storageOptions.MaxUploadBytes);

        return Results.BadRequest(new ApiErrorResponse(
            $"Uploaded file exceeds the configured limit of {storageOptions.MaxUploadBytes} bytes.",
            "invalid_file"
        ));
    }

    var originalFileName = Path.GetFileName(file.FileName);
    var extension = Path.GetExtension(originalFileName);

    if (string.IsNullOrWhiteSpace(extension) || !allowedAudioExtensions.Contains(extension))
    {
        logger.LogWarning(
            "Audio upload validation failed: unsupported extension {Extension}.",
            extension);

        return Results.BadRequest(new ApiErrorResponse(
            "Unsupported audio file extension. Allowed extensions: .wav, .mp3, .m4a, .mp4, .mpeg, .mpga, .webm.",
            "unsupported_media_type"
        ));
    }

    var session = new IngestionSession(
        Guid.NewGuid().ToString("N"),
        IngestionMode.AudioFile,
        SourceSystem.ManualUpload,
        DateTime.UtcNow,
        DisplayName: originalFileName
    );

    var safeFileName = SanitizeFileName(originalFileName);
    var storedFileName = $"{session.Id}-{safeFileName}";
    var storedPath = Path.GetFullPath(Path.Combine(paths.copyFileFolder, storedFileName));
    var uploadRoot = Path.GetFullPath(paths.copyFileFolder);

    if (!storedPath.StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning("Audio upload validation failed: sanitized path escaped upload root.");
        return Results.BadRequest(new ApiErrorResponse(
            "Invalid uploaded file name.",
            "invalid_file"
        ));
    }

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

    try
    {
        logger.LogInformation(
            "Ingestion started for session {SessionId}. Original file: {OriginalFileName}. Stored file: {StoredFileName}. Size bytes: {FileSize}.",
            session.Id,
            originalFileName,
            storedFileName,
            fileInfo.Length);

        var pipeline = services.GetRequiredService<IIngestionPipeline>();
        var results = await pipeline.ProcessLocalAudioFilesAsync(
            session,
            new[] { audioAsset },
            paths,
            processingOptions,
            cancellationToken);

        logger.LogInformation(
            "Ingestion completed for session {SessionId}. File count: {FileCount}.",
            session.Id,
            results.Count);

        return Results.Ok(new AudioIngestionResponse(
            session.Id,
            session.Source.ToString(),
            "completed",
            processingOptions.ProcessingMode.ToString(),
            processingOptions.SummarizationProfile.ToString(),
            results.Select(result => new AudioIngestionFileResult(
                originalFileName,
                result.Recording.FileName,
                result.Transcript?.Text,
                result.Summary,
                result.TranscriptArtifact?.Path ?? result.TranscriptPath,
                result.NoteArtifact?.Path ?? result.NotePath,
                ToArtifactReference(result.AudioArtifact),
                ToArtifactReference(result.TranscriptArtifact),
                ToArtifactReference(result.NoteArtifact),
                ToArtifactReference(result.MetadataArtifact)
            )).ToList()
        ));
    }
    catch (InvalidOperationException ex)
    {
        logger.LogError(ex, "Ingestion failed for session {SessionId}.", session.Id);
        return Results.Json(
            new ApiErrorResponse(
                "The audio file could not be transcribed or summarized. Check API configuration and service availability.",
                "ingestion_failed"
            ),
            statusCode: StatusCodes.Status500InternalServerError);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected ingestion failure for session {SessionId}.", session.Id);
        return Results.Json(
            new ApiErrorResponse(
                "The audio ingestion request failed.",
                "ingestion_failed"
            ),
            statusCode: StatusCodes.Status500InternalServerError);
    }
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
    var options = ResolveOpenAiOptionsWithoutThrowing(configuration);

    if (string.IsNullOrWhiteSpace(options.ApiKey))
        throw new InvalidOperationException("OpenAI API key not found. Set OpenAI:ApiKey or THOUGHT_BUFFER_OPENAI_KEY.");

    return options;
}

static OpenAiOptions ResolveOpenAiOptionsWithoutThrowing(IConfiguration configuration)
{
    var options = configuration.GetOpenAiOptions();

    if (string.IsNullOrWhiteSpace(options.ApiKey))
        options.ApiKey = Environment.GetEnvironmentVariable("THOUGHT_BUFFER_OPENAI_KEY") ?? "";

    return options;
}

static IArtifactStorage CreateArtifactStorage(IConfiguration configuration)
{
    var options = configuration.GetArtifactStorageOptions();

    if (options.Provider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase))
        return new AzureBlobArtifactStorage(options.ConnectionString, options.ContainerName);

    if (!options.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Unsupported artifact storage provider: {options.Provider}");

    return new LocalFileArtifactStorage(options.GetLocalRootPath("Api"));
}

static bool IsArtifactStorageConfigured(ArtifactStorageOptions options)
{
    if (options.Provider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase))
        return !string.IsNullOrWhiteSpace(options.ConnectionString)
               && !string.IsNullOrWhiteSpace(options.ContainerName);

    if (options.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        return true;

    return false;
}

static ArtifactReferenceResponse? ToArtifactReference(ArtifactWriteResult? result) =>
    result is null
        ? null
        : new ArtifactReferenceResponse(
            result.Kind.ToString(),
            result.StorageProvider,
            result.Path,
            result.Uri?.ToString()
        );

static ProcessingOptionsParseResult ParseProcessingOptions(IFormCollection form, ILogger logger)
{
    var processingModeValue = form["processingMode"].FirstOrDefault();
    var summarizationProfileValue = form["summarizationProfile"].FirstOrDefault();

    if (!TryParseEnumOrDefault(
            processingModeValue,
            ProcessingMode.TranscribeAndSummarize,
            out ProcessingMode processingMode))
    {
        logger.LogWarning("Audio upload validation failed: unsupported processingMode {ProcessingMode}.", processingModeValue);
        return new ProcessingOptionsParseResult(
            new IngestionProcessingOptions(),
            new ApiErrorResponse(
                "Unsupported processingMode. Allowed values: TranscribeOnly, TranscribeAndSummarize.",
                "invalid_processing_options"
            ));
    }

    if (!TryParseEnumOrDefault(
            summarizationProfileValue,
            SummarizationProfile.ThoughtNote,
            out SummarizationProfile summarizationProfile))
    {
        logger.LogWarning("Audio upload validation failed: unsupported summarizationProfile {SummarizationProfile}.", summarizationProfileValue);
        return new ProcessingOptionsParseResult(
            new IngestionProcessingOptions(),
            new ApiErrorResponse(
                "Unsupported summarizationProfile. Allowed values: ThoughtNote, SalesCall, SupportCall, IntakeCall.",
                "invalid_processing_options"
            ));
    }

    return new ProcessingOptionsParseResult(
        new IngestionProcessingOptions(processingMode, summarizationProfile),
        null);
}

static bool TryParseEnumOrDefault<TEnum>(string? value, TEnum defaultValue, out TEnum result)
    where TEnum : struct, Enum
{
    if (string.IsNullOrWhiteSpace(value))
    {
        result = defaultValue;
        return true;
    }

    return Enum.TryParse(value, ignoreCase: true, out result);
}

static string SanitizeFileName(string fileName)
{
    var safeName = Path.GetFileName(fileName);
    foreach (var invalidChar in Path.GetInvalidFileNameChars())
    {
        safeName = safeName.Replace(invalidChar, '-');
    }

    return string.IsNullOrWhiteSpace(safeName)
        ? "audio-upload"
        : safeName;
}

record ProcessingOptionsParseResult(
    IngestionProcessingOptions Options,
    ApiErrorResponse? Error
);
