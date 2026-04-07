namespace ThoughtBuffer.Services;

public interface IAudioFilterService
{
	public Task<string> FilterFile(string inputPath, string outputPath, CancellationToken cancellationToken = default);
}