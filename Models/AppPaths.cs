using ThoughtBuffer.Services;

namespace ThoughtBuffer.Models;

public record AppPaths(
    string appFolder,
    string recordingsPath,
    string copyFileFolder,
    string filteredFolder,
    string archivePath,
    string transcriptFolder,
    string notesFolder
);