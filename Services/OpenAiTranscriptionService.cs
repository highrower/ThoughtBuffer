using System;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ThoughtBuffer.Services;

public class OpenAiTranscriptionService : ITranscriptionService
{
    private readonly HttpClient _httpClient;

    public OpenAiTranscriptionService(string apiKey)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();

        await using var stream = File.OpenRead(audioFilePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            Path.GetExtension(audioFilePath).ToLowerInvariant() switch
            {
                ".wav" => "audio/wav",
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                _ => "application/octet-stream"
            });

        form.Add(fileContent, "file", Path.GetFileName(audioFilePath));
        form.Add(new StringContent("gpt-4o-mini-transcribe"), "model");

        using var response = await _httpClient.PostAsync(
            "https://api.openai.com/v1/audio/transcriptions",
            form,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("text").GetString()
               ?? throw new InvalidOperationException("Transcript text missing.");
    }
}
