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

        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var tempWavPath = Path.Combine(tempDirectory, $"{baseName}.16k.wav");

        Console.WriteLine($"[{baseName}] Starting filter...");
        Console.WriteLine($"[{baseName}] Step 1/2: converting mp3 -> 16k wav");

        try
        {
            var ffmpegArgs =
                $"-y -i \"{inputPath}\" " +
                "-vn -ac 1 -ar 16000 -sample_fmt s16 " +
                $"\"{tempWavPath}\"";

            await RunProcessAsync(
                ffmpegPath,
                ffmpegArgs,
                cancellationToken,
                heartbeatLabel: $"[{baseName}] ffmpeg still running");

            Console.WriteLine($"[{baseName}] Step 2/2: running python VAD");

            var pythonArgs =
                $"\"{scriptPath}\" " +
                $"--input \"{tempWavPath}\" " +
                $"--output \"{finalOutputPath}\"";

            var stdout = await RunProcessAsync(
                pythonExe,
                pythonArgs,
                cancellationToken,
                heartbeatLabel: $"[{baseName}] python filter still running");

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
                        segmentsProp.ValueKind == JsonValueKind.Number)
                    {
                        Console.WriteLine($"[{baseName}] Segments kept: {segmentsProp.GetInt32()}");
                    }

                    if (doc.RootElement.TryGetProperty("segments", out var zeroSegProp) &&
                        zeroSegProp.ValueKind == JsonValueKind.Number &&
                        zeroSegProp.GetInt32() == 0)
                    {
                        File.Copy(tempWavPath, finalOutputPath, true);
                        Console.WriteLine($"[{baseName}] No speech detected, copied temp wav as fallback");
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

            Console.WriteLine($"[{baseName}] Finished: {finalOutputPath}");
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
        CancellationToken cancellationToken,
        string heartbeatLabel)
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

        while (!process.HasExited)
        {
            Console.WriteLine($"{heartbeatLabel}...");
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        }

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} failed: {stderr}");

        return stdout.Trim();
    }
}