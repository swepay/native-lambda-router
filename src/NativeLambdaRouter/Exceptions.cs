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
