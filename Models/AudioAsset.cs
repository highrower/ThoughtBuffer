namespace ThoughtBuffer.Models;

public record AudioAsset(
    string Id,
    string SessionId,
    string FileName,
    string OriginalPath,
    string? StoredPath,
    string? FilteredPath,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    DateTime ImportedAtUtc,
    string? ContentType = null
);
