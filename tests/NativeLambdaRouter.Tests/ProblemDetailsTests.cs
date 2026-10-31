using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using NativeMediator;

namespace NativeLambdaRouter.Tests;

/// <summary>
/// RFC 9457 (GS-04) behavior of <see cref="RoutedApiGatewayFunction"/> when a consumer opts in to
/// <see cref="ErrorResponseFormat.ProblemDetails"/>, plus the guarantee that the legacy envelope is untouched.
/// </summary>
public sealed class ProblemDetailsTests
{
    private const string ProblemJson = "application/problem+json";

    private sealed class PingCommand;

    private sealed class PingResponse
    {
        public string Value { get; init; } = "pong";
    }

    private sealed class TenantSuspendedException : ProblemException
    {
        public TenantSuspendedException()
            : base(403, "native-mail/tenant-suspended", "Tenant Suspended",
                   "Reactivate the subscription in AWS Marketplace to resume sending email.")
        {
        }
    }

    /// <summary>
    /// Function whose handler throws whatever the test injects, so each exception type can be
    /// driven through the real FunctionHandler pipeline.
    /// </summary>
    private sealed class ThrowingFunction : RoutedApiGatewayFunction
    {
        private readonly ErrorResponseFormat _format;

        public Exception? ToThrow { get; set; }

        public ThrowingFunction(ErrorResponseFormat format) : base(Substitute.For<IMediator>())
        {
            _format = format;
        }

        protected override ErrorResponseFormat ErrorResponseFormat => _format;

        protected override void ConfigureRoutes(IRouteBuilder routes)
        {
            routes.MapGet<PingCommand, PingResponse>("/ping", _ => new PingCommand()).AllowAnonymous();
            routes.MapGet<PingCommand, PingResponse>("/items/{itemId}", _ => new PingCommand()).AllowAnonymous();
            routes.MapGet<PingCommand, PingResponse>("/docs", _ => new PingCommand())
                .Produces("text/html")
                .WithHeader("Cache-Control", "no-store")
                .AllowAnonymous();
            routes.MapGet<PingCommand, PingResponse>("/secure", _ => new PingCommand()).RequireAuthorization();
            routes.MapGet<PingCommand, PingResponse>("/admin", _ => new PingCommand()).RequireRole("admin");
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
            // Deliberately does NOT handle ProblemDetails: the router must serialize it on its own.
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

    private static ThrowingFunction ProblemFunction(Exception? toThrow = null) =>
        new(ErrorResponseFormat.ProblemDetails) { ToThrow = toThrow };

    private static ThrowingFunction LegacyFunction(Exception? toThrow = null) =>
        new(ErrorResponseFormat.LegacyEnvelope) { ToThrow = toThrow };

    private static APIGatewayHttpApiV2ProxyRequest Request(
        string path,
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? jwtClaims = null,
        string? apiGatewayRequestId = null)
    {
        return new APIGatewayHttpApiV2ProxyRequest
        {
            RawPath = path,
            Headers = headers,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                RequestId = apiGatewayRequestId,
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = "GET", Path = path },
                Authorizer = jwtClaims is null ? null : new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription
                {
                    Jwt = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription.JwtDescription { Claims = jwtClaims }
                }
            }
        };
    }

    private static ILambdaContext LambdaContext(string? awsRequestId = null)
    {
        var context = Substitute.For<ILambdaContext>();
        context.AwsRequestId.Returns(awsRequestId);
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

    // --- exception mapping ---

    [Fact]
    public async Task Validation_Returns400_ProblemWithErrors()
    {
        var errors = new List<ProblemFieldError>
        {
            new() { Field = "recipient", Message = "Invalid format." }
        };
        var function = ProblemFunction(new ValidationException("Request has invalid fields.", errors));

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(400);
        var problem = Problem(response);
        problem.Type.ShouldBe("https://errors.swepay.com.br/common/validation-failed");
        problem.Title.ShouldBe("Validation Failed");
        problem.Detail.ShouldBe("Request has invalid fields.");
        problem.Instance.ShouldBe("/ping");
        problem.Errors.ShouldNotBeNull();
        problem.Errors.Count.ShouldBe(1);
        problem.Errors[0].Field.ShouldBe("recipient");
        problem.Errors[0].Message.ShouldBe("Invalid format.");
    }

    [Fact]
    public async Task Validation_WithoutFieldErrors_OmitsErrorsMember()
    {
        var function = ProblemFunction(new ValidationException("Nope."));

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(400);
        response.Body.ShouldNotContain("\"errors\"");
        Problem(response).Errors.ShouldBeNull();
    }

    public static TheoryData<Exception, int, string, string> DomainExceptions => new()
    {
        { new NotFoundException("Domain detail."), 404, "common/not-found", "Not Found" },
        { new UnauthorizedException("Domain detail."), 401, "common/unauthorized", "Unauthorized" },
        { new ForbiddenException("Domain detail."), 403, "common/forbidden", "Forbidden" },
        { new ConflictException("Domain detail."), 409, "common/conflict", "Conflict" },
        { new TooManyRequestsException("Domain detail."), 429, "common/too-many-requests", "Too Many Requests" },
    };

    [Theory]
    [MemberData(nameof(DomainExceptions))]
    public async Task DomainExceptions_MapToCatalogType(Exception exception, int status, string slug, string title)
    {
        var function = ProblemFunction(exception);

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(status);
        var problem = Problem(response);
        problem.Type.ShouldBe($"https://errors.swepay.com.br/{slug}");
        problem.Title.ShouldBe(title);
        problem.Detail.ShouldBe("Domain detail.");
        problem.Instance.ShouldBe("/ping");
    }

    [Fact]
    public async Task UnexpectedException_Returns500_WithoutLeakingMessage()
    {
        var function = ProblemFunction(new InvalidOperationException("SELECT * FROM secrets failed: connection string=abc"));

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(500);
        var problem = Problem(response);
        problem.Type.ShouldBe("https://errors.swepay.com.br/common/internal-error");
        problem.Title.ShouldBe("Internal Server Error");
        problem.Detail.ShouldNotBeNull();
        problem.Detail.ShouldNotContain("secrets");
        problem.Detail.ShouldNotContain("connection string");
        response.Body.ShouldNotContain("InvalidOperationException");
    }

    [Fact]
    public async Task ProblemException_UsesItsOwnStatusTypeAndTitle()
    {
        var function = ProblemFunction(new TenantSuspendedException());

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(403);
        var problem = Problem(response);
        problem.Type.ShouldBe("https://errors.swepay.com.br/native-mail/tenant-suspended");
        problem.Title.ShouldBe("Tenant Suspended");
        problem.Detail.ShouldBe("Reactivate the subscription in AWS Marketplace to resume sending email.");
    }

    [Fact]
    public async Task ProblemException_WithAbsoluteType_IsKeptVerbatim()
    {
        var function = ProblemFunction(new ProblemException(422, "https://errors.swepay.com.br/passly/plan-limit", "Plan Limit", "Upgrade the plan."));

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(422);
        Problem(response).Type.ShouldBe("https://errors.swepay.com.br/passly/plan-limit");
    }

    [Fact]
    public async Task ProblemException_WithRetryAfter_EmitsRetryAfterHeader()
    {
        var function = ProblemFunction(new ProblemException(429, ProblemTypes.TooManyRequests, "Too Many Requests", "Slow down.", retryAfter: TimeSpan.FromSeconds(7)));

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(429);
        response.Headers.ShouldContainKeyAndValue("Retry-After", "7");
        Problem(response).Type.ShouldBe("https://errors.swepay.com.br/common/too-many-requests");
    }

    [Fact]
    public void ProblemException_RejectsNonErrorStatus()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ProblemException(200, "common/not-found", "Not Found", "x"));
        Should.Throw<ArgumentOutOfRangeException>(() => new ProblemException(404, "common/not-found", "Not Found", "x", retryAfter: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task TooManyRequests_StillEmitsRetryAfter_InProblemMode()
    {
        var function = ProblemFunction(new TooManyRequestsException("Slow down.", TimeSpan.FromMilliseconds(100)));

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(429);
        response.Headers.ShouldContainKeyAndValue("Retry-After", "1");
        Problem(response).Detail.ShouldBe("Slow down.");
    }

    // --- router-produced errors (before the handler runs) ---

    [Fact]
    public async Task RouteNotFound_ReturnsProblem()
    {
        var function = ProblemFunction();

        var response = await function.FunctionHandler(Request("/nope"), LambdaContext());

        response.StatusCode.ShouldBe(404);
        var problem = Problem(response);
        problem.Type.ShouldBe("https://errors.swepay.com.br/common/not-found");
        problem.Instance.ShouldBe("/nope");
        problem.Detail.ShouldBe("No route matches GET /nope.");
    }

    [Fact]
    public async Task Unauthenticated_Returns401Problem()
    {
        var function = ProblemFunction();

        var response = await function.FunctionHandler(Request("/secure"), LambdaContext());

        response.StatusCode.ShouldBe(401);
        var problem = Problem(response);
        problem.Type.ShouldBe("https://errors.swepay.com.br/common/unauthorized");
        problem.Title.ShouldBe("Unauthorized");
        problem.Instance.ShouldBe("/secure");
    }

    [Fact]
    public async Task AuthenticatedWithoutRole_Returns403Problem()
    {
        var function = ProblemFunction();
        var request = Request("/admin", jwtClaims: new Dictionary<string, string> { ["sub"] = "user-1", ["role"] = "viewer" });

        var response = await function.FunctionHandler(request, LambdaContext());

        response.StatusCode.ShouldBe(403);
        var problem = Problem(response);
        problem.Type.ShouldBe("https://errors.swepay.com.br/common/forbidden");
        problem.Title.ShouldBe("Forbidden");
    }

    [Fact]
    public async Task ProblemContentType_OverridesRouteProduces_ButKeepsOtherHeaders()
    {
        var function = ProblemFunction(new NotFoundException("Missing."));

        var response = await function.FunctionHandler(Request("/docs"), LambdaContext());

        response.StatusCode.ShouldBe(404);
        response.Headers.ShouldContainKeyAndValue("Content-Type", ProblemJson);
        response.Headers.ShouldContainKeyAndValue("Cache-Control", "no-store");
    }

    [Fact]
    public async Task Instance_IsTheConcreteRequestPath()
    {
        var function = ProblemFunction(new NotFoundException("Missing."));

        var response = await function.FunctionHandler(Request("/items/ITEM-42"), LambdaContext());

        Problem(response).Instance.ShouldBe("/items/ITEM-42");
    }

    // --- traceId resolution ---

    [Fact]
    public async Task TraceId_PrefersCorrelationIdHeader()
    {
        var function = ProblemFunction(new NotFoundException("Missing."));
        var headers = new Dictionary<string, string>
        {
            ["X-Correlation-Id"] = "corr-123",
            ["x-amzn-trace-id"] = "Root=1-abc;Sampled=1"
        };

        var response = await function.FunctionHandler(Request("/ping", headers, apiGatewayRequestId: "apigw-1"), LambdaContext("lambda-1"));

        Problem(response).TraceId.ShouldBe("corr-123");
    }

    [Fact]
    public async Task TraceId_FallsBackToXRayRoot()
    {
        var function = ProblemFunction(new NotFoundException("Missing."));
        var headers = new Dictionary<string, string> { ["x-amzn-trace-id"] = "Root=1-67ab-cdef;Parent=53995c3f42cd8ad8;Sampled=1" };

        var response = await function.FunctionHandler(Request("/ping", headers, apiGatewayRequestId: "apigw-1"), LambdaContext("lambda-1"));

        Problem(response).TraceId.ShouldBe("1-67ab-cdef");
    }

    [Fact]
    public async Task TraceId_FallsBackToApiGatewayRequestId_ThenLambdaRequestId()
    {
        var function = ProblemFunction(new NotFoundException("Missing."));

        var viaApiGateway = await function.FunctionHandler(Request("/ping", apiGatewayRequestId: "apigw-1"), LambdaContext("lambda-1"));
        Problem(viaApiGateway).TraceId.ShouldBe("apigw-1");

        var viaLambda = await function.FunctionHandler(Request("/ping"), LambdaContext("lambda-1"));
        Problem(viaLambda).TraceId.ShouldBe("lambda-1");
    }

    [Fact]
    public async Task TraceId_OmittedWhenNothingAvailable()
    {
        var function = ProblemFunction(new NotFoundException("Missing."));

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.Body.ShouldNotContain("\"traceId\"");
        Problem(response).TraceId.ShouldBeNull();
    }

    // --- serialization ---

    [Fact]
    public void ProblemDetails_SerializesCanonicalShape_WithSourceGen()
    {
        var problem = new ProblemDetails
        {
            Type = ProblemTypes.ToUri(ProblemTypes.ValidationFailed),
            Title = "Validation Failed",
            Status = 400,
            Detail = "Pedagogical message, no PII.",
            Instance = "/v1/messages/msg_01J",
            TraceId = "1-67ab",
            Errors = [new ProblemFieldError { Field = "recipient", Message = "Invalid format." }]
        };

        var json = JsonSerializer.Serialize(problem, RouterJsonContext.Default.ProblemDetails);

        json.ShouldBe("{\"type\":\"https://errors.swepay.com.br/common/validation-failed\",\"title\":\"Validation Failed\",\"status\":400,\"detail\":\"Pedagogical message, no PII.\",\"instance\":\"/v1/messages/msg_01J\",\"traceId\":\"1-67ab\",\"errors\":[{\"field\":\"recipient\",\"message\":\"Invalid format.\"}]}");
    }

    [Theory]
    [InlineData("common/not-found", "https://errors.swepay.com.br/common/not-found")]
    [InlineData("/common/not-found", "https://errors.swepay.com.br/common/not-found")]
    [InlineData("https://errors.swepay.com.br/passly/x", "https://errors.swepay.com.br/passly/x")]
    public void ProblemTypes_ToUri_PrefixesSlugsOnly(string input, string expected)
    {
        ProblemTypes.ToUri(input).ShouldBe(expected);
    }

    // --- legacy envelope is byte-for-byte unchanged when not opted in ---

    [Fact]
    public async Task Legacy_IsDefault_AndKeepsEnvelopeShape()
    {
        var function = LegacyFunction(new NotFoundException("Item 42 not found"));

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(404);
        response.Headers.ShouldContainKeyAndValue("Content-Type", "application/json");
        response.Body.ShouldBe("{\"error\":\"Resource not found\",\"details\":\"Item 42 not found\"}");
    }

    [Fact]
    public async Task Legacy_ProblemException_MapsToEnvelopeWithItsStatus()
    {
        var function = LegacyFunction(new TenantSuspendedException());

        var response = await function.FunctionHandler(Request("/ping"), LambdaContext());

        response.StatusCode.ShouldBe(403);
        response.Headers.ShouldContainKeyAndValue("Content-Type", "application/json");
        response.Body.ShouldBe("{\"error\":\"Tenant Suspended\",\"details\":\"Reactivate the subscription in AWS Marketplace to resume sending email.\"}");
    }

    [Fact]
    public async Task Legacy_RouteNotFound_KeepsShape()
    {
        var function = LegacyFunction();

        var response = await function.FunctionHandler(Request("/nope"), LambdaContext());

        response.StatusCode.ShouldBe(404);
        response.Body.ShouldBe("{\"error\":\"Route not found\",\"path\":\"/nope\",\"method\":\"GET\"}");
    }

    [Fact]
    public async Task Legacy_Unauthenticated_KeepsShape()
    {
        var function = LegacyFunction();

        var response = await function.FunctionHandler(Request("/secure"), LambdaContext());

        response.StatusCode.ShouldBe(401);
        response.Headers.ShouldContainKeyAndValue("Content-Type", "application/json");
        var envelope = JsonSerializer.Deserialize(response.Body, RouterJsonContext.Default.ErrorResponse);
        envelope.ShouldNotBeNull();
        envelope.Error.ShouldBe("Unauthorized");
    }
}
