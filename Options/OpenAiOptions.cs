namespace ThoughtBuffer.Options;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = "";
    public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
    public string SummarizationModel { get; set; } = "gpt-4.1-mini";
}
