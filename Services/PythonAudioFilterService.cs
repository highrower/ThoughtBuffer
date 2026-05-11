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

        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var finalOutputPath = Path.ChangeExtension(outputPath, ".mp3");

        if (File.Exists(finalOutputPath))
        {
            Console.WriteLine($"[{baseName}] Filtered output already exists. Skipping.");
            return finalOutputPath;
        }

        var tempDirectory = Path.Combine(outputDirectory, "_temp");
        Directory.CreateDirectory(tempDirectory);

        var tempInputWavPath = Path.Combine(tempDirectory, $"{baseName}.16k.wav");
        var tempFilteredWavPath = Path.Combine(tempDirectory, $"{baseName}.trimmed.wav");

        Console.WriteLine($"[{baseName}] Starting filter...");
        Console.WriteLine($"[{baseName}] Step 1/3: converting mp3 -> 16k wav");

        try
        {
            var ffmpegToWavArgs =
                $"-y -i \"{inputPath}\" " +
                "-vn -ac 1 -ar 16000 -sample_fmt s16 " +
                $"\"{tempInputWavPath}\"";

            await RunProcessAsync(
                ffmpegPath,
                ffmpegToWavArgs,
                cancellationToken,
                heartbeatLabel: $"[{baseName}] ffmpeg conversion still running");

            Console.WriteLine($"[{baseName}] Step 2/3: running python VAD");

            var pythonArgs =
                $"\"{scriptPath}\" " +
                $"--input \"{tempInputWavPath}\" " +
                $"--output \"{tempFilteredWavPath}\"";

            var stdout = await RunProcessAsync(
                pythonExe,
                pythonArgs,
                cancellationToken,
                heartbeatLabel: $"[{baseName}] python filter still running");

            var wavSourceForMp3 = tempFilteredWavPath;

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
                        var segmentCount = segmentsProp.GetInt32();
                        Console.WriteLine($"[{baseName}] Segments kept: {segmentCount}");

                        if (segmentCount == 0)
                        {
                            Console.WriteLine($"[{baseName}] No speech detected. Skipping filtered output.");
                            return string.Empty;
                            
                        }
                    }
                }
                catch (JsonException)
                {
                    if (!File.Exists(tempFilteredWavPath))
                        throw new InvalidOperationException($"Python filter script returned unexpected output: {stdout}");
                }
            }

            if (!File.Exists(wavSourceForMp3))
                throw new InvalidOperationException("No wav file exists to convert back to mp3.");

            Console.WriteLine($"[{baseName}] Step 3/3: converting filtered wav -> mp3");

            var ffmpegToMp3Args =
                $"-y -i \"{wavSourceForMp3}\" " +
                "-ac 1 -codec:a libmp3lame -b:a 64k " +
                $"\"{finalOutputPath}\"";

            await RunProcessAsync(
                ffmpegPath,
                ffmpegToMp3Args,
                cancellationToken,
                heartbeatLabel: $"[{baseName}] ffmpeg mp3 conversion still running");

            if (!File.Exists(finalOutputPath))
                throw new InvalidOperationException("Final filtered mp3 was not created.");

            Console.WriteLine($"[{baseName}] Finished: {finalOutputPath}");
            return finalOutputPath;
        }
        finally
        {
            TryDelete(tempInputWavPath);
            TryDelete(tempFilteredWavPath);
        }
    }

    static async Task<string> RunProcessAsync(
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

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}