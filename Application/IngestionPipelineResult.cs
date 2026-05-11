using ThoughtBuffer.Models;
using ThoughtBuffer.Storage;

namespace ThoughtBuffer.Application;

public record IngestionPipelineResult(
    RecordingEntry Recording,
    string? TranscriptPath,
    string? NotePath,
    Transcript? Transcript,
    SummaryResult? Summary,
    ArtifactWriteResult? AudioArtifact = null,
    ArtifactWriteResult? TranscriptArtifact = null,
    ArtifactWriteResult? NoteArtifact = null,
    ArtifactWriteResult? MetadataArtifact = null
);
