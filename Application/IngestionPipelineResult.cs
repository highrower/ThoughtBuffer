using ThoughtBuffer.Models;

namespace ThoughtBuffer.Application;

public record IngestionPipelineResult(
    RecordingEntry Recording,
    string? TranscriptPath,
    string? NotePath,
    Transcript? Transcript,
    SummaryResult? Summary
);
