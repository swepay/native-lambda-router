using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using NativeMediator;

namespace NativeLambdaRouter.Tests;

public sealed class RoutedApiGatewayFunctionTests
{
    private sealed class DocsCommand;

    private sealed class DocsResponse
    {
        public string Html { get; init; } = string.Empty;
    }

    private sealed class TestFunction : RoutedApiGatewayFunction
    {
        public TestFunction(IMediator mediator)
            : base(mediator)
        {
        }

        protected override void ConfigureRoutes(IRouteBuilder routes)
        {
            routes.MapGet<DocsCommand, DocsResponse>("/docs", _ => new DocsCommand())
                .Produces("text/html")
                .WithHeader("Cache-Control", "no-store")
                .AllowAnonymous();

            routes.MapGet<DocsCommand, DocsResponse>("/secure", _ => new DocsCommand())
                .Produces("text/html");
        }

        protected override Task<object> ExecuteCommandAsync(RouteMatch match, RouteContext context, IMediator mediator)
        {
            return Task.FromResult<object>(new DocsResponse { Html = "<html>ok</html>" });
        }

        protected override string SerializeResponse(object response)
        {
            return response switch
            {
                DocsResponse r => r.Html,
                ErrorResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.ErrorResponse),
                RouteNotFoundResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.RouteNotFoundResponse),
                HealthCheckResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.HealthCheckResponse),
                _ => throw new NotSupportedException($"No serializer for {response.GetType().Name}")
            };
        }
    }

    /// <summary>
    /// Test function that captures the RouteContext claims for assertion.
    /// </summary>
    private sealed class ClaimsCaptureFunction : RoutedApiGatewayFunction
    {
        public Dictionary<string, string>? CapturedClaims { get; private set; }

        public ClaimsCaptureFunction(IMediator mediator) : base(mediator) { }

        protected override void ConfigureRoutes(IRouteBuilder routes)
        {
            routes.MapGet<DocsCommand, DocsResponse>("/api/resource", _ => new DocsCommand())
                .AllowAnonymous();

            routes.MapGet<DocsCommand, DocsResponse>("/api/secure", _ => new DocsCommand())
                .RequireAuthorization();

            routes.MapGet<DocsCommand, DocsResponse>("/api/admin", _ => new DocsCommand())
                .RequireRole("Admin");
        }

        protected override void ConfigureAuthorization(AuthorizationBuilder auth)
        {
            auth.AddPolicy("realm_admin", policy =>
                policy.RequireAssertion(ctx =>
                {
                    var realmId = ctx.Claims.GetValueOrDefault("realmId", "");
                    return !string.IsNullOrEmpty(realmId);
                }));
        }

        protected override Task<object> ExecuteCommandAsync(RouteMatch match, RouteContext context, IMediator mediator)
        {
            CapturedClaims = new Dictionary<string, string>(context.Claims);
            return Task.FromResult<object>(new DocsResponse { Html = "ok" });
        }

        protected override string SerializeResponse(object response)
        {
            return response switch
            {
                DocsResponse r => r.Html,
                ErrorResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.ErrorResponse),
                RouteNotFoundResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.RouteNotFoundResponse),
                HealthCheckResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.HealthCheckResponse),
                _ => throw new NotSupportedException($"No serializer for {response.GetType().Name}")
            };
        }
    }

    [Fact]
    public async Task FunctionHandler_Returns_ContentType_ForRoute()
    {
        var mediator = Substitute.For<IMediator>();
        var function = new TestFunction(mediator);
        var request = CreateRequest("/docs", "GET");

        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        response.Headers.ShouldContainKeyAndValue("Content-Type", "text/html");
        response.Body.ShouldBe("<html>ok</html>");
        response.Headers.ShouldContainKeyAndValue("Cache-Control", "no-store");
    }

    [Fact]
    public async Task FunctionHandler_Returns_ContentType_ForErrors()
    {
        var mediator = Substitute.For<IMediator>();
        var function = new TestFunction(mediator);
        var request = CreateRequest("/secure", "GET");

        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        response.StatusCode.ShouldBe(401);
        response.Headers.ShouldContainKeyAndValue("Content-Type", "text/html");
    }

    // --- Lambda Authorizer Tests ---

    [Fact]
    public async Task BuildRouteContext_WithJwtAuthorizer_ReadsFromJwtClaims()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var function = new ClaimsCaptureFunction(mediator);
        var request = CreateRequestWithJwtAuthorizer(new Dictionary<string, string>
        {
            { "sub", "admin@example.com" },
            { "iss", "https://auth.example.com" },
            { "scope", "openid" }
        });

        // Act
        await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        function.CapturedClaims.ShouldNotBeNull();
        function.CapturedClaims.Count.ShouldBe(3);
        function.CapturedClaims["sub"].ShouldBe("admin@example.com");
        function.CapturedClaims["iss"].ShouldBe("https://auth.example.com");
        function.CapturedClaims["scope"].ShouldBe("openid");
    }

    [Fact]
    public async Task BuildRouteContext_WithLambdaAuthorizer_ReadsFromLambdaContext()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var function = new ClaimsCaptureFunction(mediator);
        var request = CreateRequestWithLambdaAuthorizer(new Dictionary<string, object>
        {
            { "sub", "ca-manager" },
            { "iss", "https://auth.example.com/openid/v1/realms/swepay-services" },
            { "realmId", "swepay-services" },
            { "scope", "client:create client:read" }
        });

        // Act
        await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        function.CapturedClaims.ShouldNotBeNull();
        function.CapturedClaims.Count.ShouldBe(4);
        function.CapturedClaims["sub"].ShouldBe("ca-manager");
        function.CapturedClaims["iss"].ShouldBe("https://auth.example.com/openid/v1/realms/swepay-services");
        function.CapturedClaims["realmId"].ShouldBe("swepay-services");
        function.CapturedClaims["scope"].ShouldBe("client:create client:read");
    }

    [Fact]
    public async Task BuildRouteContext_WithBothAuthorizers_PrefersJwtClaims()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var function = new ClaimsCaptureFunction(mediator);
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            RawPath = "/api/resource",
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = "GET" },
                Stage = "$default",
                Authorizer = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription
                {
                    Jwt = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription.JwtDescription
                    {
                        Claims = new Dictionary<string, string>
                        {
                            { "sub", "jwt-user" },
                            { "source", "jwt" }
                        }
                    },
                    Lambda = new Dictionary<string, object>
                    {
                        { "sub", "lambda-user" },
                        { "source", "lambda" }
                    }
                }
            }
        };

        // Act
        await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        function.CapturedClaims.ShouldNotBeNull();
        function.CapturedClaims["sub"].ShouldBe("jwt-user");
        function.CapturedClaims["source"].ShouldBe("jwt");
    }

    [Fact]
    public async Task BuildRouteContext_WithNoAuthorizer_ClaimsIsEmpty()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var function = new ClaimsCaptureFunction(mediator);
        var request = CreateRequest("/api/resource", "GET");

        // Act
        await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        function.CapturedClaims.ShouldNotBeNull();
        function.CapturedClaims.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildRouteContext_LambdaAuthorizer_ConvertsObjectValuesToStrings()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var function = new ClaimsCaptureFunction(mediator);
        var request = CreateRequestWithLambdaAuthorizer(new Dictionary<string, object>
        {
            { "sub", "user1" },
            { "exp", 1700000000 },
            { "active", true }
        });

        // Act
        await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        function.CapturedClaims.ShouldNotBeNull();
        function.CapturedClaims.Count.ShouldBe(3);
        function.CapturedClaims["sub"].ShouldBe("user1");
        function.CapturedClaims["exp"].ShouldBe("1700000000");
        function.CapturedClaims["active"].ShouldBe("True");
    }

    [Fact]
    public async Task BuildRouteContext_LambdaAuthorizer_SkipsNullValues()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var function = new ClaimsCaptureFunction(mediator);
        var request = CreateRequestWithLambdaAuthorizer(new Dictionary<string, object>
        {
            { "sub", "user1" },
            { "email", null! }
        });

        // Act
        await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        function.CapturedClaims.ShouldNotBeNull();
        function.CapturedClaims.ShouldContainKey("sub");
        function.CapturedClaims.Keys.ShouldNotContain("email");
    }

    [Fact]
    public async Task RequireRole_WithLambdaAuthorizer_MatchesRole()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var function = new ClaimsCaptureFunction(mediator);
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object>
            {
                { "sub", "admin-user" },
                { "roles", "Admin" }
            },
            path: "/api/admin");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
        function.CapturedClaims.ShouldNotBeNull();
        function.CapturedClaims["roles"].ShouldBe("Admin");
    }

    [Fact]
    public async Task RequireAuthorization_WithLambdaAuthorizer_Succeeds()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var function = new ClaimsCaptureFunction(mediator);
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object>
            {
                { "sub", "ca-manager" },
                { "realmId", "swepay-services" }
            },
            path: "/api/secure");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task RequireAuthorization_WithLambdaAuthorizer_DeniesWhenEmpty()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var function = new ClaimsCaptureFunction(mediator);
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object>(),
            path: "/api/secure");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(401);
    }

    // --- Mixed Policy Tests (Role + Scope) ---

    /// <summary>
    /// Test function with mixed authorization policies combining roles and scopes.
    /// </summary>
    private sealed class MixedPolicyFunction : RoutedApiGatewayFunction
    {
        public Dictionary<string, string>? CapturedClaims { get; private set; }

        public MixedPolicyFunction(IMediator mediator) : base(mediator) { }

        protected override void ConfigureRoutes(IRouteBuilder routes)
        {
            // Route requiring role only
            routes.MapGet<DocsCommand, DocsResponse>("/api/role-only", _ => new DocsCommand())
                .RequireRole("Admin");

            // Route requiring scope only
            routes.MapGet<DocsCommand, DocsResponse>("/api/scope-only", _ => new DocsCommand())
                .RequireClaim("scope", "client:read");

            // Route requiring role AND scope via named policy
            routes.MapPost<DocsCommand, DocsResponse>("/api/role-and-scope", _ => new DocsCommand())
                .RequireAuthorization("admin_with_write");

            // Route requiring role OR scope via custom assertion policy
            routes.MapGet<DocsCommand, DocsResponse>("/api/role-or-scope", _ => new DocsCommand())
                .RequireAuthorization("admin_or_reader");

            // Route with inline role + inline scope chained
            routes.MapDelete<DocsCommand, DocsResponse>("/api/chained", _ => new DocsCommand())
                .RequireRole("Admin")
                .RequireClaim("scope", "client:delete");

            // Route with multiple allowed scope values
            routes.MapPut<DocsCommand, DocsResponse>("/api/multi-scope", _ => new DocsCommand())
                .RequireClaim("scope", "client:read", "client:write");
        }

        protected override void ConfigureAuthorization(AuthorizationBuilder auth)
        {
            // AND policy: requires Admin role AND client:write scope
            auth.AddPolicy("admin_with_write", policy =>
                policy.RequireRole("Admin")
                      .RequireClaim("scope", "client:write"));

            // OR policy: requires Admin role OR client:read scope (either is sufficient)
            auth.AddPolicy("admin_or_reader", policy =>
                policy.RequireAssertion(ctx =>
                {
                    var roles = ctx.Claims.GetValueOrDefault("roles", "");
                    var scope = ctx.Claims.GetValueOrDefault("scope", "");

                    var isAdmin = roles.Contains("Admin", StringComparison.OrdinalIgnoreCase);
                    var hasReadScope = scope.Contains("client:read", StringComparison.OrdinalIgnoreCase);

                    return isAdmin || hasReadScope;
                }));
        }

        protected override Task<object> ExecuteCommandAsync(RouteMatch match, RouteContext context, IMediator mediator)
        {
            CapturedClaims = new Dictionary<string, string>(context.Claims);
            return Task.FromResult<object>(new DocsResponse { Html = "ok" });
        }

        protected override string SerializeResponse(object response)
        {
            return response switch
            {
                DocsResponse r => r.Html,
                ErrorResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.ErrorResponse),
                RouteNotFoundResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.RouteNotFoundResponse),
                HealthCheckResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.HealthCheckResponse),
                _ => throw new NotSupportedException($"No serializer for {response.GetType().Name}")
            };
        }
    }

    // -- Role-only route --

    [Fact]
    public async Task RoleOnly_WithRole_Succeeds()
    {
        // Arrange
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "user1" }, { "roles", "Admin" } },
            path: "/api/role-only");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task RoleOnly_WithScopeButNoRole_Returns403()
    {
        // Arrange
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "user1" }, { "scope", "client:read client:write" } },
            path: "/api/role-only");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(403);
    }

    // -- Scope-only route --

    [Fact]
    public async Task ScopeOnly_WithScope_Succeeds()
    {
        // Arrange
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "user1" }, { "scope", "client:read" } },
            path: "/api/scope-only");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task ScopeOnly_WithRoleButNoScope_Returns403()
    {
        // Arrange
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "user1" }, { "roles", "Admin" } },
            path: "/api/scope-only");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task ScopeOnly_WithScopeAmongMultiple_Succeeds()
    {
        // Arrange: scope claim has multiple space-separated values
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithJwtAuthorizer(
            new Dictionary<string, string> { { "sub", "user1" }, { "scope", "openid client:read profile" } },
            path: "/api/scope-only");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    // -- Role AND Scope policy (admin_with_write) --

    [Fact]
    public async Task RoleAndScopePolicy_WithBoth_Succeeds()
    {
        // Arrange
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object>
            {
                { "sub", "admin1" },
                { "roles", "Admin" },
                { "scope", "client:write client:read" }
            },
            path: "/api/role-and-scope", method: "POST");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task RoleAndScopePolicy_WithRoleOnly_Returns403()
    {
        // Arrange: has Admin role but missing client:write scope
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "admin1" }, { "roles", "Admin" } },
            path: "/api/role-and-scope", method: "POST");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task RoleAndScopePolicy_WithScopeOnly_Returns403()
    {
        // Arrange: has client:write scope but no Admin role
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "user1" }, { "scope", "client:write" } },
            path: "/api/role-and-scope", method: "POST");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task RoleAndScopePolicy_WithNeither_Returns403()
    {
        // Arrange: authenticated but has no role and no matching scope
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithJwtAuthorizer(
            new Dictionary<string, string> { { "sub", "user1" }, { "scope", "openid" } },
            path: "/api/role-and-scope", method: "POST");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task RoleAndScopePolicy_ViaJwt_WithBoth_Succeeds()
    {
        // Arrange: same AND policy but using JWT authorizer
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithJwtAuthorizer(
            new Dictionary<string, string>
            {
                { "sub", "jwt-admin" },
                { "roles", "Admin" },
                { "scope", "client:write" }
            },
            path: "/api/role-and-scope", method: "POST");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    // -- Role OR Scope policy (admin_or_reader) --

    [Fact]
    public async Task RoleOrScopePolicy_WithRoleOnly_Succeeds()
    {
        // Arrange: has Admin role, no scope
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "admin1" }, { "roles", "Admin" } },
            path: "/api/role-or-scope");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task RoleOrScopePolicy_WithScopeOnly_Succeeds()
    {
        // Arrange: has client:read scope, no Admin role
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithJwtAuthorizer(
            new Dictionary<string, string> { { "sub", "reader1" }, { "scope", "client:read" } },
            path: "/api/role-or-scope");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task RoleOrScopePolicy_WithBoth_Succeeds()
    {
        // Arrange: has both Admin role and client:read scope
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object>
            {
                { "sub", "superuser" },
                { "roles", "Admin" },
                { "scope", "client:read client:write" }
            },
            path: "/api/role-or-scope");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task RoleOrScopePolicy_WithNeither_Returns403()
    {
        // Arrange: authenticated but has no Admin role and no client:read scope
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object>
            {
                { "sub", "user1" },
                { "roles", "User" },
                { "scope", "openid" }
            },
            path: "/api/role-or-scope");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task RoleOrScopePolicy_ViaJwt_WithRoleOnly_Succeeds()
    {
        // Arrange: same OR policy but using JWT authorizer
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithJwtAuthorizer(
            new Dictionary<string, string> { { "sub", "jwt-admin" }, { "roles", "Admin" } },
            path: "/api/role-or-scope");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    // -- Chained inline role + scope --

    [Fact]
    public async Task ChainedRoleAndScope_WithBoth_Succeeds()
    {
        // Arrange
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithJwtAuthorizer(
            new Dictionary<string, string>
            {
                { "sub", "admin1" },
                { "roles", "Admin" },
                { "scope", "client:delete" }
            },
            path: "/api/chained", method: "DELETE");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task ChainedRoleAndScope_WithRoleOnly_Returns403()
    {
        // Arrange: has Admin but missing client:delete scope
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "admin1" }, { "roles", "Admin" } },
            path: "/api/chained", method: "DELETE");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task ChainedRoleAndScope_WithScopeOnly_Returns403()
    {
        // Arrange: has client:delete scope but no Admin role
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "user1" }, { "scope", "client:delete" } },
            path: "/api/chained", method: "DELETE");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(403);
    }

    // -- Multiple allowed scope values --

    [Fact]
    public async Task MultiScope_WithFirstAllowedScope_Succeeds()
    {
        // Arrange: has client:read which is one of the allowed values
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "user1" }, { "scope", "client:read openid" } },
            path: "/api/multi-scope", method: "PUT");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task MultiScope_WithSecondAllowedScope_Succeeds()
    {
        // Arrange: has client:write which is also an allowed value
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithJwtAuthorizer(
            new Dictionary<string, string> { { "sub", "user1" }, { "scope", "client:write" } },
            path: "/api/multi-scope", method: "PUT");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task MultiScope_WithBothAllowedScopes_Succeeds()
    {
        // Arrange: has both client:read and client:write
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "user1" }, { "scope", "client:read client:write openid" } },
            path: "/api/multi-scope", method: "PUT");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task MultiScope_WithNoMatchingScope_Returns403()
    {
        // Arrange: has only openid scope, neither client:read nor client:write
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequestWithLambdaAuthorizer(
            new Dictionary<string, object> { { "sub", "user1" }, { "scope", "openid profile" } },
            path: "/api/multi-scope", method: "PUT");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(403);
    }

    // -- Unauthenticated access to mixed policy routes --

    [Fact]
    public async Task MixedPolicy_Unauthenticated_Returns401()
    {
        // Arrange: no authorizer at all on a protected route
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequest("/api/role-and-scope", "POST");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task ChainedRoute_Unauthenticated_Returns401()
    {
        // Arrange: no authorizer on inline chained role+scope route
        var function = new MixedPolicyFunction(Substitute.For<IMediator>());
        var request = CreateRequest("/api/chained", "DELETE");

        // Act
        var response = await function.FunctionHandler(request, Substitute.For<ILambdaContext>());

        // Assert
        response.StatusCode.ShouldBe(401);
    }

    // --- Helpers ---

    private static APIGatewayHttpApiV2ProxyRequest CreateRequest(string path, string method)
    {
        return new APIGatewayHttpApiV2ProxyRequest
        {
            RawPath = path,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription
                {
                    Method = method
                },
                Stage = "$default"
            }
        };
    }

    private static APIGatewayHttpApiV2ProxyRequest CreateRequestWithJwtAuthorizer(
        Dictionary<string, string> claims,
        string path = "/api/resource",
        string method = "GET")
    {
        return new APIGatewayHttpApiV2ProxyRequest
        {
            RawPath = path,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = method },
                Stage = "$default",
                Authorizer = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription
                {
                    Jwt = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription.JwtDescription
                    {
                        Claims = claims
                    }
                }
            }
        };
    }

    private static APIGatewayHttpApiV2ProxyRequest CreateRequestWithLambdaAuthorizer(
        Dictionary<string, object> lambdaContext,
        string path = "/api/resource",
        string method = "GET")
    {
        return new APIGatewayHttpApiV2ProxyRequest
        {
            RawPath = path,
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = method },
                Stage = "$default",
                Authorizer = new APIGatewayHttpApiV2ProxyRequest.AuthorizerDescription
                {
                    Lambda = lambdaContext
                }
            }
        };
    }
}
