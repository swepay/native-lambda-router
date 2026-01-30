namespace NativeLambdaRouter;

/// <summary>
/// Represents a route configuration that maps an HTTP endpoint to a command handler.
/// </summary>
public sealed class RouteDefinition
{
    /// <summary>
    /// The HTTP method (GET, POST, PUT, DELETE, PATCH).
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// The route path pattern (e.g., "/products", "/products/{id}").
    /// Supports path parameters with {paramName} syntax.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Factory function to create the command from the request context.
    /// </summary>
    public required Func<RouteContext, object> CommandFactory { get; init; }

    /// <summary>
    /// The type of the command response.
    /// </summary>
    public required Type ResponseType { get; init; }

    /// <summary>
    /// Optional custom response transformer.
    /// </summary>
    public Func<object, object>? ResponseTransformer { get; init; }

    /// <summary>
    /// Optional response content type for this route (e.g., text/html).
    /// </summary>
    public string? ResponseContentType { get; set; }

    /// <summary>
    /// Optional response headers for this route.
    /// </summary>
    public Dictionary<string, string> ResponseHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this route requires authentication. Default is true.
    /// </summary>
    [Obsolete("Use AuthorizationOptions instead. This property will be removed in a future version.")]
    public bool RequiresAuth { get; init; } = true;

    /// <summary>
    /// Authorization options for this route.
    /// </summary>
    public RouteAuthorizationOptions AuthorizationOptions { get; init; } = new();
}

/// <summary>
/// Context passed to the command factory containing request information.
/// </summary>
public sealed class RouteContext
{
    /// <summary>
    /// The raw request body as string.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// Path parameters extracted from the route (e.g., {id} -> "123").
    /// </summary>
    public Dictionary<string, string> PathParameters { get; init; } = [];

    /// <summary>
    /// Query string parameters.
    /// </summary>
    public Dictionary<string, string> QueryParameters { get; init; } = [];

    /// <summary>
    /// Request headers.
    /// </summary>
    public Dictionary<string, string> Headers { get; init; } = [];

    /// <summary>
    /// The authenticated user's claims (if available).
    /// </summary>
    public Dictionary<string, string> Claims { get; init; } = [];
}
