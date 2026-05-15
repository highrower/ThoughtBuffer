using ThoughtBuffer.Api.Contracts;
using ThoughtBuffer.Application;
using ThoughtBuffer.Integrations.Twilio;
using ThoughtBuffer.Models;
using ThoughtBuffer.Options;
using ThoughtBuffer.Services;
using ThoughtBuffer.Storage;
using System.Security;
using System.Net.WebSockets;
using System.Text.Json;

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
builder.Services.AddSingleton(_ => builder.Configuration.GetTwilioOptions());
builder.Services.AddSingleton<TwilioSignatureValidator>();
builder.Services.AddScoped<TwilioRecordingIngestionService>();
builder.Services.AddScoped<TwilioMediaStreamIngestionService>();
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
app.UseWebSockets();
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
    endpoints = new[] { "GET /health", "GET /api/config/status", "POST /api/ingestions/audio", "POST /api/twilio/voice", "POST /api/twilio/recording-status", "GET /api/twilio/media-stream" }
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
        var results = await pipeline.ProcessBatchAudioAsync(
            new BatchIngestionRequest(
                session,
                new[] { new BatchAudioInput(audioAsset) },
                processingOptions
            ),
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

app.MapPost("/api/twilio/voice", async (
    HttpRequest request,
    TwilioOptions twilioOptions,
    TwilioSignatureValidator signatureValidator,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    logger.LogInformation(
        "Twilio voice webhook received. Path: {Path}. ValidateSignatures: {ValidateSignatures}.",
        request.Path,
        twilioOptions.ValidateSignatures);

    if (twilioOptions.ValidateSignatures)
    {
        if (!request.HasFormContentType)
        {
            logger.LogWarning("Twilio voice webhook rejected: content type was not form data.");
            return Results.BadRequest(new ApiErrorResponse(
                "Expected application/x-www-form-urlencoded form data.",
                "invalid_request"
            ));
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var requestUri = BuildPublicRequestUri(request);
        if (!signatureValidator.IsValid(requestUri, ToSignatureFormData(form), request.Headers["X-Twilio-Signature"].FirstOrDefault()))
        {
            logger.LogWarning("Twilio voice webhook rejected: invalid signature.");
            return Results.Json(
                new ApiErrorResponse("Invalid Twilio signature.", "invalid_signature"),
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    var recordingStatusCallback = BuildEndpointUri(request, twilioOptions, "/api/twilio/recording-status");
    var mediaStreamUri = BuildWebSocketEndpointUri(request, twilioOptions, "/api/twilio/media-stream");
    var twiml = BuildVoiceTwiML(twilioOptions, recordingStatusCallback, mediaStreamUri);
    var containsStartStream = ContainsStartStream(twiml);

    logger.LogInformation(
        "Twilio voice webhook returned TwiML. EnableLiveMediaStreams: {EnableLiveMediaStreams}. LiveStreamTrack: {LiveStreamTrack}. LiveStreamName: {LiveStreamName}. MediaStreamUri: {MediaStreamUri}. RecordingStatusCallback: {RecordingStatusCallback}. ContainsStartStream: {ContainsStartStream}. ForwardToPhoneLast4: {ForwardToPhoneLast4}.",
        twilioOptions.EnableLiveMediaStreams,
        twilioOptions.LiveStreamTrack,
        twilioOptions.LiveStreamName,
        mediaStreamUri,
        recordingStatusCallback,
        containsStartStream,
        RedactPhoneLast4(twilioOptions.ForwardToPhoneNumber));

    if (request.HasFormContentType)
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var callSid = form["CallSid"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(callSid))
        {
            var artifactStorage = request.HttpContext.RequestServices.GetRequiredService<IArtifactStorage>();
            _ = SaveTwilioVoiceDebugArtifactAsync(
                artifactStorage,
                callSid,
                twilioOptions,
                twiml,
                recordingStatusCallback,
                mediaStreamUri,
                containsStartStream,
                cancellationToken);
        }
    }

    return Results.Text(twiml, "application/xml");
});

app.MapGet("/api/twilio/voice/preview", (
    HttpRequest request,
    TwilioOptions twilioOptions) =>
{
    var recordingStatusCallback = BuildEndpointUri(request, twilioOptions, "/api/twilio/recording-status");
    var mediaStreamUri = BuildWebSocketEndpointUri(request, twilioOptions, "/api/twilio/media-stream");
    var twiml = BuildVoiceTwiML(twilioOptions, recordingStatusCallback, mediaStreamUri);
    var redactedTwiml = RedactConfiguredPhoneNumber(twiml, twilioOptions.ForwardToPhoneNumber);

    return Results.Ok(new
    {
        enableLiveMediaStreams = twilioOptions.EnableLiveMediaStreams,
        streamUrl = mediaStreamUri.ToString(),
        streamTrack = twilioOptions.LiveStreamTrack,
        streamName = twilioOptions.LiveStreamName,
        forwardToPhoneNumber = RedactPhoneLast4(twilioOptions.ForwardToPhoneNumber),
        recordingStatusCallback = recordingStatusCallback.ToString(),
        containsStartStream = ContainsStartStream(twiml),
        twiml = redactedTwiml
    });
});

app.MapGet("/api/twilio/media-stream", async (
    HttpContext context,
    TwilioMediaStreamIngestionService mediaStreamIngestionService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    logger.LogInformation("Media stream endpoint hit. IsWebSocket: {IsWebSocket}", context.WebSockets.IsWebSocketRequest);

    if (!context.WebSockets.IsWebSocketRequest)
    {
        logger.LogWarning("Twilio media stream endpoint rejected non-WebSocket request.");
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                "Expected a Twilio Media Streams WebSocket upgrade request.",
                "websocket_required"),
            cancellationToken);
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    TwilioMediaStreamResult result;
    try
    {
        result = await mediaStreamIngestionService.ProcessAsync(webSocket, cancellationToken);
    }
    catch (WebSocketException ex)
    {
        logger.LogWarning(ex, "Twilio media stream WebSocket failed.");
        return;
    }

    logger.LogInformation(
        "Twilio media stream request completed. SessionId: {SessionId}. StreamSid: {StreamSid}. CallSid: {CallSid}. FinalizationStatus: {FinalizationStatus}. MetadataArtifactPath: {MetadataArtifactPath}.",
        result.SessionId,
        result.StreamSid,
        result.CallSid,
        result.FinalizationStatus,
        result.MetadataArtifactPath);
});

app.MapPost("/api/twilio/recording-status", async (
    HttpRequest request,
    TwilioOptions twilioOptions,
    TwilioSignatureValidator signatureValidator,
    TwilioRecordingIngestionService ingestionService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        logger.LogWarning("Twilio recording webhook rejected: content type was not form data.");
        return Results.BadRequest(new ApiErrorResponse(
            "Expected application/x-www-form-urlencoded form data.",
            "invalid_request"
        ));
    }

    var form = await request.ReadFormAsync(cancellationToken);

    if (twilioOptions.ValidateSignatures)
    {
        var requestUri = BuildPublicRequestUri(request);
        if (!signatureValidator.IsValid(requestUri, ToSignatureFormData(form), request.Headers["X-Twilio-Signature"].FirstOrDefault()))
        {
            logger.LogWarning("Twilio recording webhook rejected: invalid signature.");
            return Results.Json(
                new ApiErrorResponse("Invalid Twilio signature.", "invalid_signature"),
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    var webhook = new TwilioRecordingWebhookRequest(
        GetRequiredFormValue(form, "CallSid"),
        GetRequiredFormValue(form, "RecordingSid"),
        GetRequiredFormValue(form, "RecordingUrl"),
        GetRequiredFormValue(form, "RecordingStatus"),
        form["AccountSid"].FirstOrDefault(),
        form["RecordingDuration"].FirstOrDefault(),
        form["RecordingChannels"].FirstOrDefault(),
        form["RecordingSource"].FirstOrDefault()
    );

    logger.LogInformation(
        "Twilio recording callback received. CallSid: {CallSid}. RecordingSid: {RecordingSid}. RecordingStatus: {RecordingStatus}.",
        webhook.CallSid,
        webhook.RecordingSid,
        webhook.RecordingStatus);

    var missingFields = GetMissingRequiredTwilioFields(webhook).ToList();
    if (missingFields.Count > 0)
    {
        logger.LogWarning("Twilio recording webhook rejected: missing fields {MissingFields}.", string.Join(",", missingFields));
        return Results.BadRequest(new ApiErrorResponse(
            $"Missing required Twilio recording fields: {string.Join(", ", missingFields)}.",
            "invalid_request"
        ));
    }

    try
    {
        var result = await ingestionService.IngestAsync(webhook, cancellationToken);
        logger.LogInformation(
            "Twilio recording webhook {Status}. CallSid: {CallSid}. RecordingSid: {RecordingSid}. RecordingStatus: {RecordingStatus}.",
            result.Status,
            webhook.CallSid,
            webhook.RecordingSid,
            webhook.RecordingStatus);

        return Results.Ok(new
        {
            status = result.Status,
            message = result.Message,
            sessionId = result.Session?.Id,
            callSid = webhook.CallSid,
            recordingSid = webhook.RecordingSid,
            externalId = result.Session?.ExternalId,
            processingMode = result.ProcessingMode.ToString(),
            summarizationProfile = result.SummarizationProfile.ToString(),
            artifacts = result.PipelineResults is null
                ? Array.Empty<ArtifactReferenceResponse>()
                : result.PipelineResults.SelectMany(ToArtifactReferences).ToArray()
        });
    }
    catch (TwilioRecordingIngestionException ex)
    {
        logger.LogWarning(
            ex,
            "Twilio recording webhook failed. CallSid: {CallSid}. RecordingSid: {RecordingSid}. ErrorCode: {ErrorCode}. DownloadStatusCode: {DownloadStatusCode}.",
            webhook.CallSid,
            webhook.RecordingSid,
            ex.ErrorCode,
            ex.DownloadStatusCode);

        return Results.Json(
            new
            {
                status = "failed",
                error = ex.Message,
                errorCode = ex.ErrorCode,
                sessionId = (string?)null,
                callSid = webhook.CallSid,
                recordingSid = webhook.RecordingSid,
                processingMode = twilioOptions.DefaultProcessingMode.ToString(),
                summarizationProfile = twilioOptions.DefaultSummarizationProfile.ToString(),
                artifacts = Array.Empty<ArtifactReferenceResponse>()
            },
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (InvalidOperationException ex)
    {
        logger.LogError(
            ex,
            "Twilio recording ingestion failed. CallSid: {CallSid}. RecordingSid: {RecordingSid}.",
            webhook.CallSid,
            webhook.RecordingSid);

        return Results.Json(
            new
            {
                status = "failed",
                error = "The Twilio recording could not be processed. Check API configuration and service availability.",
                errorCode = "ingestion_failed",
                sessionId = (string?)null,
                callSid = webhook.CallSid,
                recordingSid = webhook.RecordingSid,
                processingMode = twilioOptions.DefaultProcessingMode.ToString(),
                summarizationProfile = twilioOptions.DefaultSummarizationProfile.ToString(),
                artifacts = Array.Empty<ArtifactReferenceResponse>()
            },
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

static IEnumerable<ArtifactReferenceResponse> ToArtifactReferences(IngestionPipelineResult result)
{
    var artifacts = new[]
    {
        ToArtifactReference(result.AudioArtifact),
        ToArtifactReference(result.TranscriptArtifact),
        ToArtifactReference(result.NoteArtifact),
        ToArtifactReference(result.MetadataArtifact)
    };

    return artifacts.Where(artifact => artifact is not null)!;
}

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

static string GetRequiredFormValue(IFormCollection form, string key) =>
    form[key].FirstOrDefault() ?? "";

static IEnumerable<string> GetMissingRequiredTwilioFields(TwilioRecordingWebhookRequest request)
{
    if (string.IsNullOrWhiteSpace(request.CallSid))
        yield return nameof(request.CallSid);

    if (string.IsNullOrWhiteSpace(request.RecordingSid))
        yield return nameof(request.RecordingSid);

    if (string.IsNullOrWhiteSpace(request.RecordingUrl))
        yield return nameof(request.RecordingUrl);

    if (string.IsNullOrWhiteSpace(request.RecordingStatus))
        yield return nameof(request.RecordingStatus);
}

static Uri BuildPublicRequestUri(HttpRequest request)
{
    var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
    var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value ?? "";
    return new Uri($"{scheme}://{host}{request.Path}{request.QueryString}");
}

static Uri BuildEndpointUri(HttpRequest request, TwilioOptions twilioOptions, string path)
{
    if (TryBuildFromPublicBaseUrl(twilioOptions, path, out var configuredUri))
        return configuredUri;

    var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
    var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value ?? "";
    return new Uri($"{scheme}://{host}{path}");
}

static Uri BuildWebSocketEndpointUri(HttpRequest request, TwilioOptions twilioOptions, string path)
{
    if (TryBuildFromPublicBaseUrl(twilioOptions, path, out var configuredUri))
    {
        var builder = new UriBuilder(configuredUri)
        {
            Scheme = Uri.UriSchemeHttps.Equals(configuredUri.Scheme, StringComparison.OrdinalIgnoreCase)
                ? "wss"
                : "ws",
            Port = -1
        };
        return builder.Uri;
    }

    var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
    var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value ?? "";
    var isLocalHost = host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
        || host.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase);
    var webSocketScheme = isLocalHost && !scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
        ? "ws"
        : "wss";

    return new Uri($"{webSocketScheme}://{host}{path}");
}

static bool TryBuildFromPublicBaseUrl(TwilioOptions twilioOptions, string path, out Uri uri)
{
    uri = null!;
    if (string.IsNullOrWhiteSpace(twilioOptions.PublicBaseUrl))
        return false;

    if (!Uri.TryCreate(twilioOptions.PublicBaseUrl, UriKind.Absolute, out var baseUri))
        return false;

    uri = new Uri(baseUri, path);
    return true;
}

static string RedactPhoneLast4(string phoneNumber)
{
    var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
    if (digits.Length == 0)
        return "";

    var last4 = digits.Length <= 4 ? digits : digits[^4..];
    return $"***{last4}";
}

static string RedactConfiguredPhoneNumber(string value, string phoneNumber)
{
    if (string.IsNullOrWhiteSpace(phoneNumber))
        return value;

    return value.Replace(phoneNumber, RedactPhoneLast4(phoneNumber), StringComparison.Ordinal);
}

static bool ContainsStartStream(string twiml) =>
    twiml.Contains("<Start>", StringComparison.Ordinal)
    && twiml.Contains("<Stream", StringComparison.Ordinal);

static async Task SaveTwilioVoiceDebugArtifactAsync(
    IArtifactStorage artifactStorage,
    string callSid,
    TwilioOptions twilioOptions,
    string twiml,
    Uri recordingStatusCallback,
    Uri mediaStreamUri,
    bool containsStartStream,
    CancellationToken cancellationToken)
{
    try
    {
        var sanitizedTwiml = RedactConfiguredPhoneNumber(twiml, twilioOptions.ForwardToPhoneNumber);
        var debugData = new
        {
            callSid,
            generatedAtUtc = DateTime.UtcNow,
            enableLiveMediaStreams = twilioOptions.EnableLiveMediaStreams,
            containsStartStream,
            streamUrl = mediaStreamUri.ToString(),
            streamTrack = twilioOptions.LiveStreamTrack,
            streamName = twilioOptions.LiveStreamName,
            recordingStatusCallback = recordingStatusCallback.ToString(),
            sanitizedTwiml
        };

        var json = JsonSerializer.Serialize(debugData, new JsonSerializerOptions { WriteIndented = true });
        await artifactStorage.SaveTextAsync(
            ArtifactKind.Metadata,
            $"sessions/twilio-voice-debug/{callSid}/voice-twiml.json",
            json,
            cancellationToken);
    }
    catch
    {
        // Fire and forget, don't break the voice call if debug storage fails
    }
}

static string BuildVoiceTwiML(TwilioOptions twilioOptions, Uri recordingStatusCallback, Uri mediaStreamUri)
{
    if (string.IsNullOrWhiteSpace(twilioOptions.ForwardToPhoneNumber))
    {
        return """
<?xml version="1.0" encoding="UTF-8"?>
<Response>
  <Say>Thought Buffer call forwarding is not configured.</Say>
  <Hangup />
</Response>
""";
    }

    var escapedNumber = SecurityElement.Escape(twilioOptions.ForwardToPhoneNumber);
    var escapedCallback = SecurityElement.Escape(recordingStatusCallback.ToString());

    if (twilioOptions.EnableLiveMediaStreams)
    {
        var escapedStreamName = SecurityElement.Escape(twilioOptions.LiveStreamName);
        var escapedStreamUri = SecurityElement.Escape(mediaStreamUri.ToString());
        var escapedTrack = SecurityElement.Escape(twilioOptions.LiveStreamTrack);

        if (twilioOptions.LiveStreamTwiMLMode.Equals("StreamOnlyTest", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
<?xml version="1.0" encoding="UTF-8"?>
<Response>
  <Start>
    <Stream url="{escapedStreamUri}" track="{escapedTrack}" />
  </Start>
  <Say>Stream started. You have 30 seconds to speak for the test. Testing 1 2 3.</Say>
  <Pause length="30" />
  <Say>Test complete. Hanging up now. Goodbye.</Say>
</Response>
""";
        }

        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<Response>
  <Say>This call may be recorded and monitored for transcription and follow up.</Say>
  <Start>
    <Stream name="{escapedStreamName}" url="{escapedStreamUri}" track="{escapedTrack}">
      <Parameter name="source" value="twilio-live-media-stream" />
      <Parameter name="mode" value="sales-call-training" />
    </Stream>
  </Start>
  <Dial record="record-from-answer-dual" recordingStatusCallback="{escapedCallback}" recordingStatusCallbackEvent="completed">
    <Number>{escapedNumber}</Number>
  </Dial>
</Response>
""";
    }

    return $"""
<?xml version="1.0" encoding="UTF-8"?>
<Response>
  <Say>This call may be recorded for transcription and follow up.</Say>
  <Dial record="record-from-answer-dual" recordingStatusCallback="{escapedCallback}" recordingStatusCallbackEvent="completed">
    <Number>{escapedNumber}</Number>
  </Dial>
</Response>
""";
}

static IReadOnlyDictionary<string, IReadOnlyList<string>> ToSignatureFormData(IFormCollection form) =>
    form.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<string>)pair.Value.Select(value => value ?? "").ToArray(),
        StringComparer.Ordinal);

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
