using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using ThoughtBuffer.Application;
using ThoughtBuffer.Models;
using ThoughtBuffer.Storage;

namespace ThoughtBuffer.Integrations.Twilio;

public sealed class TwilioRecordingIngestionService(
    TwilioOptions options,
    AppPaths paths,
    IIngestionPipeline ingestionPipeline,
    ILogger<TwilioRecordingIngestionService> logger)
{
    static readonly HttpClient HttpClient = new();

    public async Task<TwilioRecordingWebhookResult> IngestAsync(
        TwilioRecordingWebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedStatus = request.RecordingStatus.Trim();

        if (!normalizedStatus.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            return new TwilioRecordingWebhookResult(
                "ignored",
                $"Recording status '{request.RecordingStatus}' is not ready for batch ingestion.",
                null,
                options.DefaultProcessingMode,
                options.DefaultSummarizationProfile);
        }

        if (string.IsNullOrWhiteSpace(options.AccountSid) || string.IsNullOrWhiteSpace(options.AuthToken))
            throw new TwilioRecordingIngestionException(
                "Twilio recording download credentials are not configured.",
                "twilio_configuration_missing");

        var session = new IngestionSession(
            Guid.NewGuid().ToString("N"),
            IngestionMode.AudioFile,
            SourceSystem.TwilioRecording,
            DateTime.UtcNow,
            ExternalId: $"{request.CallSid}:{request.RecordingSid}",
            DisplayName: $"Twilio recording {request.RecordingSid}");

        logger.LogInformation(
            "Twilio ingestion session created. SessionId: {SessionId}. SourceSystem: {SourceSystem}. CallSid: {CallSid}. RecordingSid: {RecordingSid}.",
            session.Id,
            session.Source,
            request.CallSid,
            request.RecordingSid);

        var downloadedPath = await DownloadRecordingAsync(request, session, cancellationToken);
        var fileInfo = new FileInfo(downloadedPath);
        var audioAsset = new AudioAsset(
            Guid.NewGuid().ToString("N"),
            session.Id,
            Path.GetFileName(downloadedPath),
            downloadedPath,
            downloadedPath,
            downloadedPath,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc,
            DateTime.UtcNow,
            "audio/mpeg");

        var processingOptions = new IngestionProcessingOptions(
            options.DefaultProcessingMode,
            options.DefaultSummarizationProfile);

        logger.LogInformation(
            "Twilio recording ingestion started. SessionId: {SessionId}. CallSid: {CallSid}. RecordingSid: {RecordingSid}.",
            session.Id,
            request.CallSid,
            request.RecordingSid);

        var pipelineResults = await ingestionPipeline.ProcessBatchAudioAsync(
            new BatchIngestionRequest(
                session,
                new[] { new BatchAudioInput(audioAsset) },
                processingOptions),
            cancellationToken);

        logger.LogInformation(
            "Twilio recording ingestion completed. SessionId: {SessionId}. CallSid: {CallSid}. RecordingSid: {RecordingSid}.",
            session.Id,
            request.CallSid,
            request.RecordingSid);

        return new TwilioRecordingWebhookResult(
            "completed",
            "Completed Twilio recording downloaded and processed.",
            session,
            options.DefaultProcessingMode,
            options.DefaultSummarizationProfile,
            pipelineResults);
    }

    async Task<string> DownloadRecordingAsync(
        TwilioRecordingWebhookRequest request,
        IngestionSession session,
        CancellationToken cancellationToken)
    {
        var recordingUri = BuildRecordingMediaUri(request.RecordingUrl);
        var safeRecordingSid = SanitizeFileName(request.RecordingSid);
        var fileName = $"{session.Id}-{safeRecordingSid}.mp3";
        var storedPath = Path.GetFullPath(Path.Combine(paths.copyFileFolder, fileName));
        var storageRoot = Path.GetFullPath(paths.copyFileFolder);

        if (!storedPath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
            throw new TwilioRecordingIngestionException("Invalid Twilio recording file path.", "invalid_recording_path");

        logger.LogInformation(
            "Twilio recording download started. SessionId: {SessionId}. CallSid: {CallSid}. RecordingSid: {RecordingSid}.",
            session.Id,
            request.CallSid,
            request.RecordingSid);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, recordingUri);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TwilioRecordingIngestionException(
                "Twilio recording download request failed.",
                "recording_download_failed",
                innerException: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TwilioRecordingIngestionException(
                    $"Twilio recording download failed with HTTP {(int)response.StatusCode}.",
                    "recording_download_failed",
                    response.StatusCode);
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(storedPath);
            await source.CopyToAsync(target, cancellationToken);

            var fileInfo = new FileInfo(storedPath);
            logger.LogInformation(
                "Twilio recording download completed. SessionId: {SessionId}. CallSid: {CallSid}. RecordingSid: {RecordingSid}. SizeBytes: {SizeBytes}.",
                session.Id,
                request.CallSid,
                request.RecordingSid,
                fileInfo.Length);

            return storedPath;
        }
    }

    static Uri BuildRecordingMediaUri(string recordingUrl)
    {
        if (!Uri.TryCreate(recordingUrl, UriKind.Absolute, out var uri))
            throw new TwilioRecordingIngestionException("Twilio RecordingUrl is not an absolute URI.", "invalid_recording_url");

        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new TwilioRecordingIngestionException("Twilio RecordingUrl must use HTTPS.", "invalid_recording_url");

        if (uri.AbsolutePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            return uri;

        var builder = new UriBuilder(uri)
        {
            Path = $"{uri.AbsolutePath}.mp3"
        };

        return builder.Uri;
    }

    static string SanitizeFileName(string value)
    {
        var safeName = Path.GetFileName(value);
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '-');
        }

        return string.IsNullOrWhiteSpace(safeName)
            ? "twilio-recording"
            : safeName;
    }
}

public sealed record TwilioRecordingWebhookResult(
    string Status,
    string Message,
    IngestionSession? Session,
    ProcessingMode ProcessingMode,
    SummarizationProfile SummarizationProfile,
    IReadOnlyList<IngestionPipelineResult>? PipelineResults = null
);

public sealed class TwilioRecordingIngestionException(
    string message,
    string errorCode,
    HttpStatusCode? downloadStatusCode = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
    public HttpStatusCode? DownloadStatusCode { get; } = downloadStatusCode;
}
