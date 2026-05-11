namespace ThoughtBuffer.Models;

public record IngestionSession(
    string Id,
    IngestionMode Mode,
    SourceSystem Source,
    DateTime CreatedAtUtc,
    string? ExternalId = null,
    string? DisplayName = null
);
