namespace NativeLambdaRouter;

/// <summary>
/// Exception thrown when validation fails.
/// Returns HTTP 400 Bad Request.
/// </summary>
public class ValidationException : Exception
{
    /// <summary>
    /// Creates a new validation exception.
    /// </summary>
    public ValidationException(string message) : base(message) { }

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
        RetryAfter = retryAfter;
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
        RetryAfter = retryAfter;
    }
}
