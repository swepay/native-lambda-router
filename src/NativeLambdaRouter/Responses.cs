using System.Text.Json.Serialization;

namespace NativeLambdaRouter;

/// <summary>
/// Standard error response for API Gateway.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>
    /// The error message.
    /// </summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    /// <summary>
    /// Additional details about the error.
    /// </summary>
    [JsonPropertyName("details")]
    public string? Details { get; init; }
}

/// <summary>
/// Standard health check response.
/// </summary>
public sealed class HealthCheckResponse
{
    /// <summary>
    /// The health status.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// The function name.
    /// </summary>
    [JsonPropertyName("function")]
    public string? Function { get; init; }

    /// <summary>
    /// The timestamp of the health check.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    /// <summary>
    /// The environment name.
    /// </summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; init; }
}

/// <summary>
/// Route not found error response.
/// </summary>
public sealed class RouteNotFoundResponse
{
    /// <summary>
    /// The error message.
    /// </summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    /// <summary>
    /// The requested path.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>
    /// The HTTP method used.
    /// </summary>
    [JsonPropertyName("method")]
    public string? Method { get; init; }
}

/// <summary>
/// JSON serializer context for NativeLambdaRouter internal types.
/// This context provides AOT-compatible serialization for error responses
/// and health check responses.
/// </summary>
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthCheckResponse))]
[JsonSerializable(typeof(RouteNotFoundResponse))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class RouterJsonContext : JsonSerializerContext
{
}
