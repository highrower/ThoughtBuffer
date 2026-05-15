using ThoughtBuffer.Models;

namespace ThoughtBuffer.Application;

public interface IIngestionPipeline
{
    Task<IReadOnlyList<IngestionPipelineResult>> ProcessBatchAudioAsync(
        BatchIngestionRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IngestionPipelineResult>> ProcessLocalAudioFilesAsync(
        IngestionSession session,
        IReadOnlyList<AudioAsset> audioAssets,
        AppPaths paths,
        IngestionProcessingOptions? processingOptions = null,
        CancellationToken cancellationToken = default);
}
