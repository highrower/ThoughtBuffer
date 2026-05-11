using ThoughtBuffer.Models;

namespace ThoughtBuffer.Services;

public interface ISummarizationService
{
    Task<SummaryResult> SummarizeAsync(
        string transcript,
        SummarizationProfile profile = SummarizationProfile.ThoughtNote,
        CancellationToken cancellationToken = default);
}
