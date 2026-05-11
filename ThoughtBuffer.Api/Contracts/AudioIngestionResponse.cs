namespace ThoughtBuffer.Api.Contracts;

public record AudioIngestionResponse(
    string SessionId,
    string Source,
    string Status,
    IReadOnlyList<AudioIngestionFileResult> Files
);
