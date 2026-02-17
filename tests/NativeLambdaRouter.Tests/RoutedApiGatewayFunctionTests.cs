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
}
