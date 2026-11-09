using System.Text.Json.Serialization;

namespace TixFlow.Api.Contracts;

/// <summary>Success envelope. HTTP status remains authoritative; 204 responses have no body.</summary>
public sealed record ApiResponse<T>(T Data, string Message = "Request completed successfully.")
{
    public bool Success => true;
}

/// <summary>Problem Details compatible error envelope. Never pass raw exception messages here.</summary>
public sealed record ApiErrorResponse(
    string Type, string Title, int Status, string Detail, string Instance,
    string Code, string TraceId, bool Retryable = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IDictionary<string, string[]>? Errors = null)
{
    public bool Success => false;
    public string Message => Detail;

    public static ApiErrorResponse Create(HttpContext context, int status, string? code = null,
        string? message = null, IDictionary<string, string[]>? errors = null, bool retryable = false)
    {
        if (status is < 400 or > 599) throw new ArgumentOutOfRangeException(nameof(status));
        code = string.IsNullOrWhiteSpace(code) ? $"HTTP_{status}" : code;
        return new("about:blank", code, status,
            string.IsNullOrWhiteSpace(message) ? ApiMessages.ForStatus(status) : message,
            context.Request.Path.Value ?? "/", code, context.TraceIdentifier, retryable, errors);
    }

    public IResult ToResult() => Results.Json(this, statusCode: Status, contentType: "application/problem+json");
}

public static class ApiMessages
{
    public static string ForStatus(int status) => status switch
    {
        400 => "The request is invalid.",
        401 => "Authentication is required.",
        403 => "You do not have permission to perform this action.",
        404 => "The requested resource was not found.",
        409 => "The request conflicts with the current resource state.",
        422 => "One or more validation errors occurred.",
        429 => "Too many requests. Please try again later.",
        503 => "The service is temporarily unavailable.",
        _ when status >= 500 => "An unexpected server error occurred.",
        _ => "The request could not be completed."
    };
}
