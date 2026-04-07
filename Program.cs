using System.Text.Json;
using ThoughtBuffer.Services;
using ThoughtBuffer.Models;
using ThoughtBuffer.Formatting;
using ThoughtBuffer;

var apiKey = Environment.GetEnvironmentVariable("THOUGHT_BUFFER_OPENAI_KEY")
             ?? throw new InvalidOperationException("API key not found in environment variables.");

const string devicePath      = @"E:\REC_FILE";
const string recordingFolder = "FOLDER01";
const string archiveFolder   = "Archive";
const string solutionRoot    = @"G:\Projects\Dotnet\ThoughtBuffer\";

var recordingsPath       = Path.Combine(devicePath, recordingFolder);
var archivePath          = Path.Combine(devicePath, archiveFolder);
var baseAppData          = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var appFolder            = Path.Combine(baseAppData,  "ThoughtBuffer");
var copyFileFolder       = Path.Combine(appFolder,    "Recordings");
var filteredFolder       = Path.Combine(appFolder,    "Filtered");
var transcriptFolder     = Path.Combine(appFolder,    "Transcripts");
var notesFolder          = Path.Combine(appFolder,    "Notes");
var pythonExe            = Path.Combine(solutionRoot, ".venv",  "Scripts", "python.exe");
var scriptPath           = Path.Combine(solutionRoot, "python", "filter_audio.py");

var filtrationService    = new PythonAudioFilterService(pythonExe, scriptPath);
var transcriptionService = new OpenAiTranscriptionService(apiKey);
var summarizationService = new OpenAiSummarizationService(apiKey);

Console.WriteLine("Options: "                                                +
                  "\n\t1: filter down all audio "                            +
                  "\n\t2: send filtered audio to be transcribed/summarized " +
                  "\n\t3: 1 + 2 "                                            +
                  "\n\tQ: Quit");
var response = Console.ReadLine();

Directory.CreateDirectory(copyFileFolder);
Directory.CreateDirectory(transcriptFolder);
Directory.CreateDirectory(notesFolder);
Directory.CreateDirectory(filteredFolder);

var appPaths = new AppPaths(appFolder, recordingsPath, copyFileFolder, filteredFolder, archivePath, transcriptFolder, notesFolder);

await ThoughtBuffer.ThoughtBuffer.Main(response, appPaths, filtrationService, transcriptionService, summarizationService);

Console.WriteLine("Done.");