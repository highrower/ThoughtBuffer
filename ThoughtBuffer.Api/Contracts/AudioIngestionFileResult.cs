using ThoughtBuffer.Models;

namespace ThoughtBuffer.Api.Contracts;

public record AudioIngestionFileResult(
    string OriginalFileName,
    string StoredFileName,
    string? TranscriptText,
    SummaryResult? Summary,
    string? TranscriptArtifactPath,
    string? NoteArtifactPath
);
