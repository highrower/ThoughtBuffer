using System.Net.Http.Headers;
using System.Text.Json;

namespace ThoughtBuffer.Services;

public class OpenAiTranscriptionService : ITranscriptionService
{
	readonly HttpClient _httpClient;
	readonly string     _model;

	public OpenAiTranscriptionService(string apiKey, string model = "gpt-4o-mini-transcribe")
	{
		if (string.IsNullOrWhiteSpace(apiKey))
			throw new ArgumentException("API key is required.", nameof(apiKey));

		if (string.IsNullOrWhiteSpace(model))
			throw new ArgumentException("Transcription model is required.", nameof(model));

		_model = model;
		_httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(15)
		};

		_httpClient.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", apiKey);
	}

	public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken cancellationToken = default)
	{
		using var form = new MultipartFormDataContent();

		await using var stream      = File.OpenRead(audioFilePath);
		using var       fileContent = new StreamContent(stream);
		fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");

		form.Add(fileContent,                "file", Path.GetFileName(audioFilePath));
		form.Add(new StringContent(_model), "model");

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
