namespace NativeLambdaRouter.Tests;

public class RouteBuilderTests
{
    [Fact]
    public void MapGet_ShouldAddRoute_WithCorrectMethod()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"));

        // Assert
        builder.Routes.Should().HaveCount(1);
        builder.Routes[0].Method.Should().Be(HttpMethod.GET);
        builder.Routes[0].Path.Should().Be("/items");
    }

    [Fact]
    public void MapPost_ShouldAddRoute_WithCorrectMethod()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapPost<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"));

        // Assert
        builder.Routes.Should().HaveCount(1);
        builder.Routes[0].Method.Should().Be(HttpMethod.POST);
    }

    [Fact]
    public void MapPut_ShouldAddRoute_WithCorrectMethod()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapPut<TestCommand, TestResponse>("/items/{id}", ctx => new TestCommand("test"));

        // Assert
        builder.Routes.Should().HaveCount(1);
        builder.Routes[0].Method.Should().Be(HttpMethod.PUT);
    }

    [Fact]
    public void MapDelete_ShouldAddRoute_WithCorrectMethod()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapDelete<TestCommand, TestResponse>("/items/{id}", ctx => new TestCommand("test"));

        // Assert
        builder.Routes.Should().HaveCount(1);
        builder.Routes[0].Method.Should().Be(HttpMethod.DELETE);
    }

    [Fact]
    public void MapPatch_ShouldAddRoute_WithCorrectMethod()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapPatch<TestCommand, TestResponse>("/items/{id}", ctx => new TestCommand("test"));

        // Assert
        builder.Routes.Should().HaveCount(1);
        builder.Routes[0].Method.Should().Be(HttpMethod.PATCH);
    }

    [Fact]
    public void Map_ShouldNormalizePath_WithoutLeadingSlash()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("items", ctx => new TestCommand("test"));

        // Assert
        builder.Routes[0].Path.Should().Be("/items");
    }

    [Fact]
    public void Map_ShouldNormalizePath_WithTrailingSlash()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items/", ctx => new TestCommand("test"));

        // Assert
        builder.Routes[0].Path.Should().Be("/items");
    }

    [Fact]
    public void Map_ShouldNormalizePath_ToLowerCase()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/ITEMS", ctx => new TestCommand("test"));

        // Assert
        builder.Routes[0].Path.Should().Be("/items");
    }

    [Fact]
    public void Map_ShouldSupportFluentChaining()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder
            .MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("get"))
            .MapPost<TestCommand, TestResponse>("/items", ctx => new TestCommand("post"))
            .MapDelete<TestCommand, TestResponse>("/items/{id}", ctx => new TestCommand("delete"));

        // Assert
        builder.Routes.Should().HaveCount(3);
    }

    [Fact]
    public void Map_ShouldSetRequiresAuth_ToTrueByDefault()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"));

        // Assert
        builder.Routes[0].RequiresAuth.Should().BeTrue();
    }

    [Fact]
    public void Map_ShouldAllowSettingRequiresAuth_ToFalse()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.Map<TestCommand, TestResponse>(HttpMethod.GET, "/public", ctx => new TestCommand("test"), requiresAuth: false);

        // Assert
        builder.Routes[0].RequiresAuth.Should().BeFalse();
    }

    [Fact]
    public void Map_ShouldSetCorrectResponseType()
    {
        // Arrange
        var builder = new RouteBuilder();

        // Act
        builder.MapGet<TestCommand, TestResponse>("/items", ctx => new TestCommand("test"));

        // Assert
        builder.Routes[0].ResponseType.Should().Be(typeof(TestResponse));
    }

    [Fact]
    public void CommandFactory_ShouldCreateCommand_FromContext()
    {
        // Arrange
        var builder = new RouteBuilder();
        builder.MapPost<TestCommand, TestResponse>("/items", ctx => new TestCommand(ctx.Body ?? "default"));

        var context = new RouteContext { Body = "test-body" };

        // Act
        var command = builder.Routes[0].CommandFactory(context);

        // Assert
        command.Should().BeOfType<TestCommand>();
        ((TestCommand)command).Value.Should().Be("test-body");
    }
}

// Test types
public record TestCommand(string Value);
public record TestResponse(string Result);
