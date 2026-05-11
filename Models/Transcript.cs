namespace ThoughtBuffer.Models;

public record Transcript(
    string Id,
    string SessionId,
    string AudioAssetId,
    string Text,
    DateTime CreatedAtUtc
);
