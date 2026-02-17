namespace NativeLambdaRouter.Tests;

public class RouteContextTests
{
    [Fact]
    public void RouteContext_ShouldHaveEmptyCollections_ByDefault()
    {
        // Arrange & Act
        var context = new RouteContext();

        // Assert
        context.Body.ShouldBeNull();
        context.PathParameters.ShouldBeEmpty();
        context.QueryParameters.ShouldBeEmpty();
        context.Headers.ShouldBeEmpty();
        context.Claims.ShouldBeEmpty();
    }

    [Fact]
    public void RouteContext_ShouldAllowSettingBody()
    {
        // Arrange & Act
        var context = new RouteContext { Body = "{\"name\":\"test\"}" };

        // Assert
        context.Body.ShouldBe("{\"name\":\"test\"}");
    }

    [Fact]
    public void RouteContext_ShouldAllowSettingPathParameters()
    {
        // Arrange & Act
        var context = new RouteContext
        {
            PathParameters = new Dictionary<string, string>
            {
                { "id", "123" },
                { "name", "test" }
            }
        };

        // Assert
        context.PathParameters.Count.ShouldBe(2);
        context.PathParameters["id"].ShouldBe("123");
        context.PathParameters["name"].ShouldBe("test");
    }

    [Fact]
    public void RouteContext_ShouldAllowSettingQueryParameters()
    {
        // Arrange & Act
        var context = new RouteContext
        {
            QueryParameters = new Dictionary<string, string>
            {
                { "page", "1" },
                { "limit", "10" }
            }
        };

        // Assert
        context.QueryParameters.Count.ShouldBe(2);
        context.QueryParameters["page"].ShouldBe("1");
    }

    [Fact]
    public void RouteContext_ShouldAllowSettingHeaders()
    {
        // Arrange & Act
        var context = new RouteContext
        {
            Headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer token" },
                { "Content-Type", "application/json" }
            }
        };

        // Assert
        context.Headers.Count.ShouldBe(2);
        context.Headers["Authorization"].ShouldBe("Bearer token");
    }

    [Fact]
    public void RouteContext_ShouldAllowSettingClaims()
    {
        // Arrange & Act
        var context = new RouteContext
        {
            Claims = new Dictionary<string, string>
            {
                { "sub", "user-123" },
                { "email", "user@example.com" }
            }
        };

        // Assert
        context.Claims.Count.ShouldBe(2);
        context.Claims["sub"].ShouldBe("user-123");
    }
}
