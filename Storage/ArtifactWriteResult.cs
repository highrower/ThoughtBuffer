namespace ThoughtBuffer.Storage;

public record ArtifactWriteResult(
    ArtifactKind Kind,
    string StorageProvider,
    string Path,
    Uri? Uri = null
);
