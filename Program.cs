using Microsoft.Extensions.Hosting;
using ThoughtBuffer.Options;
using ThoughtBuffer.Services;

var builder = Host.CreateApplicationBuilder(args);

var thoughtBufferOptions = builder.Configuration
    .GetThoughtBufferOptions();

var openAiOptions = builder.Configuration
    .GetOpenAiOptions();

var localStorageOptions = builder.Configuration
    .GetLocalStorageOptions();

openAiOptions.ApiKey = ResolveOpenAiApiKey(openAiOptions);

var solutionRoot = ResolveSolutionRoot(thoughtBufferOptions);
var devicePath = ResolveDevicePath(thoughtBufferOptions);
var recordingsPath = Path.Combine(devicePath, thoughtBufferOptions.RecordingFolder);
var archivePath = Path.Combine(devicePath, thoughtBufferOptions.ArchiveFolder);
var appPaths = localStorageOptions.ToAppPaths(recordingsPath, archivePath, "Console");
var pythonExe = ResolveConfiguredPath(
    thoughtBufferOptions.PythonExePath,
    Path.Combine(solutionRoot, ".venv", "Scripts", "python.exe"));
var scriptPath = ResolveConfiguredPath(
    thoughtBufferOptions.FilterScriptPath,
    Path.Combine(solutionRoot, "python", "filter_audio.py"));

var filtrationService = new PythonAudioFilterService(pythonExe, scriptPath);
var transcriptionService = new OpenAiTranscriptionService(openAiOptions.ApiKey, openAiOptions.TranscriptionModel);
var summarizationService = new OpenAiSummarizationService(openAiOptions.ApiKey, openAiOptions.SummarizationModel);

Console.WriteLine("Options: "                                                +
                  "\n\t1: filter down all audio "                            +
                  "\n\t2: send filtered audio to be transcribed/summarized " +
                  "\n\t3: 1 + 2 "                                            +
                  "\n\tQ: Quit");
var response = Console.ReadLine();

Directory.CreateDirectory(appPaths.appFolder);
Directory.CreateDirectory(appPaths.copyFileFolder);
Directory.CreateDirectory(appPaths.transcriptFolder);
Directory.CreateDirectory(appPaths.notesFolder);
Directory.CreateDirectory(appPaths.filteredFolder);

await ThoughtBuffer.ThoughtBuffer.Main(response, appPaths, filtrationService, transcriptionService, summarizationService);

Console.WriteLine("Done.");

static string ResolveOpenAiApiKey(OpenAiOptions options)
{
    var apiKey = !string.IsNullOrWhiteSpace(options.ApiKey)
        ? options.ApiKey
        : Environment.GetEnvironmentVariable("THOUGHT_BUFFER_OPENAI_KEY");

    return !string.IsNullOrWhiteSpace(apiKey)
        ? apiKey
        : throw new InvalidOperationException("OpenAI API key not found. Set OpenAI:ApiKey or THOUGHT_BUFFER_OPENAI_KEY.");
}

static string ResolveSolutionRoot(ThoughtBufferOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.SolutionRoot))
        return Environment.ExpandEnvironmentVariables(options.SolutionRoot);

    return Directory.GetCurrentDirectory();
}

static string ResolveDevicePath(ThoughtBufferOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.DevicePath))
        return Environment.ExpandEnvironmentVariables(options.DevicePath);

    var baseAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(baseAppData, "ThoughtBuffer", "RecorderDevice");
}

static string ResolveConfiguredPath(string configuredPath, string defaultPath)
{
    var path = string.IsNullOrWhiteSpace(configuredPath)
        ? defaultPath
        : configuredPath;

    return Environment.ExpandEnvironmentVariables(path);
}
