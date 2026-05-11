namespace ThoughtBuffer.Api.Contracts;

public record ConfigStatusResponse(
    bool HasOpenAiKey,
    bool LocalStorageRootConfigured,
    long MaxUploadBytes,
    string EnvironmentName
);
