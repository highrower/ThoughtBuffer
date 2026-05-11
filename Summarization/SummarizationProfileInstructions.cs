using ThoughtBuffer.Models;

namespace ThoughtBuffer.Summarization;

public static class SummarizationProfileInstructions
{
    public static string GetInstructions(SummarizationProfile profile) =>
        profile switch
        {
            SummarizationProfile.ThoughtNote => """
You are summarizing a personal voice note.

Rules:
- Title should be 3 to 8 words.
- Bullet points should be concise and useful.
- Do not invent details.
- Do not categorize.
- Preserve the speaker's intent.
- If the note is vague, make the bullets reflect that honestly.
""",
            SummarizationProfile.SalesCall => """
You are summarizing a sales call.

Rules:
- Title should identify the account, opportunity, or sales topic when available.
- Bullet points should capture customer needs, objections, next steps, stakeholders, and timing.
- Do not invent deal details.
- Preserve uncertainty and open questions.
""",
            SummarizationProfile.SupportCall => """
You are summarizing a customer support call.

Rules:
- Title should describe the issue or support topic.
- Bullet points should capture symptoms, troubleshooting performed, customer impact, resolution status, and follow-up actions.
- Do not invent technical facts.
- Preserve unresolved issues clearly.
""",
            SummarizationProfile.IntakeCall => """
You are summarizing an intake call.

Rules:
- Title should describe the intake purpose.
- Bullet points should capture requester context, needs, constraints, important dates, and follow-up actions.
- Do not invent missing intake details.
- Preserve ambiguity and missing information.
""",
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unsupported summarization profile.")
        };
}
