using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ThoughtBuffer.Models;
using ThoughtBuffer.Storage;

namespace ThoughtBuffer.Integrations.Twilio;

public sealed class TwilioMediaStreamIngestionService(
    TwilioOptions options,
    IArtifactStorage artifactStorage,
    ILogger<TwilioMediaStreamIngestionService> logger)
{
    const string MediaEncoding = "audio/x-mulaw";
    const int MediaSampleRate = 8000;
    const int MediaChannels = 1;

    public async Task<TwilioMediaStreamResult> ProcessAsync(
        WebSocket webSocket,
        CancellationToken cancellationToken = default)
    {
        var state = new TwilioMediaStreamState(
            Guid.NewGuid().ToString("N"),
            options.LiveStreamTrack,
            DateTime.UtcNow);

        var buffer = new byte[64 * 1024];
        var closeReason = "websocket_closed";

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveTextMessageAsync(webSocket, buffer, cancellationToken);
                if (message is null)
                {
                    closeReason = webSocket.CloseStatusDescription ?? webSocket.CloseStatus?.ToString() ?? "websocket_closed";
                    break;
                }

                try
                {
                    ProcessMessage(message, state);
                }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
                {
                    state.MessageParseErrorCount++;
                    state.FailureReason ??= ex.GetType().Name;
                    logger.LogWarning(
                        ex,
                        "Twilio media stream message parsing failed. SessionId: {SessionId}. StreamSid: {StreamSid}. CallSid: {CallSid}. ParseErrorCount: {ParseErrorCount}.",
                        state.SessionId,
                        state.StreamSid,
                        state.CallSid,
                        state.MessageParseErrorCount);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            state.FailureReason ??= "request_cancelled";
            closeReason = "request_cancelled";
        }
        catch (Exception ex) when (ex is JsonException or FormatException or WebSocketException)
        {
            state.FailureReason ??= ex.GetType().Name;
            closeReason = state.FailureReason;
            logger.LogWarning(
                ex,
                "Twilio media stream processing failed. SessionId: {SessionId}. StreamSid: {StreamSid}. CallSid: {CallSid}.",
                state.SessionId,
                state.StreamSid,
                state.CallSid);
        }
        finally
        {
            state.StoppedAtUtc ??= DateTime.UtcNow;
            if (!state.StopEventReceived && state.FailureReason is null)
                state.FailureReason = closeReason;
        }

        var finalizationStatus = state.StopEventReceived && state.FailureReason is null
            ? "completed"
            : state.StopEventReceived
                ? "completed_with_warnings"
                : "incomplete";

        ArtifactWriteResult? metadataArtifact = null;
        if (options.LiveStreamStoreMetadata)
            metadataArtifact = await SaveMetadataAsync(state, finalizationStatus, cancellationToken);

        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Twilio media stream processed.",
                CancellationToken.None);
        }

        logger.LogInformation(
            "Twilio media stream finalized. SessionId: {SessionId}. StreamSid: {StreamSid}. CallSid: {CallSid}. Status: {FinalizationStatus}. InboundChunks: {InboundChunks}. OutboundChunks: {OutboundChunks}.",
            state.SessionId,
            state.StreamSid,
            state.CallSid,
            finalizationStatus,
            state.Inbound.ChunkCount,
            state.Outbound.ChunkCount);

        return new TwilioMediaStreamResult(
            state.SessionId,
            state.StreamSid,
            state.CallSid,
            finalizationStatus,
            metadataArtifact?.Path);
    }

    static async Task<string?> ReceiveTextMessageAsync(
        WebSocket webSocket,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();

        while (true)
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            if (result.MessageType != WebSocketMessageType.Text)
                throw new WebSocketException("Twilio media stream sent a non-text WebSocket message.");

            message.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(message.ToArray());
        }
    }

    void ProcessMessage(string message, TwilioMediaStreamState state)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var eventType = GetStringOrNumberAsString(root, "event");

        switch (eventType)
        {
            case "connected":
                state.ConnectedEventReceived = true;
                logger.LogInformation(
                    "Twilio media stream connected event received. SessionId: {SessionId}.",
                    state.SessionId);
                break;

            case "start":
                ProcessStart(root, state);
                break;

            case "media":
                ProcessMedia(root, state);
                break;

            case "stop":
                state.StopEventReceived = true;
                state.StoppedAtUtc = DateTime.UtcNow;
                logger.LogInformation(
                    "Twilio media stream stop event received. SessionId: {SessionId}. StreamSid: {StreamSid}. CallSid: {CallSid}.",
                    state.SessionId,
                    state.StreamSid,
                    state.CallSid);
                break;

            case "dtmf":
            case "mark":
                state.IgnoredEventCount++;
                logger.LogInformation(
                    "Twilio media stream event ignored. SessionId: {SessionId}. EventType: {EventType}. StreamSid: {StreamSid}.",
                    state.SessionId,
                    eventType,
                    state.StreamSid);
                break;

            default:
                state.UnknownEventCount++;
                logger.LogInformation(
                    "Unknown Twilio media stream event ignored. SessionId: {SessionId}. EventType: {EventType}. StreamSid: {StreamSid}.",
                    state.SessionId,
                    eventType,
                    state.StreamSid);
                break;
        }
    }

    void ProcessStart(JsonElement root, TwilioMediaStreamState state)
    {
        var start = root.GetProperty("start");
        state.StreamSid = GetStringOrNumberAsString(start, "streamSid") ?? GetStringOrNumberAsString(root, "streamSid");
        state.CallSid = GetStringOrNumberAsString(start, "callSid");
        state.StartedAtUtc = DateTime.UtcNow;
        state.MediaFormat = ReadMediaFormat(start);
        state.CustomParameters = ReadCustomParameters(start);

        if (string.IsNullOrWhiteSpace(state.StreamSid))
        {
            state.RequiredFieldWarningCount++;
            state.FailureReason ??= "missing_streamSid";
            logger.LogWarning("Twilio media stream start event missing streamSid. SessionId: {SessionId}.", state.SessionId);
        }

        if (string.IsNullOrWhiteSpace(state.CallSid))
        {
            state.RequiredFieldWarningCount++;
            state.FailureReason ??= "missing_callSid";
            logger.LogWarning("Twilio media stream start event missing callSid. SessionId: {SessionId}. StreamSid: {StreamSid}.", state.SessionId, state.StreamSid);
        }

        foreach (var track in ReadTracks(start))
            state.TracksReceived.Add(track);

        logger.LogInformation(
            "Twilio media stream start event received. SessionId: {SessionId}. StreamSid: {StreamSid}. CallSid: {CallSid}. RequestedTrack: {RequestedTrack}. Encoding: {Encoding}. SampleRate: {SampleRate}. Channels: {Channels}.",
            state.SessionId,
            state.StreamSid,
            state.CallSid,
            state.RequestedTrack,
            state.MediaFormat.Encoding,
            state.MediaFormat.SampleRate,
            state.MediaFormat.Channels);
    }

    void ProcessMedia(JsonElement root, TwilioMediaStreamState state)
    {
        var sequenceNumber = GetLongFlexible(root, "sequenceNumber");
        TrackSequence(state.AllSequences, sequenceNumber);

        var media = root.GetProperty("media");
        var track = NormalizeTrack(GetStringOrNumberAsString(media, "track"));
        var chunkNumber = GetLongFlexible(media, "chunk");
        var timestamp = GetLongFlexible(media, "timestamp");
        var payload = GetStringOrNumberAsString(media, "payload") ?? "";
        long decodedBytes = 0;
        try
        {
            decodedBytes = Convert.FromBase64String(payload).LongLength;
        }
        catch (FormatException)
        {
            state.InvalidPayloadCount++;
            state.FailureReason ??= "invalid_media_payload";
            logger.LogWarning(
                "Twilio media stream media event contained invalid base64 payload. SessionId: {SessionId}. StreamSid: {StreamSid}. CallSid: {CallSid}. Track: {Track}.",
                state.SessionId,
                state.StreamSid,
                state.CallSid,
                track);
        }

        var counters = track == "outbound" ? state.Outbound : state.Inbound;

        if (!state.TracksReceived.Contains(track))
            state.TracksReceived.Add(track);

        TrackSequence(counters.Chunks, chunkNumber);
        counters.ChunkCount++;
        counters.DecodedByteCount += decodedBytes;

        if (timestamp.HasValue)
        {
            state.FirstMediaTimestamp ??= timestamp;
            state.LastMediaTimestamp = timestamp;
        }
    }

    async Task<ArtifactWriteResult> SaveMetadataAsync(
        TwilioMediaStreamState state,
        string finalizationStatus,
        CancellationToken cancellationToken)
    {
        var metadata = new
        {
            sessionId = state.SessionId,
            streamSid = state.StreamSid,
            callSid = state.CallSid,
            source = SourceSystem.TwilioMediaStream.ToString(),
            requestedTrack = state.RequestedTrack,
            tracksReceived = state.TracksReceived.OrderBy(track => track).ToArray(),
            mediaFormat = state.MediaFormat,
            chunkCountsByTrack = new
            {
                inbound = state.Inbound.ChunkCount,
                outbound = state.Outbound.ChunkCount
            },
            decodedByteCountsByTrack = new
            {
                inbound = state.Inbound.DecodedByteCount,
                outbound = state.Outbound.DecodedByteCount
            },
            duplicateChunkCountsByTrack = new
            {
                inbound = state.Inbound.Chunks.DuplicateCount,
                outbound = state.Outbound.Chunks.DuplicateCount
            },
            chunkGapCountsByTrack = new
            {
                inbound = state.Inbound.Chunks.GapCount,
                outbound = state.Outbound.Chunks.GapCount
            },
            outOfOrderChunkCountsByTrack = new
            {
                inbound = state.Inbound.Chunks.OutOfOrderCount,
                outbound = state.Outbound.Chunks.OutOfOrderCount
            },
            firstMediaTimestamp = state.FirstMediaTimestamp,
            lastMediaTimestamp = state.LastMediaTimestamp,
            stopEventReceived = state.StopEventReceived,
            startedAtUtc = state.StartedAtUtc,
            stoppedAtUtc = state.StoppedAtUtc,
            finalizationStatus,
            failureReason = state.FailureReason,
            connectedEventReceived = state.ConnectedEventReceived,
            duplicateEventCount = state.AllSequences.DuplicateCount + state.Inbound.Chunks.DuplicateCount + state.Outbound.Chunks.DuplicateCount,
            sequenceGapCount = state.AllSequences.GapCount,
            outOfOrderEventCount = state.AllSequences.OutOfOrderCount + state.Inbound.Chunks.OutOfOrderCount + state.Outbound.Chunks.OutOfOrderCount,
            ignoredEventCount = state.IgnoredEventCount,
            unknownEventCount = state.UnknownEventCount,
            messageParseErrorCount = state.MessageParseErrorCount,
            requiredFieldWarningCount = state.RequiredFieldWarningCount,
            invalidPayloadCount = state.InvalidPayloadCount,
            customParameters = state.CustomParameters
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var artifact = await artifactStorage.SaveTextAsync(
            ArtifactKind.Metadata,
            $"sessions/{state.SessionId}/metadata/live-stream.json",
            json,
            cancellationToken);

        logger.LogInformation(
            "Twilio media stream metadata artifact written. SessionId: {SessionId}. StreamSid: {StreamSid}. CallSid: {CallSid}. ArtifactPath: {ArtifactPath}.",
            state.SessionId,
            state.StreamSid,
            state.CallSid,
            artifact.Path);

        return artifact;
    }

    static TwilioMediaFormat ReadMediaFormat(JsonElement start)
    {
        if (!start.TryGetProperty("mediaFormat", out var mediaFormat))
            return new TwilioMediaFormat(MediaEncoding, MediaSampleRate, MediaChannels);

        return new TwilioMediaFormat(
            GetStringOrNumberAsString(mediaFormat, "encoding") ?? MediaEncoding,
            GetIntFlexible(mediaFormat, "sampleRate") ?? MediaSampleRate,
            GetIntFlexible(mediaFormat, "channels") ?? MediaChannels);
    }

    static IReadOnlyDictionary<string, string> ReadCustomParameters(JsonElement start)
    {
        if (!start.TryGetProperty("customParameters", out var customParameters)
            || customParameters.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in customParameters.EnumerateObject())
        {
            if (IsSensitiveParameterName(property.Name))
                continue;

            result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? ""
                : property.Value.ToString();
        }

        return result;
    }

    static IEnumerable<string> ReadTracks(JsonElement start)
    {
        if (!start.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var track in tracks.EnumerateArray())
        {
            if (track.ValueKind == JsonValueKind.String)
                yield return NormalizeTrack(GetStringOrNumberAsString(track));
        }
    }

    static void TrackSequence(TwilioSequenceTracker tracker, long? value)
    {
        if (!value.HasValue)
            return;

        if (!tracker.Seen.Add(value.Value))
        {
            tracker.DuplicateCount++;
            return;
        }

        if (tracker.Last.HasValue)
        {
            if (value.Value < tracker.Last.Value)
                tracker.OutOfOrderCount++;
            else if (value.Value > tracker.Last.Value + 1)
                tracker.GapCount += value.Value - tracker.Last.Value - 1;
        }

        tracker.Last = value.Value;
    }

    static string NormalizeTrack(string? track) =>
        track?.Equals("outbound", StringComparison.OrdinalIgnoreCase) == true
            ? "outbound"
            : "inbound";

    static string? GetStringOrNumberAsString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
            ? GetStringOrNumberAsString(property)
            : null;

    static string? GetStringOrNumberAsString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.ToString()
        };

    static long? GetLongFlexible(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
            return number;

        var value = GetStringOrNumberAsString(property);
        return long.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    static int? GetIntFlexible(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;

        var value = GetStringOrNumberAsString(property);
        return int.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    static bool IsSensitiveParameterName(string name) =>
        name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("key", StringComparison.OrdinalIgnoreCase)
        || name.Contains("credential", StringComparison.OrdinalIgnoreCase);
}

public sealed record TwilioMediaStreamResult(
    string SessionId,
    string? StreamSid,
    string? CallSid,
    string FinalizationStatus,
    string? MetadataArtifactPath);

public sealed record TwilioMediaFormat(
    string Encoding,
    int SampleRate,
    int Channels);

sealed class TwilioMediaStreamState(
    string sessionId,
    string requestedTrack,
    DateTime startedAtUtc)
{
    public string SessionId { get; } = sessionId;
    public string RequestedTrack { get; } = requestedTrack;
    public DateTime StartedAtUtc { get; set; } = startedAtUtc;
    public DateTime? StoppedAtUtc { get; set; }
    public string? StreamSid { get; set; }
    public string? CallSid { get; set; }
    public TwilioMediaFormat MediaFormat { get; set; } = new("audio/x-mulaw", 8000, 1);
    public HashSet<string> TracksReceived { get; } = new(StringComparer.OrdinalIgnoreCase);
    public TwilioTrackCounters Inbound { get; } = new();
    public TwilioTrackCounters Outbound { get; } = new();
    public TwilioSequenceTracker AllSequences { get; } = new();
    public long? FirstMediaTimestamp { get; set; }
    public long? LastMediaTimestamp { get; set; }
    public bool ConnectedEventReceived { get; set; }
    public bool StopEventReceived { get; set; }
    public string? FailureReason { get; set; }
    public int IgnoredEventCount { get; set; }
    public int UnknownEventCount { get; set; }
    public int MessageParseErrorCount { get; set; }
    public int RequiredFieldWarningCount { get; set; }
    public int InvalidPayloadCount { get; set; }
    public IReadOnlyDictionary<string, string> CustomParameters { get; set; } = new Dictionary<string, string>();
}

sealed class TwilioTrackCounters
{
    public long ChunkCount { get; set; }
    public long DecodedByteCount { get; set; }
    public TwilioSequenceTracker Chunks { get; } = new();
}

sealed class TwilioSequenceTracker
{
    public HashSet<long> Seen { get; } = new();
    public long? Last { get; set; }
    public long DuplicateCount { get; set; }
    public long GapCount { get; set; }
    public long OutOfOrderCount { get; set; }
}
