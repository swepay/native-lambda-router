using System.Net;
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

        try
        {
            // Find matching route
            var match = _routeMatcher.Match(method, path);
            if (match == null)
            {
                return CreateJsonResponse(HttpStatusCode.NotFound, new RouteNotFoundResponse
                {
                    Error = "Route not found",
                    Path = path,
                    Method = method
                });
            }

            matchedRoute = match.Route;

            // Build route context
            var routeContext = BuildRouteContext(request, match);

            // Validate authorization
            var authResult = _authorizationService.Authorize(routeContext, match.Route.AuthorizationOptions);
            if (!authResult.Succeeded)
            {
                // Determine if it's 401 (not authenticated) or 403 (not authorized)
                if (routeContext.Claims.Count == 0)
                {
                    context.Logger.LogWarning($"Unauthorized: {authResult.FailureMessage}");
                    return CreateResponse(HttpStatusCode.Unauthorized, new ErrorResponse
                    {
                        Error = "Unauthorized",
                        Details = authResult.FailureMessage
                    }, matchedRoute);
                }
                else
                {
                    context.Logger.LogWarning($"Forbidden: {authResult.FailureMessage}");
                    return CreateResponse(HttpStatusCode.Forbidden, new ErrorResponse
                    {
                        Error = "Forbidden",
                        Details = authResult.FailureMessage
                    }, matchedRoute);
                }
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
        catch (ValidationException ex)
        {
            context.Logger.LogWarning($"Validation error: {ex.Message}");
            return CreateResponse(HttpStatusCode.BadRequest, new ErrorResponse
            {
                Error = "Validation failed",
                Details = ex.Message
            }, matchedRoute);
        }
        catch (NotFoundException ex)
        {
            context.Logger.LogWarning($"Not found: {ex.Message}");
            return CreateResponse(HttpStatusCode.NotFound, new ErrorResponse
            {
                Error = "Resource not found",
                Details = ex.Message
            }, matchedRoute);
        }
        catch (UnauthorizedException ex)
        {
            context.Logger.LogWarning($"Unauthorized: {ex.Message}");
            return CreateResponse(HttpStatusCode.Unauthorized, new ErrorResponse
            {
                Error = "Unauthorized",
                Details = ex.Message
            }, matchedRoute);
        }
        catch (ForbiddenException ex)
        {
            context.Logger.LogWarning($"Forbidden: {ex.Message}");
            return CreateResponse(HttpStatusCode.Forbidden, new ErrorResponse
            {
                Error = "Forbidden",
                Details = ex.Message
            }, matchedRoute);
        }
        catch (ConflictException ex)
        {
            context.Logger.LogWarning($"Conflict: {ex.Message}");
            return CreateResponse(HttpStatusCode.Conflict, new ErrorResponse
            {
                Error = "Conflict",
                Details = ex.Message
            }, matchedRoute);
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error in {GetType().Name}: {ex.Message}");
            return CreateResponse(HttpStatusCode.InternalServerError, new ErrorResponse
            {
                Error = "Internal server error",
                Details = ex.Message
            }, matchedRoute);
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
        if (request.RequestContext?.Authorizer?.Jwt?.Claims != null)
        {
            foreach (var claim in request.RequestContext.Authorizer.Jwt.Claims)
            {
                claims[claim.Key] = claim.Value;
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
}
