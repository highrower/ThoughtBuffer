using ThoughtBuffer.Models;

namespace ThoughtBuffer.Application;

public record BatchIngestionRequest(
    IngestionSession Session,
    IReadOnlyList<BatchAudioInput> AudioInputs,
    IngestionProcessingOptions ProcessingOptions,
    string? LegacyTranscriptFolder = null,
    string? LegacyNotesFolder = null,
    string? LegacyMetadataPath = null
);
