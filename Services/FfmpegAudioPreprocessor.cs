using ThoughtBuffer.Models;
using System.Diagnostics;

namespace ThoughtBuffer.Services;

public class FfmpegAudioPreprocessor(string ffmpegPath = "ffmpeg") : IAudioFilterService
{
	public async Task<string> FilterFile(string inputPath, string outputPath, CancellationToken cancellationToken = default)
	{
		var args =
			$"-y -i \"{inputPath}\" "                                                                                                              +
			"-map 0:a:0 -vn " + 
			"-af \"silenceremove=start_periods=1:start_duration=0.2:start_threshold=-50dB:stop_periods=-1:stop_duration=2:stop_threshold=-35dB\" " +
			"-c:a libmp3lame -b:a 128k " +
			$"\"{outputPath}\"";	
		
		var psi = new ProcessStartInfo
		{
			FileName               = ffmpegPath,
			Arguments              = args,
			RedirectStandardError  = true,
			RedirectStandardOutput = true,
			UseShellExecute        = false,
			CreateNoWindow         = true,
		};
		
		using var process = Process.Start(psi)
							?? throw new InvalidOperationException("Failed to start ffmpeg.");

		await process.WaitForExitAsync(cancellationToken);

		var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

		return process.ExitCode != 0 ? throw new InvalidOperationException($"ffmpeg failed: {stderr}") : outputPath;
	}
}