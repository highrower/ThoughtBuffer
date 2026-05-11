namespace ThoughtBuffer.Api.Contracts;

public record AudioIngestionResponse(
    string SessionId,
    string Source,
    string Status,
    string ProcessingMode,
    string SummarizationProfile,
    IReadOnlyList<AudioIngestionFileResult> Files
);
