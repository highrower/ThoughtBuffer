using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ThoughtBuffer.Storage;

public sealed class AzureBlobArtifactStorage : IArtifactStorage
{
    readonly BlobContainerClient _containerClient;

    public AzureBlobArtifactStorage(string connectionString, string containerName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Azure Blob Storage connection string is required.", nameof(connectionString));

        if (string.IsNullOrWhiteSpace(containerName))
            throw new ArgumentException("Azure Blob Storage container name is required.", nameof(containerName));

        _containerClient = new BlobContainerClient(connectionString, containerName);
    }

    public string ProviderName => "AzureBlob";

    public async Task<ArtifactWriteResult> SaveFileAsync(
        ArtifactKind kind,
        string artifactPath,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobName = NormalizeBlobName(artifactPath);
        var blobClient = _containerClient.GetBlobClient(blobName);
        await using var stream = File.OpenRead(sourcePath);
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken);

        return new ArtifactWriteResult(kind, ProviderName, blobName, blobClient.Uri);
    }

    public async Task<ArtifactWriteResult> SaveTextAsync(
        ArtifactKind kind,
        string artifactPath,
        string content,
        CancellationToken cancellationToken = default)
    {
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobName = NormalizeBlobName(artifactPath);
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(BinaryData.FromString(content), overwrite: true, cancellationToken);

        return new ArtifactWriteResult(kind, ProviderName, blobName, blobClient.Uri);
    }

    static string NormalizeBlobName(string artifactPath) =>
        artifactPath.Replace('\\', '/').TrimStart('/');
}
