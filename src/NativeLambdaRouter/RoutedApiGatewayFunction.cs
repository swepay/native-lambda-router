using System.Net;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using NativeMediator;

namespace NativeLambdaRouter;

/// <summary>
/// Base class for API Gateway Lambda functions with routing support.
/// Provides automatic route matching, error handling, and health checks.
/// </summary>
public abstract class RoutedApiGatewayFunction
{
    private readonly IMediator _mediator;
    private readonly RouteMatcher _routeMatcher;
    private readonly JsonSerializerOptions? _jsonOptions;
    private readonly AuthorizationService _authorizationService;

    private static readonly Dictionary<string, string> _jsonContentTypeHeader = new()
    {
        { "Content-Type", "application/json" }
    };

    /// <summary>
    /// The mediator instance for sending commands.
    /// </summary>
    protected IMediator Mediator => _mediator;

    /// <summary>
    /// Creates a new routed API Gateway function.
    /// </summary>
    /// <param name="mediator">The mediator for handling commands.</param>
    /// <param name="jsonOptions">Optional JSON serializer options for Native AOT compatibility.</param>
    protected RoutedApiGatewayFunction(IMediator mediator, JsonSerializerOptions? jsonOptions = null)
    {
        _mediator = mediator;
        _jsonOptions = jsonOptions;

        var builder = new RouteBuilder();
        ConfigureRoutes(builder);
        _routeMatcher = new RouteMatcher(builder.Routes);

        var authBuilder = new AuthorizationBuilder();
        ConfigureAuthorization(authBuilder);
        _authorizationService = new AuthorizationService(authBuilder.Policies);
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
    /// </summary>
    /// <example>
    /// <code>
    /// protected override async Task&lt;object&gt; ExecuteCommandAsync(RouteMatch match, RouteContext context)
    /// {
    ///     var command = match.Route.CommandFactory(context);
    ///     return command switch
    ///     {
    ///         GetItemsCommand cmd => await Mediator.Send(cmd),
    ///         CreateItemCommand cmd => await Mediator.Send(cmd),
    ///         _ => throw new InvalidOperationException($"Unknown command: {command.GetType().Name}")
    ///     };
    /// }
    /// </code>
    /// </example>
    protected abstract Task<object> ExecuteCommandAsync(RouteMatch match, RouteContext context);

    /// <summary>
    /// Override this method to provide custom JSON serialization for Native AOT.
    /// Use source-generated JSON serializer context for AOT compatibility.
    /// </summary>
    protected virtual string SerializeResponse(object response)
    {
        if (_jsonOptions != null)
            return JsonSerializer.Serialize(response, response.GetType(), _jsonOptions);

        return JsonSerializer.Serialize(response);
    }

    /// <summary>
    /// Override to provide custom health check response.
    /// </summary>
    protected virtual object GetHealthCheckResponse()
    {
        return new
        {
            Status = "healthy",
            Function = GetType().Name,
            Timestamp = DateTime.UtcNow.ToString("o"),
            Environment = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "unknown"
        };
    }

    /// <summary>
    /// Override to customize the health check paths.
    /// Default paths are: /health, /health/, /healthz, /healthz/
    /// </summary>
    protected virtual bool IsHealthCheckPath(string path)
    {
        return path is "/health" or "/health/" or "/healthz" or "/healthz/";
    }

    /// <summary>
    /// Main entry point for the Lambda function.
    /// Handles routing, error handling, and response formatting.
    /// </summary>
    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context)
    {
        var path = request.RawPath?.ToLowerInvariant() ?? "/";
        var method = request.RequestContext?.Http?.Method?.ToUpperInvariant() ?? "GET";

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
                return CreateJsonResponse(HttpStatusCode.NotFound, new
                {
                    Error = "Route not found",
                    Path = path,
                    Method = method
                });
            }

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
                    return CreateJsonResponse(HttpStatusCode.Unauthorized, new
                    {
                        Error = "Unauthorized",
                        Details = authResult.FailureMessage
                    });
                }
                else
                {
                    context.Logger.LogWarning($"Forbidden: {authResult.FailureMessage}");
                    return CreateJsonResponse(HttpStatusCode.Forbidden, new
                    {
                        Error = "Forbidden",
                        Details = authResult.FailureMessage
                    });
                }
            }

            // Execute command via abstract method (implementation handles type safety)
            var result = await ExecuteCommandAsync(match, routeContext);

            return CreateJsonResponse(HttpStatusCode.OK, result);
        }
        catch (ValidationException ex)
        {
            context.Logger.LogWarning($"Validation error: {ex.Message}");
            return CreateJsonResponse(HttpStatusCode.BadRequest, new
            {
                Error = "Validation failed",
                Details = ex.Message
            });
        }
        catch (NotFoundException ex)
        {
            context.Logger.LogWarning($"Not found: {ex.Message}");
            return CreateJsonResponse(HttpStatusCode.NotFound, new
            {
                Error = "Resource not found",
                Details = ex.Message
            });
        }
        catch (UnauthorizedException ex)
        {
            context.Logger.LogWarning($"Unauthorized: {ex.Message}");
            return CreateJsonResponse(HttpStatusCode.Unauthorized, new
            {
                Error = "Unauthorized",
                Details = ex.Message
            });
        }
        catch (ForbiddenException ex)
        {
            context.Logger.LogWarning($"Forbidden: {ex.Message}");
            return CreateJsonResponse(HttpStatusCode.Forbidden, new
            {
                Error = "Forbidden",
                Details = ex.Message
            });
        }
        catch (ConflictException ex)
        {
            context.Logger.LogWarning($"Conflict: {ex.Message}");
            return CreateJsonResponse(HttpStatusCode.Conflict, new
            {
                Error = "Conflict",
                Details = ex.Message
            });
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error in {GetType().Name}: {ex.Message}");
            return CreateJsonResponse(HttpStatusCode.InternalServerError, new
            {
                Error = "Internal server error",
                Details = ex.Message
            });
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

    private APIGatewayHttpApiV2ProxyResponse CreateJsonResponse(HttpStatusCode statusCode, object body) =>
        new()
        {
            StatusCode = (int)statusCode,
            Body = SerializeResponse(body),
            Headers = _jsonContentTypeHeader
        };
}
