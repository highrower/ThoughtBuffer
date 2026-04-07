using System;

namespace ThoughtBuffer.Formatting;

public static class MarkdownNoteBuilder
{
    public static string Build(Models.RecordingEntry entry, Models.SummaryResult summary, string transcript)
    {
        var bullets = string.Join(
            Environment.NewLine,
            summary.BulletPoints.Select(b => $"- {b}"));

        return $"""
# {summary.Title}

**Recorded:** {entry.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd hh:mm tt}
**Imported:** {entry.ImportedAtUtc.ToLocalTime():yyyy-MM-dd hh:mm tt}
**Audio File:** {entry.FileName}

## Summary
{bullets}

## Transcript
{transcript}
""";
    }
}