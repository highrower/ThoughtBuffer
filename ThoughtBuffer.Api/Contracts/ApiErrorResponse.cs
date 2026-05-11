namespace ThoughtBuffer.Api.Contracts;

public record ApiErrorResponse(
    string Error,
    string Status
);
