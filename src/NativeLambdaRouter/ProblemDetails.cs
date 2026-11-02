using System.Text.Json.Serialization;

namespace NativeLambdaRouter;

/// <summary>
/// Selects the wire format the router uses for the error responses it produces itself
/// (exception mapping, authorization failures and route-not-found).
/// </summary>
public enum ErrorResponseFormat
{
    /// <summary>
    /// Proprietary <c>application/json</c> envelope (<c>{ "error": "...", "details": "..." }</c>).
    /// Default in 2.x; scheduled to be replaced by <see cref="ProblemDetails"/> as the default in 3.0.
    /// </summary>
    LegacyEnvelope = 0,

    /// <summary>
    /// RFC 9457 <c>application/problem+json</c> document (<see cref="NativeLambdaRouter.ProblemDetails"/>).
    /// Opt in by overriding <see cref="RoutedApiGatewayFunction.ErrorResponseFormat"/>.
    /// </summary>
    ProblemDetails = 1,
}

/// <summary>
/// RFC 9457 problem document, served as <c>application/problem+json</c>.
/// Carries the five RFC members plus the Swepay extensions <see cref="TraceId"/> and <see cref="Errors"/>.
/// </summary>
/// <remarks>
/// Never put stack traces, raw exception messages from unexpected faults, secrets or PII in any member.
/// </remarks>
public sealed class ProblemDetails
{
    /// <summary>Stable URI identifying the problem class, always under the Swepay error catalog.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Short, human-readable summary of the problem class.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>HTTP status code of this occurrence.</summary>
    [JsonPropertyName("status")]
    public required int Status { get; init; }

    /// <summary>Human-readable explanation specific to this occurrence.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>URI reference identifying this occurrence; the router uses the request path.</summary>
    [JsonPropertyName("instance")]
    public string? Instance { get; init; }

    /// <summary>Correlation id so the caller can quote it to support and engineering can find the logs.</summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    /// <summary>Field-level failures. Present only on validation problems.</summary>
    [JsonPropertyName("errors")]
    public IReadOnlyList<ProblemFieldError>? Errors { get; init; }
}

/// <summary>
/// One field-level failure inside <see cref="ProblemDetails.Errors"/>.
/// </summary>
public sealed class ProblemFieldError
{
    /// <summary>Name of the offending field (JSON path or property name).</summary>
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    /// <summary>What is wrong with the field and how to fix it.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

/// <summary>
/// Error-catalog slugs shared by every Swepay service and the helper that turns a slug into
/// the absolute <c>type</c> URI. Product-specific problems use their own slug
/// (e.g. <c>native-mail/tenant-suspended</c>) through <see cref="ProblemException"/>.
/// </summary>
public static class ProblemTypes
{
    /// <summary>Base URL of the canonical Swepay error catalog.</summary>
    public const string CatalogBaseUrl = "https://errors.swepay.com.br";

    /// <summary>400 — request failed validation.</summary>
    public const string ValidationFailed = "common/validation-failed";

    /// <summary>401 — authentication missing or invalid.</summary>
    public const string Unauthorized = "common/unauthorized";

    /// <summary>403 — authenticated but not allowed.</summary>
    public const string Forbidden = "common/forbidden";

    /// <summary>404 — resource or route does not exist.</summary>
    public const string NotFound = "common/not-found";

    /// <summary>409 — request conflicts with the current state.</summary>
    public const string Conflict = "common/conflict";

    /// <summary>429 — caller exceeded a rate limit.</summary>
    public const string TooManyRequests = "common/too-many-requests";

    /// <summary>500 — unexpected server fault.</summary>
    public const string InternalError = "common/internal-error";

    /// <summary>
    /// Returns <paramref name="slugOrUri"/> unchanged when it is already absolute; otherwise
    /// prefixes it with <see cref="CatalogBaseUrl"/>.
    /// </summary>
    public static string ToUri(string slugOrUri)
    {
        if (slugOrUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || slugOrUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return slugOrUri;
        }

        return $"{CatalogBaseUrl}/{slugOrUri.TrimStart('/')}";
    }
}
