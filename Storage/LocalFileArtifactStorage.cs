namespace ThoughtBuffer.Storage;

public sealed class LocalFileArtifactStorage(string rootPath) : IArtifactStorage
{
    public string ProviderName => "Local";

    public async Task<ArtifactWriteResult> SaveFileAsync(
        ArtifactKind kind,
        string artifactPath,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var destinationPath = GetSafeFullPath(artifactPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
                                  ?? throw new InvalidOperationException("Artifact path must include a directory."));

        if (!Path.GetFullPath(sourcePath).Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            await using var source = File.OpenRead(sourcePath);
            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination, cancellationToken);
        }

        return new ArtifactWriteResult(kind, ProviderName, destinationPath);
    }

    public async Task<ArtifactWriteResult> SaveTextAsync(
        ArtifactKind kind,
        string artifactPath,
        string content,
        CancellationToken cancellationToken = default)
    {
        var destinationPath = GetSafeFullPath(artifactPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
                                  ?? throw new InvalidOperationException("Artifact path must include a directory."));

        await File.WriteAllTextAsync(destinationPath, content, cancellationToken);
        return new ArtifactWriteResult(kind, ProviderName, destinationPath);
    }

    string GetSafeFullPath(string artifactPath)
    {
        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rootPath));
        var normalizedArtifactPath = artifactPath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var destinationPath = Path.GetFullPath(Path.Combine(root, normalizedArtifactPath));

        if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Artifact path escaped the configured local storage root.");

        return destinationPath;
    }
}
