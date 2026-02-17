namespace NativeLambdaRouter.Tests;

public class RouteMatcherTests
{
    [Fact]
    public void Match_ShouldReturnNull_WhenNoRoutesRegistered()
    {
        // Arrange
        var matcher = new RouteMatcher([]);

        // Act
        var result = matcher.Match("GET", "/items");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Match_ShouldReturnNull_WhenMethodDoesNotMatch()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.POST, "/items")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/items");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Match_ShouldReturnNull_WhenPathDoesNotMatch()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/items")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/products");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Match_ShouldReturnMatch_WhenExactPathMatches()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/items")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/items");

        // Assert
        result.ShouldNotBeNull();
        result!.Route.Path.ShouldBe("/items");
    }

    [Fact]
    public void Match_ShouldBeCaseInsensitive_ForMethod()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/items")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("get", "/items");

        // Assert
        result.ShouldNotBeNull();
    }

    [Fact]
    public void Match_ShouldBeCaseInsensitive_ForPath()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/items")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/ITEMS");

        // Assert
        result.ShouldNotBeNull();
    }

    [Fact]
    public void Match_ShouldExtractSinglePathParameter()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/items/{id}")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/items/123");

        // Assert
        result.ShouldNotBeNull();
        result!.PathParameters.ShouldContainKey("id");
        result.PathParameters["id"].ShouldBe("123");
    }

    [Fact]
    public void Match_ShouldExtractMultiplePathParameters()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/users/{userId}/orders/{orderId}")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/users/user-123/orders/order-456");

        // Assert
        result.ShouldNotBeNull();
        result!.PathParameters.Count.ShouldBe(2);
        // Parameter names are lowercased from the route template normalization
        result.PathParameters["userid"].ShouldBe("user-123");
        result.PathParameters["orderid"].ShouldBe("order-456");
    }

    [Fact]
    public void Match_ShouldPreservePathParameterCase()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/realms/{realmId}/.well-known/openid-configuration")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/realms/Master/.well-known/openid-configuration");

        // Assert
        result.ShouldNotBeNull();
        result!.PathParameters["realmid"].ShouldBe("Master");
    }

    [Fact]
    public void Match_ShouldPreservePathParameterCase_WithMixedCaseValues()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/api/{tenantName}/users/{userId}")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/api/MyTenant/users/User-ABC-123");

        // Assert
        result.ShouldNotBeNull();
        result!.PathParameters.Count.ShouldBe(2);
        result.PathParameters["tenantname"].ShouldBe("MyTenant");
        result.PathParameters["userid"].ShouldBe("User-ABC-123");
    }

    [Fact]
    public void Match_ShouldHandlePathWithTrailingSlash()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/items")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/items/");

        // Assert
        result.ShouldNotBeNull();
    }

    [Fact]
    public void Match_ShouldHandlePathWithoutLeadingSlash()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/items")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "items");

        // Assert
        result.ShouldNotBeNull();
    }

    [Fact]
    public void Match_ShouldReturnFirstMatchingRoute()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/items", "first"),
            CreateRoute(HttpMethod.GET, "/items", "second")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/items");

        // Assert
        result.ShouldNotBeNull();
        ((TestCommand)result!.Route.CommandFactory(new RouteContext())).Value.ShouldBe("first");
    }

    [Fact]
    public void Match_ShouldNotMatchPartialPaths()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/items")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/items/extra");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void Match_ShouldHandleRootPath()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/");

        // Assert
        result.ShouldNotBeNull();
    }

    [Fact]
    public void Match_ShouldHandleComplexPathParameters()
    {
        // Arrange
        var routes = new List<RouteDefinition>
        {
            CreateRoute(HttpMethod.GET, "/api/v1/{resource}/{id}/details")
        };
        var matcher = new RouteMatcher(routes);

        // Act
        var result = matcher.Match("GET", "/api/v1/products/prod-123/details");

        // Assert
        result.ShouldNotBeNull();
        result!.PathParameters["resource"].ShouldBe("products");
        result.PathParameters["id"].ShouldBe("prod-123");
    }

    private static RouteDefinition CreateRoute(string method, string path, string commandValue = "test")
    {
        return new RouteDefinition
        {
            Method = method,
            Path = path.ToLowerInvariant(),
            CommandFactory = _ => new TestCommand(commandValue),
            ResponseType = typeof(TestResponse)
        };
    }
}
