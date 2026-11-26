using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using NativeMediator;

namespace NativeLambdaRouter.Tests;

/// <summary>
/// <see cref="RoutedApiGatewayFunction.SelectErrorResponseFormat"/> — the per-request/per-route
/// override point for <see cref="ErrorResponseFormat"/> (closes swepay/native-guard-backend#153:
/// a function with OAuth-boundary routes that must keep the legacy envelope while every other
/// route moves to RFC 9457, including the case where no route matches at all).
/// </summary>
public sealed class ErrorResponseFormatSelectionTests
{
    private const string ProblemJson = "application/problem+json";

    private sealed class PingCommand;

    private sealed class PingResponse
    {
        public string Value { get; init; } = "pong";
    }

    /// <summary>
    /// Function with a function-wide default of <see cref="ErrorResponseFormat.LegacyEnvelope"/>
    /// that overrides <see cref="RoutedApiGatewayFunction.SelectErrorResponseFormat"/> to switch to
    /// <see cref="ErrorResponseFormat.ProblemDetails"/> for every path under <c>/v1</c>, mirroring
    /// the shape of the Identity function's <c>IsOAuthBoundaryRoute</c> split: non-boundary routes
    /// (here, <c>/v1/*</c>) move to RFC 9457, everything else (here, <c>/oauth/*</c>) keeps the
    /// legacy shape untouched.
    /// </summary>
    private sealed class MixedFormatFunction : RoutedApiGatewayFunction
    {
        public Exception? ToThrow { get; set; }
        public int SelectCallCount { get; private set; }
        public ErrorResponseFormatContext? LastContext { get; private set; }

        public MixedFormatFunction() : base(Substitute.For<IMediator>())
        {
        }

        protected override ErrorResponseFormat SelectErrorResponseFormat(ErrorResponseFormatContext context)
        {
            SelectCallCount++;
            LastContext = context;
            return context.Path.StartsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? ErrorResponseFormat.ProblemDetails
                : ErrorResponseFormat.LegacyEnvelope;
        }

        protected override void ConfigureRoutes(IRouteBuilder routes)
        {
            routes.MapGet<PingCommand, PingResponse>("/v1/items/{itemId}", _ => new PingCommand()).AllowAnonymous();
            routes.MapGet<PingCommand, PingResponse>("/v1/secure", _ => new PingCommand()).RequireAuthorization();
            routes.MapGet<PingCommand, PingResponse>("/v1/admin", _ => new PingCommand()).RequireRole("admin");
            routes.MapGet<PingCommand, PingResponse>("/oauth/token", _ => new PingCommand()).AllowAnonymous();
            routes.MapGet<PingCommand, PingResponse>("/oauth/secure", _ => new PingCommand()).RequireAuthorization();
        }

        protected override Task<object> ExecuteCommandAsync(RouteMatch match, RouteContext context, IMediator mediator)
        {
            if (ToThrow is not null)
            {
                throw ToThrow;
            }

            return Task.FromResult<object>(new PingResponse());
        }

        protected override string SerializeResponse(object response)
        {
            return response switch
            {
                PingResponse r => "{\"value\":\"" + r.Value + "\"}",
                ErrorResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.ErrorResponse),
                RouteNotFoundResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.RouteNotFoundResponse),
                HealthCheckResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.HealthCheckResponse),
                _ => throw new NotSupportedException($"No serializer for {response.GetType().Name}")
            };
        }
    }

    /// <summary>
    /// Function that only overrides the 2.4.0-era <see cref="RoutedApiGatewayFunction.ErrorResponseFormat"/>
    /// property and never touches <see cref="RoutedApiGatewayFunction.SelectErrorResponseFormat"/> —
    /// proves the new hook is additive and does not change behavior for existing consumers.
    /// </summary>
    private sealed class LegacyOverrideOnlyFunction : RoutedApiGatewayFunction
    {
        private readonly ErrorResponseFormat _format;

        public Exception? ToThrow { get; set; }

        public LegacyOverrideOnlyFunction(ErrorResponseFormat format) : base(Substitute.For<IMediator>())
        {
            _format = format;
        }

        protected override ErrorResponseFormat ErrorResponseFormat => _format;

        protected override void ConfigureRoutes(IRouteBuilder routes)
        {
            routes.MapGet<PingCommand, PingResponse>("/ping", _ => new PingCommand()).AllowAnonymous();
        }

        protected override Task<object> ExecuteCommandAsync(RouteMatch match, RouteContext context, IMediator mediator)
        {
            if (ToThrow is not null)
            {
                throw ToThrow;
            }

            return Task.FromResult<object>(new PingResponse());
        }

        protected override string SerializeResponse(object response)
        {
            return response switch
            {
                PingResponse r => "{\"value\":\"" + r.Value + "\"}",
                ErrorResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.ErrorResponse),
                RouteNotFoundResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.RouteNotFoundResponse),
                HealthCheckResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.HealthCheckResponse),
                _ => throw new NotSupportedException($"No serializer for {response.GetType().Name}")
            };
        }
    }

    private static APIGatewayHttpApiV2ProxyRequest Request(
        string path,
        string method = "GET",
        Dictionary<string, string>? jwtClaims = null)
    {
        return new APIGatewayHttpApiV2ProxyRequest
        {
            RawPath = path,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = method, Path = path },
                Authorizer = jwtClaims is null ? null : new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription
                {
                    Jwt = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription.JwtDescription { Claims = jwtClaims }
                }
            }
        };
    }

    private static ILambdaContext LambdaContext()
    {
        var context = Substitute.For<ILambdaContext>();
        context.Logger.Returns(Substitute.For<ILambdaLogger>());
        return context;
    }

    private static ProblemDetails Problem(APIGatewayHttpApiV2ProxyResponse response)
    {
        response.Headers.ShouldContainKeyAndValue("Content-Type", ProblemJson);
        var problem = JsonSerializer.Deserialize(response.Body, RouterJsonContext.Default.ProblemDetails);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe(response.StatusCode);
        return problem;
    }

    // --- route-not-found: the one case with no matched route (Route == null) ---

    [Fact]
    public async Task RouteNotFound_UnderSelectedPrefix_ReturnsProblemDetails()
    {
        var function = new MixedFormatFunction();

        var response = await function.FunctionHandler(Request("/v1/does-not-exist"), LambdaContext());

        response.StatusCode.ShouldBe(404);
        var problem = Problem(response);
        problem.Type.ShouldBe("https://errors.swepay.com.br/common/not-found");
        problem.Instance.ShouldBe("/v1/does-not-exist");
        function.LastContext.ShouldNotBeNull();
        function.LastContext!.Route.ShouldBeNull();
        function.LastContext!.Method.ShouldBe("GET");
        function.LastContext!.Path.ShouldBe("/v1/does-not-exist");
    }

    [Fact]
    public async Task RouteNotFound_OutsideSelectedPrefix_KeepsLegacyEnvelope()
    {
        var function = new MixedFormatFunction();

        var response = await function.FunctionHandler(Request("/oauth/does-not-exist"), LambdaContext());

        response.StatusCode.ShouldBe(404);
        response.Headers.ShouldContainKeyAndValue("Content-Type", "application/json");
        response.Body.ShouldBe("{\"error\":\"Route not found\",\"path\":\"/oauth/does-not-exist\",\"method\":\"GET\"}");
    }

    // --- mapped exceptions: the format follows the matched route's path ---

    [Fact]
    public async Task MappedException_UnderSelectedPrefix_ReturnsProblemDetails()
    {
        var function = new MixedFormatFunction { ToThrow = new NotFoundException("Item 42 not found") };

        var response = await function.FunctionHandler(Request("/v1/items/42"), LambdaContext());

        response.StatusCode.ShouldBe(404);
        var problem = Problem(response);
        problem.Type.ShouldBe("https://errors.swepay.com.br/common/not-found");
        problem.Detail.ShouldBe("Item 42 not found");
        function.LastContext!.Route.ShouldNotBeNull();
        function.LastContext!.Route!.Path.ShouldBe("/v1/items/{itemId}");
    }

    [Fact]
    public async Task MappedException_OutsideSelectedPrefix_KeepsLegacyEnvelope()
    {
        var function = new MixedFormatFunction { ToThrow = new ValidationException("grant_type is required.") };

        var response = await function.FunctionHandler(Request("/oauth/token"), LambdaContext());

        response.StatusCode.ShouldBe(400);
        response.Headers.ShouldContainKeyAndValue("Content-Type", "application/json");
        response.Body.ShouldBe("{\"error\":\"Validation failed\",\"details\":\"grant_type is required.\"}");
    }

    // --- authorization 401/403: the format follows the matched route's path ---

    [Fact]
    public async Task Unauthenticated_UnderSelectedPrefix_ReturnsProblemDetails()
    {
        var function = new MixedFormatFunction();

        var response = await function.FunctionHandler(Request("/v1/secure"), LambdaContext());

        response.StatusCode.ShouldBe(401);
        Problem(response).Type.ShouldBe("https://errors.swepay.com.br/common/unauthorized");
    }

    [Fact]
    public async Task Unauthenticated_OutsideSelectedPrefix_KeepsLegacyEnvelope()
    {
        var function = new MixedFormatFunction();

        var response = await function.FunctionHandler(Request("/oauth/secure"), LambdaContext());

        response.StatusCode.ShouldBe(401);
        response.Headers.ShouldContainKeyAndValue("Content-Type", "application/json");
        var envelope = JsonSerializer.Deserialize(response.Body, RouterJsonContext.Default.ErrorResponse);
        envelope.ShouldNotBeNull();
        envelope.Error.ShouldBe("Unauthorized");
    }

    [Fact]
    public async Task Forbidden_UnderSelectedPrefix_ReturnsProblemDetails()
    {
        var function = new MixedFormatFunction();
        var request = Request("/v1/admin", jwtClaims: new Dictionary<string, string> { ["sub"] = "user-1", ["role"] = "viewer" });

        var response = await function.FunctionHandler(request, LambdaContext());

        response.StatusCode.ShouldBe(403);
        Problem(response).Type.ShouldBe("https://errors.swepay.com.br/common/forbidden");
    }

    // --- 500 fallback follows the same rule as every other mapped error ---

    [Fact]
    public async Task UnexpectedException_UnderSelectedPrefix_Returns500ProblemWithoutLeakingMessage()
    {
        var function = new MixedFormatFunction { ToThrow = new InvalidOperationException("connection string=secret") };

        var response = await function.FunctionHandler(Request("/v1/items/42"), LambdaContext());

        response.StatusCode.ShouldBe(500);
        var problem = Problem(response);
        problem.Type.ShouldBe("https://errors.swepay.com.br/common/internal-error");
        problem.Detail.ShouldNotBeNull();
        problem.Detail.ShouldNotContain("secret");
    }

    [Fact]
    public async Task UnexpectedException_OutsideSelectedPrefix_KeepsLegacyEnvelope_WithMessage()
    {
        // Documents the pre-existing (2.4.0+) behavior of the legacy envelope: it still echoes
        // ex.Message in `details` until 3.0 — unchanged by this feature.
        var function = new MixedFormatFunction { ToThrow = new InvalidOperationException("boom") };

        var response = await function.FunctionHandler(Request("/oauth/token"), LambdaContext());

        response.StatusCode.ShouldBe(500);
        response.Headers.ShouldContainKeyAndValue("Content-Type", "application/json");
        response.Body.ShouldBe("{\"error\":\"Internal server error\",\"details\":\"boom\"}");
    }

    // --- the selector is consulted exactly once per error, never more ---

    [Fact]
    public async Task SelectErrorResponseFormat_IsCalledExactlyOncePerError()
    {
        var function = new MixedFormatFunction { ToThrow = new NotFoundException("x") };

        await function.FunctionHandler(Request("/v1/items/42"), LambdaContext());

        function.SelectCallCount.ShouldBe(1);
    }

    // --- backward compatibility: consumers that never override the new hook are unaffected ---

    [Fact]
    public async Task WithoutOverride_DefaultsTo_ErrorResponseFormatProperty_Legacy()
    {
        var function = new LegacyOverrideOnlyFunction(ErrorResponseFormat.LegacyEnvelope)
        {
            ToThrow = new NotFoundException("Item 42 not found")
        };

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(404);
        response.Headers.ShouldContainKeyAndValue("Content-Type", "application/json");
        response.Body.ShouldBe("{\"error\":\"Resource not found\",\"details\":\"Item 42 not found\"}");
    }

    [Fact]
    public async Task WithoutOverride_DefaultsTo_ErrorResponseFormatProperty_ProblemDetails()
    {
        var function = new LegacyOverrideOnlyFunction(ErrorResponseFormat.ProblemDetails)
        {
            ToThrow = new NotFoundException("Item 42 not found")
        };

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(404);
        Problem(response).Type.ShouldBe("https://errors.swepay.com.br/common/not-found");
    }
}
