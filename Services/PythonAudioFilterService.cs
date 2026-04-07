using System.Diagnostics;
using System.Text.Json;

namespace ThoughtBuffer.Services;

public sealed class PythonAudioFilterService(
    string pythonExe,
    string scriptPath,
    string ffmpegPath = "ffmpeg") : IAudioFilterService
{
    public async Task<string> FilterFile(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input audio file not found.", inputPath);

        if (!File.Exists(pythonExe))
            throw new FileNotFoundException("Python executable not found.", pythonExe);

        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Python filter script not found.", scriptPath);

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("Output path must include a directory.");

        Directory.CreateDirectory(outputDirectory);

        var finalOutputPath = Path.ChangeExtension(outputPath, ".wav");

        var tempDirectory = Path.Combine(outputDirectory, "_temp");
        Directory.CreateDirectory(tempDirectory);

        var tempWavPath = Path.Combine(
            tempDirectory,
            $"{Path.GetFileNameWithoutExtension(inputPath)}.16k.wav");

        try
        {
            var ffmpegArgs =
                $"-y -i \"{inputPath}\" " +
                "-vn -ac 1 -ar 16000 -sample_fmt s16 " +
                $"\"{tempWavPath}\"";

            await RunProcessAsync(ffmpegPath, ffmpegArgs, cancellationToken);

            var pythonArgs =
                $"\"{scriptPath}\" " +
                $"--input \"{tempWavPath}\" " +
                $"--output \"{finalOutputPath}\"";

            var stdout = await RunProcessAsync(pythonExe, pythonArgs, cancellationToken);

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                try
                {
                    using var doc = JsonDocument.Parse(stdout);

                    if (doc.RootElement.TryGetProperty("ok", out var okProp) && !okProp.GetBoolean())
                    {
                        var error = doc.RootElement.TryGetProperty("error", out var errorProp)
                            ? errorProp.GetString()
                            : "Python filter script reported failure.";

                        throw new InvalidOperationException(error);
                    }

                    if (doc.RootElement.TryGetProperty("segments", out var segmentsProp) &&
                        segmentsProp.ValueKind == JsonValueKind.Number &&
                        segmentsProp.GetInt32() == 0)
                    {
                        File.Copy(tempWavPath, finalOutputPath, true);
                    }
                }
                catch (JsonException)
                {
                    if (!File.Exists(finalOutputPath))
                        throw new InvalidOperationException($"Python filter script returned unexpected output: {stdout}");
                }
            }

            if (!File.Exists(finalOutputPath))
                throw new InvalidOperationException("Python filter script did not create the filtered output file.");

            return finalOutputPath;
        }
        finally
        {
            try
            {
                if (File.Exists(tempWavPath))
                    File.Delete(tempWavPath);
            }
            catch
            {
            }
        }
    }

    private static async Task<string> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} failed: {stderr}");

        return stdout.Trim();
    }
}