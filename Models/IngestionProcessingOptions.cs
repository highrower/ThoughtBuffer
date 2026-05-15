namespace ThoughtBuffer.Models;

public record IngestionProcessingOptions(
    ProcessingMode ProcessingMode = ProcessingMode.TranscribeAndSummarize,
    SummarizationProfile SummarizationProfile = SummarizationProfile.ThoughtNote
);
