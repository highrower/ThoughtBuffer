namespace ThoughtBuffer.Api.Contracts;

public record ConfigStatusResponse(
    bool HasOpenAiKey,
    bool LocalStorageRootConfigured,
    long MaxUploadBytes,
    string EnvironmentName,
    string ArtifactStorageProvider,
    bool ArtifactStorageConfigured,
    string ArtifactContainerName
);
