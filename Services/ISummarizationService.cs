using System;

namespace ThoughtBuffer.Services;

public interface ISummarizationService
{
    Task<Models.SummaryResult> SummarizeAsync(string transcript, CancellationToken cancellationToken = default);
}
