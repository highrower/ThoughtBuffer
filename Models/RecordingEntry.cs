namespace ThoughtBuffer.Models;

public record RecordingEntry(
    string FileName,
    string OriginalPath,
    string? CopiedPath,
    string? FilteredPath,
    long FileSize,
    DateTime LastWriteTimeUtc,
    DateTime ImportedAtUtc
);