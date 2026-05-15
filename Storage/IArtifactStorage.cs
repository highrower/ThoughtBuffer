namespace ThoughtBuffer.Storage;

public interface IArtifactStorage
{
    string ProviderName { get; }

    Task<ArtifactWriteResult> SaveFileAsync(
        ArtifactKind kind,
        string artifactPath,
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<ArtifactWriteResult> SaveTextAsync(
        ArtifactKind kind,
        string artifactPath,
        string content,
        CancellationToken cancellationToken = default);
}
