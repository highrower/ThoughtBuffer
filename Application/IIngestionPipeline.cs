using ThoughtBuffer.Models;

namespace ThoughtBuffer.Application;

public interface IIngestionPipeline
{
    Task<IReadOnlyList<IngestionPipelineResult>> ProcessLocalAudioFilesAsync(
        IngestionSession session,
        IReadOnlyList<AudioAsset> audioAssets,
        AppPaths paths,
        CancellationToken cancellationToken = default);
}
