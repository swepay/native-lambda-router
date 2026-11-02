using System.Globalization;
using System.Net;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using NativeMediator;

namespace NativeLambdaRouter;

/// <summary>
/// Base class for API Gateway Lambda functions with routing support.
/// Provides automatic route matching, error handling, and health checks.
/// </summary>
public abstract class RoutedApiGatewayFunction
{
    private const string ProblemJsonContentType = "application/problem+json";

    private readonly IMediator? _mediator;
    private readonly IServiceProvider? _serviceProvider;
    private readonly RouteMatcher _routeMatcher;
    private readonly AuthorizationService _authorizationService;

    private static readonly Dictionary<string, string> _jsonContentTypeHeader = new()
    {
        { "Content-Type", "application/json" }
    };

    /// <summary>
    /// The mediator instance for sending commands.
    /// When using IServiceProvider constructor, this returns a scoped mediator
    /// that should only be accessed within the request context.
    /// </summary>
    protected IMediator Mediator => _mediator ?? throw new InvalidOperationException(
        "Mediator is not available. Use GetScopedMediator() with the IServiceProvider constructor.");

    /// <summary>
    /// Creates a new routed API Gateway function with a pre-configured mediator.
    /// Use this constructor when IMediator is registered as Singleton.
    /// </summary>
    /// <param name="mediator">The mediator for handling commands.</param>
    protected RoutedApiGatewayFunction(IMediator mediator)
    {
        _mediator = mediator;
        _serviceProvider = null;

        var builder = new RouteBuilder();
        ConfigureRoutes(builder);
        _routeMatcher = new RouteMatcher(builder.Routes);

        var authBuilder = new AuthorizationBuilder();
        ConfigureAuthorization(authBuilder);
        _authorizationService = new AuthorizationService(authBuilder.Policies);
    }

    /// <summary>
    /// Creates a new routed API Gateway function with a service provider.
    /// Use this constructor when IMediator is registered as Scoped.
    /// The mediator will be resolved within a scope for each request.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
    protected RoutedApiGatewayFunction(IServiceProvider serviceProvider)
    {
        _mediator = null;
        _serviceProvider = serviceProvider;

        var builder = new RouteBuilder();
        ConfigureRoutes(builder);
        _routeMatcher = new RouteMatcher(builder.Routes);

        var authBuilder = new AuthorizationBuilder();
        ConfigureAuthorization(authBuilder);
        _authorizationService = new AuthorizationService(authBuilder.Policies);
    }

    /// <summary>
    /// Gets whether this function uses a service provider for scoped resolution.
    /// </summary>
    protected bool UsesScopedMediator => _serviceProvider != null;

    /// <summary>
    /// Wire format of the error responses the router produces (exception mapping,
    /// authorization failures, route-not-found). Defaults to
    /// <see cref="NativeLambdaRouter.ErrorResponseFormat.LegacyEnvelope"/> in 2.x; override to return
    /// <see cref="NativeLambdaRouter.ErrorResponseFormat.ProblemDetails"/> to emit RFC 9457
    /// <c>application/problem+json</c> (Swepay GS-04). The default flips in 3.0.
    /// </summary>
    protected virtual ErrorResponseFormat ErrorResponseFormat => ErrorResponseFormat.LegacyEnvelope;

    /// <summary>
    /// Creates a new scope and returns the scoped mediator.
    /// The caller is responsible for disposing the scope.
    /// </summary>
    protected (IServiceScope scope, IMediator mediator) CreateScopedMediator()
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not available. Use the IServiceProvider constructor.");

        var scope = _serviceProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return (scope, mediator);
    }

    /// <summary>
    /// Override this method to configure authorization policies.
    /// </summary>
    /// <example>
    /// <code>
    /// protected override void ConfigureAuthorization(AuthorizationBuilder auth)
    /// {
    ///     auth.AddPolicy("admin_only", policy => policy
    ///         .RequireRole("admin"));
    ///
    ///     auth.AddPolicy("api_access", policy => policy
    ///         .RequireClaim("scope", "api:read", "api:write"));
    /// }
    /// </code>
    /// </example>
    protected virtual void ConfigureAuthorization(AuthorizationBuilder auth)
    {
        // Default: no policies configured
    }

    /// <summary>
    /// Override this method to configure routes for the function.
    /// </summary>
    /// <example>
    /// <code>
    /// protected override void ConfigureRoutes(IRouteBuilder routes)
    /// {
    ///     routes.MapGet&lt;GetItemsCommand, GetItemsResponse&gt;("/items", ctx => new GetItemsCommand());
    ///     routes.MapPost&lt;CreateItemCommand, CreateItemResponse&gt;("/items", ctx => Deserialize&lt;CreateItemCommand&gt;(ctx.Body));
    /// }
    /// </code>
    /// </example>
    protected abstract void ConfigureRoutes(IRouteBuilder routes);

    /// <summary>
    /// Override this method to handle the matched route and execute the command.
    /// This is where you call the mediator with the specific command type.
    /// The mediator instance is provided to ensure proper scoping of dependencies.
    /// </summary>
    /// <example>
    /// <code>
    /// protected override async Task&lt;object&gt; ExecuteCommandAsync(RouteMatch match, RouteContext context, IMediator mediator)
    /// {
    ///     var command = match.Route.CommandFactory(context);
    ///     return command switch
    ///     {
    ///         GetItemsCommand cmd => await mediator.Send(cmd),
    ///         CreateItemCommand cmd => await mediator.Send(cmd),
    ///         _ => throw new InvalidOperationException($"Unknown command: {command.GetType().Name}")
    ///     };
    /// }
    /// </code>
    /// </example>
    protected abstract Task<object> ExecuteCommandAsync(RouteMatch match, RouteContext context, IMediator mediator);

    /// <summary>
    /// Override this method to provide custom JSON serialization for Native AOT.
    /// Use source-generated JSON serializer context for AOT compatibility.
    /// </summary>
    /// <remarks>
    /// You MUST override this method to provide AOT-compatible serialization.
    /// Use a JsonSerializerContext with [JsonSerializable] attributes for all response types.
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override string SerializeResponse(object response)
    /// {
    ///     return response switch
    ///     {
    ///         GetItemsResponse r => JsonSerializer.Serialize(r, AppJsonContext.Default.GetItemsResponse),
    ///         CreateItemResponse r => JsonSerializer.Serialize(r, AppJsonContext.Default.CreateItemResponse),
    ///         ErrorResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.ErrorResponse),
    ///         HealthCheckResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.HealthCheckResponse),
    ///         _ => throw new NotSupportedException($"No serializer for {response.GetType().Name}")
    ///     };
    /// }
    /// </code>
    /// </example>
    protected abstract string SerializeResponse(object response);

    /// <summary>
    /// Serializes an RFC 9457 problem document. The default uses the router's own
    /// source-generated <see cref="RouterJsonContext"/>, so consumers do not need to add
    /// <see cref="ProblemDetails"/> to their <c>SerializeResponse</c>. Override only to add
    /// service-specific extension members.
    /// </summary>
    protected virtual string SerializeProblemDetails(ProblemDetails problem)
    {
        return JsonSerializer.Serialize(problem, RouterJsonContext.Default.ProblemDetails);
    }

    /// <summary>
    /// Resolves the correlation id reported as the problem <c>traceId</c>. Default order:
    /// <c>x-correlation-id</c> header, <c>x-request-id</c> header, X-Ray root id from
    /// <c>x-amzn-trace-id</c>, API Gateway request id, Lambda request id.
    /// </summary>
    protected virtual string? ResolveTraceId(APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        if (request.Headers is not null)
        {
            foreach (var header in request.Headers)
            {
                if (string.IsNullOrEmpty(header.Value))
                {
                    continue;
                }

                if (header.Key.Equals("x-correlation-id", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("x-request-id", StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }

            foreach (var header in request.Headers)
            {
                if (header.Key.Equals("x-amzn-trace-id", StringComparison.OrdinalIgnoreCase)
                    && TryParseXRayRoot(header.Value, out var root))
                {
                    return root;
                }
            }
        }

        var requestId = request.RequestContext?.RequestId;
        if (!string.IsNullOrEmpty(requestId))
        {
            return requestId;
        }

        var awsRequestId = context.AwsRequestId;
        return string.IsNullOrEmpty(awsRequestId) ? null : awsRequestId;
    }

    /// <summary>
    /// Override to provide custom health check response.
    /// </summary>
    protected virtual HealthCheckResponse GetHealthCheckResponse()
    {
        return new HealthCheckResponse
        {
            Status = "healthy",
            Function = GetType().Name,
            Timestamp = DateTime.UtcNow.ToString("o"),
            Environment = System.Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "unknown"
        };
    }

    /// <summary>
    /// Override to customize the health check paths.
    /// Default paths are: /health, /health/, /healthz, /healthz/
    /// </summary>
    protected virtual bool IsHealthCheckPath(string path)
    {
        return string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/health/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/healthz", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/healthz/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Main entry point for the Lambda function.
    /// Handles routing, error handling, and response formatting.
    /// </summary>
    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context)
    {
        RouteDefinition? matchedRoute = null;
        var path = request.RawPath ?? "/";
        var method = request.RequestContext?.Http?.Method?.ToUpperInvariant() ?? "GET";

        // Remove stage prefix if present (when using named stages like 'hml' or 'prd')
        // API Gateway HTTP APIs include the stage name in RawPath when not using $default stage
        var stage = request.RequestContext?.Stage;
        if (!string.IsNullOrEmpty(stage) && stage != "$default" && path.StartsWith($"/{stage}", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(stage.Length + 1); // +1 for the leading slash
            if (string.IsNullOrEmpty(path))
                path = "/";
        }

        context.Logger.LogInformation($"{GetType().Name}: {method} {path}");

        // Health check endpoint (always available without auth)
        if (IsHealthCheckPath(path))
        {
            return CreateJsonResponse(HttpStatusCode.OK, GetHealthCheckResponse());
        }

        var traceId = ResolveTraceId(request, context);

        try
        {
            // Find matching route
            var match = _routeMatcher.Match(method, path);
            if (match == null)
            {
                return CreateRouteNotFoundResponse(method, path, traceId);
            }

            matchedRoute = match.Route;

            // Build route context
            var routeContext = BuildRouteContext(request, match);

            // Validate authorization
            var authResult = _authorizationService.Authorize(routeContext, match.Route.AuthorizationOptions);
            if (!authResult.Succeeded)
            {
                // 401 when the caller is not authenticated at all, 403 when authenticated but not allowed
                var authenticated = routeContext.Claims.Count > 0;
                var mapped = authenticated
                    ? MappedError.Forbidden(authResult.FailureMessage)
                    : MappedError.Unauthorized(authResult.FailureMessage);
                context.Logger.LogWarning($"{mapped.LogPrefix}: {authResult.FailureMessage}");
                return CreateErrorResponse(mapped, matchedRoute, path, traceId);
            }

            // Execute command via abstract method (implementation handles type safety)
            IServiceScope? scope = null;
            IMediator mediator;

            try
            {
                if (_serviceProvider != null)
                {
                    scope = _serviceProvider.CreateScope();
                    mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                }
                else
                {
                    mediator = _mediator!;
                }

                var result = await ExecuteCommandAsync(match, routeContext, mediator);

                if (result is APIGatewayHttpApiV2ProxyResponse apiGatewayResponse)
                {
                    return apiGatewayResponse;
                }

                if (result is ApiGatewayResponse customResponse)
                {
                    return CreateApiGatewayResponse(customResponse);
                }

                return CreateResponse(HttpStatusCode.OK, result, match.Route);
            }
            finally
            {
                scope?.Dispose();
            }
        }
        catch (Exception ex)
        {
            var mapped = MappedError.FromException(ex);
            if (mapped.Status == HttpStatusCode.InternalServerError)
            {
                context.Logger.LogError($"Error in {GetType().Name}: {ex.Message}");
            }
            else
            {
                context.Logger.LogWarning($"{mapped.LogPrefix}: {ex.Message}");
            }

            var response = CreateErrorResponse(mapped, matchedRoute, path, traceId);
            if (mapped.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
            {
                // RFC 7231 §7.1.3 — delta-seconds. Round up so a 0.1s hint still
                // communicates "wait a bit" rather than "retry immediately".
                var seconds = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds));
                response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
            }
            return response;
        }
    }

    private static RouteContext BuildRouteContext(
        APIGatewayHttpApiV2ProxyRequest request,
        RouteMatch match)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Headers != null)
        {
            foreach (var header in request.Headers)
            {
                headers[header.Key] = header.Value;
            }
        }

        var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.QueryStringParameters != null)
        {
            foreach (var param in request.QueryStringParameters)
            {
                queryParams[param.Key] = param.Value;
            }
        }

        var claims = new Dictionary<string, string>();
        var jwtClaims = request.RequestContext?.Authorizer?.Jwt?.Claims;
        if (jwtClaims is not null && jwtClaims.Count > 0)
        {
            foreach (var claim in jwtClaims)
            {
                claims[claim.Key] = claim.Value;
            }
        }
        else
        {
            var lambdaClaims = request.RequestContext?.Authorizer?.Lambda;
            if (lambdaClaims is not null)
            {
                foreach (var kvp in lambdaClaims)
                {
                    if (kvp.Value is not null)
                    {
                        claims[kvp.Key] = kvp.Value.ToString() ?? "";
                    }
                }
            }
        }

        return new RouteContext
        {
            Body = request.Body,
            PathParameters = match.PathParameters,
            QueryParameters = queryParams,
            Headers = headers,
            Claims = claims
        };
    }

    private APIGatewayHttpApiV2ProxyResponse CreateRouteNotFoundResponse(string method, string path, string? traceId)
    {
        if (ErrorResponseFormat == ErrorResponseFormat.ProblemDetails)
        {
            return CreateProblemResponse(new ProblemDetails
            {
                Type = ProblemTypes.ToUri(ProblemTypes.NotFound),
                Title = "Not Found",
                Status = (int)HttpStatusCode.NotFound,
                Detail = $"No route matches {method} {path}.",
                Instance = path,
                TraceId = traceId
            }, route: null);
        }

        return CreateJsonResponse(HttpStatusCode.NotFound, new RouteNotFoundResponse
        {
            Error = "Route not found",
            Path = path,
            Method = method
        });
    }

    private APIGatewayHttpApiV2ProxyResponse CreateErrorResponse(
        MappedError error,
        RouteDefinition? route,
        string instance,
        string? traceId)
    {
        if (ErrorResponseFormat == ErrorResponseFormat.ProblemDetails)
        {
            return CreateProblemResponse(new ProblemDetails
            {
                Type = ProblemTypes.ToUri(error.Type),
                Title = error.Title,
                Status = (int)error.Status,
                Detail = error.Detail,
                Instance = instance,
                TraceId = traceId,
                Errors = error.Errors
            }, route);
        }

        return CreateResponse(error.Status, new ErrorResponse
        {
            Error = error.LegacyError,
            Details = error.LegacyDetails
        }, route);
    }

    private APIGatewayHttpApiV2ProxyResponse CreateProblemResponse(ProblemDetails problem, RouteDefinition? route)
    {
        var headers = BuildHeaders(route);
        // A problem document is always problem+json, regardless of the route's Produces().
        headers["Content-Type"] = ProblemJsonContentType;
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = problem.Status,
            Body = SerializeProblemDetails(problem),
            Headers = headers
        };
    }

    private APIGatewayHttpApiV2ProxyResponse CreateJsonResponse(HttpStatusCode statusCode, object body)
    {
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = (int)statusCode,
            Body = SerializeResponse(body),
            Headers = _jsonContentTypeHeader
        };
    }

    private APIGatewayHttpApiV2ProxyResponse CreateResponse(
        HttpStatusCode statusCode,
        object body,
        RouteDefinition? route)
    {
        var headers = BuildHeaders(route);
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = (int)statusCode,
            Body = SerializeResponse(body),
            Headers = headers
        };
    }

    private static APIGatewayHttpApiV2ProxyResponse CreateApiGatewayResponse(ApiGatewayResponse response)
    {
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = response.StatusCode,
            Body = response.Body,
            Headers = response.Headers ?? new Dictionary<string, string>(),
            IsBase64Encoded = response.IsBase64Encoded
        };
    }

    private static Dictionary<string, string> BuildHeaders(RouteDefinition? route)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (route is not null)
        {
            foreach (var pair in route.ResponseHeaders)
            {
                headers[pair.Key] = pair.Value;
            }
        }

        if (!headers.ContainsKey("Content-Type"))
        {
            if (route?.ResponseContentType is not null)
            {
                headers["Content-Type"] = route.ResponseContentType;
            }
            else
            {
                headers["Content-Type"] = "application/json";
            }
        }

        return headers;
    }

    private static bool TryParseXRayRoot(string? traceHeader, out string root)
    {
        // Header shape: "Root=1-67ab...;Parent=...;Sampled=1"
        root = string.Empty;
        if (string.IsNullOrEmpty(traceHeader))
        {
            return false;
        }

        foreach (var segment in traceHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.StartsWith("Root=", StringComparison.OrdinalIgnoreCase) && segment.Length > 5)
            {
                root = segment[5..];
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Format-agnostic description of an error the router is about to return. Built once from
    /// the exception (or auth failure) and rendered either as the legacy envelope or as a
    /// problem document, so both formats share a single status/type mapping.
    /// </summary>
    private sealed record MappedError(
        HttpStatusCode Status,
        string Type,
        string Title,
        string? Detail,
        string LegacyError,
        string? LegacyDetails,
        string LogPrefix,
        IReadOnlyList<ProblemFieldError>? Errors = null,
        TimeSpan? RetryAfter = null)
    {
        private const string InternalErrorDetail =
            "An unexpected error occurred. If it persists, contact support quoting the traceId.";

        public static MappedError Unauthorized(string? detail) => new(
            HttpStatusCode.Unauthorized, ProblemTypes.Unauthorized, "Unauthorized",
            detail ?? "Authentication is missing or invalid.", "Unauthorized", detail, "Unauthorized");

        public static MappedError Forbidden(string? detail) => new(
            HttpStatusCode.Forbidden, ProblemTypes.Forbidden, "Forbidden",
            detail ?? "Insufficient permissions.", "Forbidden", detail, "Forbidden");

        public static MappedError FromException(Exception ex) => ex switch
        {
            ProblemException p => new(
                (HttpStatusCode)p.Status, p.Type, p.Title, p.Message, p.Title, p.Message, p.Title, p.Errors, p.RetryAfter),
            ValidationException v => new(
                HttpStatusCode.BadRequest, ProblemTypes.ValidationFailed, "Validation Failed",
                v.Message, "Validation failed", v.Message, "Validation error", v.Errors),
            NotFoundException => new(
                HttpStatusCode.NotFound, ProblemTypes.NotFound, "Not Found",
                ex.Message, "Resource not found", ex.Message, "Not found"),
            UnauthorizedException => new(
                HttpStatusCode.Unauthorized, ProblemTypes.Unauthorized, "Unauthorized",
                ex.Message, "Unauthorized", ex.Message, "Unauthorized"),
            ForbiddenException => new(
                HttpStatusCode.Forbidden, ProblemTypes.Forbidden, "Forbidden",
                ex.Message, "Forbidden", ex.Message, "Forbidden"),
            ConflictException => new(
                HttpStatusCode.Conflict, ProblemTypes.Conflict, "Conflict",
                ex.Message, "Conflict", ex.Message, "Conflict"),
            TooManyRequestsException t => new(
                HttpStatusCode.TooManyRequests, ProblemTypes.TooManyRequests, "Too Many Requests",
                t.Message, "Too many requests", t.Message, "Too many requests", null, t.RetryAfter),
            // Unexpected fault: the problem document never echoes the exception message (LGPD).
            // The legacy envelope keeps its historical behavior unchanged until 3.0.
            _ => new(
                HttpStatusCode.InternalServerError, ProblemTypes.InternalError, "Internal Server Error",
                InternalErrorDetail, "Internal server error", ex.Message, "Error"),
        };
    }
}
