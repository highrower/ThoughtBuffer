namespace ThoughtBuffer.Api.Contracts;

public record ArtifactReferenceResponse(
    string ArtifactKind,
    string StorageProvider,
    string Path,
    string? Uri
);
