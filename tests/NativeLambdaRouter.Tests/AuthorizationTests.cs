using FluentAssertions;

namespace NativeLambdaRouter.Tests;

public class AuthorizationTests
{
    [Fact]
    public void RequireAuthorization_ShouldSetRequiresAuthentication()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"))
            .RequireAuthorization();

        // Assert
        builder.Routes[0].AuthorizationOptions.RequiresAuthentication.Should().BeTrue();
    }

    [Fact]
    public void RequireAuthorization_WithPolicy_ShouldAddPolicyName()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"))
            .RequireAuthorization("admin_only");

        // Assert
        builder.Routes[0].AuthorizationOptions.PolicyNames.Should().Contain("admin_only");
    }

    [Fact]
    public void RequireAuthorization_WithMultiplePolicies_ShouldAddAllPolicyNames()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"))
            .RequireAuthorization("policy1", "policy2");

        // Assert
        builder.Routes[0].AuthorizationOptions.PolicyNames.Should().BeEquivalentTo(["policy1", "policy2"]);
    }

    [Fact]
    public void RequireRole_ShouldAddRoles()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"))
            .RequireRole("admin", "superuser");

        // Assert
        builder.Routes[0].AuthorizationOptions.Roles.Should().BeEquivalentTo(["admin", "superuser"]);
        builder.Routes[0].AuthorizationOptions.RequiresAuthentication.Should().BeTrue();
    }

    [Fact]
    public void RequireClaim_ShouldAddClaim()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"))
            .RequireClaim("scope", "api:read", "api:write");

        // Assert
        builder.Routes[0].AuthorizationOptions.Claims.Should().ContainKey("scope");
        builder.Routes[0].AuthorizationOptions.Claims["scope"].Should().BeEquivalentTo(["api:read", "api:write"]);
    }

    [Fact]
    public void AllowAnonymous_ShouldSetAllowAnonymous()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"))
            .AllowAnonymous();

        // Assert
        builder.Routes[0].AuthorizationOptions.AllowAnonymous.Should().BeTrue();
        builder.Routes[0].AuthorizationOptions.RequiresAuthentication.Should().BeFalse();
    }

    [Fact]
    public void FluentChaining_ShouldWorkWithMultipleRequirements()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapPut<TestCommand, TestResponse>("/items/{id}", ctx => new TestCommand("test"))
            .RequireAuthorization("admin_greetings")
            .RequireRole("admin")
            .RequireClaim("scope", "greetings_api");

        // Assert
        var options = builder.Routes[0].AuthorizationOptions;
        options.PolicyNames.Should().Contain("admin_greetings");
        options.Roles.Should().Contain("admin");
        options.Claims["scope"].Should().Contain("greetings_api");
    }

    [Fact]
    public void AuthorizationService_ShouldAllowAnonymous()
    {
        // Arrange
        var service = new AuthorizationService();
        var context = new RouteContext();
        var options = new RouteAuthorizationOptions { AllowAnonymous = true };

        // Act
        var result = service.Authorize(context, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AuthorizationService_ShouldDenyUnauthenticatedUser()
    {
        // Arrange
        var service = new AuthorizationService();
        var context = new RouteContext(); // No claims = not authenticated
        var options = new RouteAuthorizationOptions { RequiresAuthentication = true };

        // Act
        var result = service.Authorize(context, options);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("not authenticated");
    }

    [Fact]
    public void AuthorizationService_ShouldAllowAuthenticatedUser()
    {
        // Arrange
        var service = new AuthorizationService();
        var context = new RouteContext
        {
            Claims = new Dictionary<string, string> { { "sub", "user123" } }
        };
        var options = new RouteAuthorizationOptions { RequiresAuthentication = true };

        // Act
        var result = service.Authorize(context, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AuthorizationService_ShouldValidateRoles()
    {
        // Arrange
        var service = new AuthorizationService();
        var context = new RouteContext
        {
            Claims = new Dictionary<string, string>
            {
                { "sub", "user123" },
                { "role", "admin" }
            }
        };
        var options = new RouteAuthorizationOptions();
        options.Roles.Add("admin");

        // Act
        var result = service.Authorize(context, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AuthorizationService_ShouldDenyWhenRoleMissing()
    {
        // Arrange
        var service = new AuthorizationService();
        var context = new RouteContext
        {
            Claims = new Dictionary<string, string>
            {
                { "sub", "user123" },
                { "role", "user" }
            }
        };
        var options = new RouteAuthorizationOptions();
        options.Roles.Add("admin");

        // Act
        var result = service.Authorize(context, options);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.FailureMessage.Should().Contain("admin");
    }

    [Fact]
    public void AuthorizationService_ShouldValidateClaims()
    {
        // Arrange
        var service = new AuthorizationService();
        var context = new RouteContext
        {
            Claims = new Dictionary<string, string>
            {
                { "sub", "user123" },
                { "scope", "api:read api:write" }
            }
        };
        var options = new RouteAuthorizationOptions();
        options.Claims["scope"] = ["api:read"];

        // Act
        var result = service.Authorize(context, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AuthorizationService_ShouldValidatePolicy()
    {
        // Arrange
        var authBuilder = new AuthorizationBuilder();
        authBuilder.AddPolicy("admin_api", policy =>
            policy.RequireRole("admin").RequireClaim("scope", "api:read"));

        var service = new AuthorizationService(authBuilder.Policies);
        var context = new RouteContext
        {
            Claims = new Dictionary<string, string>
            {
                { "sub", "user123" },
                { "role", "admin" },
                { "scope", "api:read" }
            }
        };
        var options = new RouteAuthorizationOptions();
        options.PolicyNames.Add("admin_api");

        // Act
        var result = service.Authorize(context, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AuthorizationService_ShouldDenyWhenPolicyFails()
    {
        // Arrange
        var authBuilder = new AuthorizationBuilder();
        authBuilder.AddPolicy("admin_api", policy =>
            policy.RequireRole("admin").RequireClaim("scope", "api:read"));

        var service = new AuthorizationService(authBuilder.Policies);
        var context = new RouteContext
        {
            Claims = new Dictionary<string, string>
            {
                { "sub", "user123" },
                { "role", "user" }, // Not admin
                { "scope", "api:read" }
            }
        };
        var options = new RouteAuthorizationOptions();
        options.PolicyNames.Add("admin_api");

        // Act
        var result = service.Authorize(context, options);

        // Assert
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void AuthorizationBuilder_ShouldBuildPolicy()
    {
        // Arrange & Act
        var builder = new AuthorizationBuilder();
        builder.AddPolicy("test_policy", policy =>
            policy
                .RequireRole("admin", "superuser")
                .RequireClaim("scope", "api:read", "api:write")
                .RequireAssertion(ctx => ctx.Headers.ContainsKey("X-Custom-Header")));

        // Assert
        var policy = builder.Policies["test_policy"];
        policy.Name.Should().Be("test_policy");
        policy.RequiredRoles.Should().BeEquivalentTo(["admin", "superuser"]);
        policy.RequiredClaims["scope"].Should().BeEquivalentTo(["api:read", "api:write"]);
        policy.Requirements.Should().HaveCount(1);
    }

    [Fact]
    public void AuthorizationService_ShouldSupportCognitoGroups()
    {
        // Arrange
        var service = new AuthorizationService();
        var context = new RouteContext
        {
            Claims = new Dictionary<string, string>
            {
                { "sub", "user123" },
                { "cognito:groups", "[\"admin\",\"users\"]" }
            }
        };
        var options = new RouteAuthorizationOptions();
        options.Roles.Add("admin");

        // Act
        var result = service.Authorize(context, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    private record TestCommand(string Value);
    private record TestResponse(string Result);
}
