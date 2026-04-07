using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ThoughtBuffer.Services;

public sealed class OpenAiSummarizationService : ISummarizationService
{
    private readonly HttpClient _httpClient;

    public OpenAiSummarizationService(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<Models.SummaryResult> SummarizeAsync(
        string transcript,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            throw new ArgumentException("Transcript cannot be empty.", nameof(transcript));

        var prompt = """
You are summarizing a personal voice note.

Return strict JSON only with this shape:
{
  "title": "short descriptive title",
  "bulletPoints": ["point 1", "point 2", "point 3", ...]
}

Rules:
- Title should be 3 to 8 words.
- Bullet points should be concise and useful.
- Do not invent details.
- Do not categorize.
- Preserve the speaker's intent.
- If the note is vague, make the bullets reflect that honestly.
Transcript:
""" + transcript;

        var requestBody = new
        {
            model = "gpt-4.1-mini",
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

        return new Models.SummaryResult(result.Title, result.BulletPoints);
    }

    private sealed class SummaryDto
    {
        public string Title { get; set; } = "";
        public List<string> BulletPoints { get; set; } = new();
    }
}