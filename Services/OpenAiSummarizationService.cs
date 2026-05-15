using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ThoughtBuffer.Models;
using ThoughtBuffer.Summarization;

namespace ThoughtBuffer.Services;

public sealed class OpenAiSummarizationService : ISummarizationService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OpenAiSummarizationService(string apiKey, string model = "gpt-4.1-mini")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Summarization model is required.", nameof(model));

        _model = model;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15),
        };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<SummaryResult> SummarizeAsync(
        string transcript,
        SummarizationProfile profile = SummarizationProfile.ThoughtNote,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            throw new ArgumentException("Transcript cannot be empty.", nameof(transcript));

        var prompt = SummarizationProfileInstructions.GetInstructions(profile) + """

Return strict JSON only with this shape:
{
  "title": "short descriptive title",
  "bulletPoints": ["point 1", "point 2", "point 3", ...]
}

Transcript:
""" + transcript;

        var requestBody = new
        {
            model = _model,
            input = prompt,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "summary_result",
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            title = new
                            {
                                type = "string"
                            },
                            bulletPoints = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "string"
                                }
                            }
                        },
                        required = new[] { "title", "bulletPoints" }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(
            "https://api.openai.com/v1/responses",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(responseJson);

        var outputText = doc.RootElement
            .GetProperty("output")
            [0]
            .GetProperty("content")
            [0]
            .GetProperty("text").GetString();

        if (string.IsNullOrWhiteSpace(outputText))
            throw new InvalidOperationException("Model returned empty summary output.");

        var result = JsonSerializer.Deserialize<SummaryDto>(outputText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (result is null || string.IsNullOrWhiteSpace(result.Title) || result.BulletPoints is null || result.BulletPoints.Count == 0)
            throw new InvalidOperationException("Failed to parse summary JSON.");

        return new SummaryResult(result.Title, result.BulletPoints);
    }

    private sealed class SummaryDto
    {
        public string Title { get; set; } = "";
        public List<string> BulletPoints { get; set; } = new();
    }
}
