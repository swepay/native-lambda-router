namespace NativeLambdaRouter;

/// <summary>
/// Exception thrown when validation fails.
/// Returns HTTP 400 Bad Request.
/// </summary>
public class ValidationException : Exception
{
    /// <summary>
    /// Field-level failures, surfaced as the RFC 9457 <c>errors[]</c> extension when the
    /// router runs in <see cref="ErrorResponseFormat.ProblemDetails"/> mode.
    /// </summary>
    public IReadOnlyList<ProblemFieldError>? Errors { get; }

    /// <summary>
    /// Creates a new validation exception.
    /// </summary>
    public ValidationException(string message) : base(message) { }

    /// <summary>
    /// Creates a new validation exception carrying field-level failures.
    /// </summary>
    public ValidationException(string message, IReadOnlyList<ProblemFieldError> errors) : base(message)
    {
        Errors = errors;
    }

    /// <summary>
    /// Creates a new validation exception with inner exception.
    /// </summary>
    public ValidationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a resource is not found.
/// Returns HTTP 404 Not Found.
/// </summary>
public class NotFoundException : Exception
{
    /// <summary>
    /// Creates a new not found exception.
    /// </summary>
    public NotFoundException(string message) : base(message) { }

    /// <summary>
    /// Creates a new not found exception with inner exception.
    /// </summary>
    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when the user is not authorized.
/// Returns HTTP 401 Unauthorized.
/// </summary>
public class UnauthorizedException : Exception
{
    /// <summary>
    /// Creates a new unauthorized exception.
    /// </summary>
    public UnauthorizedException(string message) : base(message) { }

    /// <summary>
    /// Creates a new unauthorized exception with inner exception.
    /// </summary>
    public UnauthorizedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when the user is forbidden from accessing a resource.
/// Returns HTTP 403 Forbidden.
/// </summary>
public class ForbiddenException : Exception
{
    /// <summary>
    /// Creates a new forbidden exception.
    /// </summary>
    public ForbiddenException(string message) : base(message) { }

    /// <summary>
    /// Creates a new forbidden exception with inner exception.
    /// </summary>
    public ForbiddenException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when there is a conflict with the current state.
/// Returns HTTP 409 Conflict.
/// </summary>
public class ConflictException : Exception
{
    /// <summary>
    /// Creates a new conflict exception.
    /// </summary>
    public ConflictException(string message) : base(message) { }

    /// <summary>
    /// Creates a new conflict exception with inner exception.
    /// </summary>
    public ConflictException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when the caller exceeded a rate limit.
/// Returns HTTP 429 Too Many Requests.
/// <para>
/// When <see cref="RetryAfter"/> is set, the router emits a
/// <c>Retry-After</c> response header with delta-seconds per RFC 7231 §7.1.3
/// so well-behaved clients know how long to back off.
/// </para>
/// </summary>
public class TooManyRequestsException : Exception
{
    /// <summary>
    /// Suggested back-off duration. When set, the router translates this into a
    /// <c>Retry-After</c> header using delta-seconds (rounded up, minimum 1s).
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Creates a new rate-limit exception without a suggested back-off.
    /// </summary>
    public TooManyRequestsException(string message) : base(message) { }

    /// <summary>
    /// Creates a new rate-limit exception with a suggested back-off duration
    /// that will be surfaced to the caller via the <c>Retry-After</c> header.
    /// </summary>
    public TooManyRequestsException(string message, TimeSpan retryAfter) : base(message)
    {
        RetryAfter = ValidateRetryAfter(retryAfter);
    }

    /// <summary>
    /// Creates a new rate-limit exception with an inner exception.
    /// </summary>
    public TooManyRequestsException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Creates a new rate-limit exception with a suggested back-off duration
    /// and an inner exception.
    /// </summary>
    public TooManyRequestsException(string message, TimeSpan retryAfter, Exception innerException)
        : base(message, innerException)
    {
        RetryAfter = ValidateRetryAfter(retryAfter);
    }

    private static TimeSpan ValidateRetryAfter(TimeSpan retryAfter)
    {
        if (retryAfter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfter), retryAfter, "Retry-after must be greater than or equal to zero.");
        }

        return retryAfter;
    }
}

/// <summary>
/// Base class for domain exceptions that already know how to describe themselves as an
/// RFC 9457 problem. The router maps it to <see cref="Status"/> with the given
/// <see cref="Type"/>/<see cref="Title"/> and uses the exception message as <c>detail</c>,
/// so services declare product-specific problems as subclasses instead of reimplementing
/// the exception-to-problem mapping at the edge.
/// </summary>
/// <example>
/// <code>
/// public sealed class TenantSuspendedException : ProblemException
/// {
///     public TenantSuspendedException()
///         : base(403, "native-mail/tenant-suspended", "Tenant Suspended",
///                "Reactivate the subscription in AWS Marketplace to resume sending email.") { }
/// }
/// </code>
/// </example>
public class ProblemException : Exception
{
    /// <summary>HTTP status code to return.</summary>
    public int Status { get; }

    /// <summary>
    /// Error-catalog slug (<c>{service}/{error}</c>) or absolute URI. Resolved through
    /// <see cref="ProblemTypes.ToUri"/> when the problem document is built.
    /// </summary>
    public string Type { get; }

    /// <summary>Short, human-readable title of the problem class.</summary>
    public string Title { get; }

    /// <summary>Field-level failures, when applicable.</summary>
    public IReadOnlyList<ProblemFieldError>? Errors { get; }

    /// <summary>For 429 responses: suggested back-off, surfaced as the <c>Retry-After</c> header.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Creates a problem exception. <paramref name="detail"/> becomes both
    /// <see cref="Exception.Message"/> and the problem <c>detail</c>, so it must be
    /// pedagogical and free of PII, secrets and internal identifiers.
    /// </summary>
    public ProblemException(
        int status,
        string type,
        string title,
        string detail,
        IReadOnlyList<ProblemFieldError>? errors = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(detail, innerException)
    {
        if (status is < 400 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Problem status must be a 4xx or 5xx HTTP status code.");
        }

        if (retryAfter is { } value && value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfter), retryAfter, "Retry-after must be greater than or equal to zero.");
        }

        Status = status;
        Type = type;
        Title = title;
        Errors = errors;
        RetryAfter = retryAfter;
    }
}
