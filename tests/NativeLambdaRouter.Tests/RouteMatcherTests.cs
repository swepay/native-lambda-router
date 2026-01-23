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
        result.Should().BeNull();
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
        result.Should().BeNull();
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
        result.Should().BeNull();
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
        result.Should().NotBeNull();
        result!.Route.Path.Should().Be("/items");
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
        result.Should().NotBeNull();
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
        result.Should().NotBeNull();
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
        result.Should().NotBeNull();
        result!.PathParameters.Should().ContainKey("id");
        result.PathParameters["id"].Should().Be("123");
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
        result.Should().NotBeNull();
        result!.PathParameters.Should().HaveCount(2);
        // Note: Parameter names are lowercased due to path normalization
        result.PathParameters["userid"].Should().Be("user-123");
        result.PathParameters["orderid"].Should().Be("order-456");
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
        result.Should().NotBeNull();
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
        result.Should().NotBeNull();
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
        result.Should().NotBeNull();
        ((TestCommand)result!.Route.CommandFactory(new RouteContext())).Value.Should().Be("first");
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
        result.Should().BeNull();
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
        result.Should().NotBeNull();
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
        result.Should().NotBeNull();
        result!.PathParameters["resource"].Should().Be("products");
        result.PathParameters["id"].Should().Be("prod-123");
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
