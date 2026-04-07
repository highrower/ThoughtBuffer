using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;
using ThoughtBuffer.Formatting;
using ThoughtBuffer.Models;
using ThoughtBuffer.Services;

namespace ThoughtBuffer;

public class ThoughtBuffer
{
	static string[] _files;
	public static async Task Main(
		string? choice,
		AppPaths paths,
		IAudioFilterService filtrationService,
		ITranscriptionService transcriber,
		ISummarizationService summarizer)
	{
		if (GetChoice(choice) == 'Q')
			return;
		
		_files = Directory.GetFiles(paths.recordingsPath, "*.wav");
		if (_files.Length == 0)
			_files = Directory.GetFiles(paths.recordingsPath, "*.mp3");
		
		switch (GetChoice(choice))
		{
			case '1':
				await FilterFiles(paths, filtrationService);
				break;
			case '2':
				await TranscribeAndSummarize(paths, transcriber, summarizer, false);
				break;
			case '3':
				await FilterFiles(paths, filtrationService);
				await TranscribeAndSummarize(paths, transcriber, summarizer);
				break;
			default:
				return;
		}
		if (!Directory.Exists(paths.recordingsPath) || !Directory.Exists(paths.archivePath))
			throw new DirectoryNotFoundException("Recorder or Archive not found.");
	}

	static char GetChoice(string? choice) =>
		choice is "1" or "2" or "3" ? choice[0] : 'Q';

	static async Task FilterFiles(AppPaths paths, IAudioFilterService filterer)
	{
		foreach (var file in _files)
		{
			var baseName        = Path.GetFileNameWithoutExtension(file);
			var destinationPath = Path.Combine(paths.filteredFolder, $"{baseName}.trimmed.wav");			
			
			var filteredPath = await filterer.FilterFile(file, destinationPath);
			Console.WriteLine($"Filtered {file} to {filteredPath}");
		}
	}

	static async Task TranscribeAndSummarize(
		AppPaths paths,
		ITranscriptionService transcriber,
		ISummarizationService summarizer,
		bool filtered = true)
	{
		var imported = new List<RecordingEntry>();
		var movedPath = filtered ? paths.filteredFolder : paths.copyFileFolder;

		foreach (var file in _files)
		{
			var fileName        = Path.GetFileName(file);
			var copyPath = Path.Combine(movedPath, fileName);
    
			File.Copy(file, copyPath, true);
			Console.WriteLine($"Copied {fileName} to {copyPath}");

			var info = new FileInfo(copyPath);

			imported.Add(new RecordingEntry(
							 fileName,
							 file,
							 filtered ? null : copyPath,
							 filtered ? copyPath : null,
							 info.Length,
							 info.LastWriteTimeUtc,
							 DateTime.UtcNow
						 ));

			// File.Move(file, Path.Combine(archivePath, fileName)); TODO: Move file to archive after processing,
			// but for now we will just copy it to avoid issues during development.
		}

		foreach (var entry in imported)
		{
			var transcript = await transcriber.TranscribeAsync(entry.FilteredPath);
			Console.WriteLine($"Transcript for {entry.FileName}:");
			Console.WriteLine(transcript);

			var transcriptPath = Path.Combine(paths.transcriptFolder, Path.ChangeExtension(entry.FileName, ".txt"));
			await File.WriteAllTextAsync(transcriptPath, transcript);

			var summary = await summarizer.SummarizeAsync(transcript);

			var notePath = Path.Combine(paths.notesFolder, Path.ChangeExtension(entry.FileName, ".md"));
			var markdown = MarkdownNoteBuilder.Build(entry, summary, transcript);
			await File.WriteAllTextAsync(notePath, markdown);
		}

		var json = JsonSerializer.Serialize(imported, new JsonSerializerOptions
		{
			WriteIndented = true,
		});

		var jsonPath = Path.Combine(paths.appFolder, "recordings.json");
		await File.WriteAllTextAsync(jsonPath, json);
	}
}