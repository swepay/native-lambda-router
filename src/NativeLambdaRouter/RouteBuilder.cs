using System.Text.RegularExpressions;

namespace NativeLambdaRouter;

/// <summary>
/// Interface for route configuration. Implement this in each Lambda Function
/// to define the routes it handles.
/// </summary>
public interface IRouteConfiguration
{
    /// <summary>
    /// Configure the routes for this Lambda Function.
    /// </summary>
    void Configure(IRouteBuilder builder);
}

/// <summary>
/// Builder interface for configuring routes.
/// </summary>
public interface IRouteBuilder
{
    /// <summary>
    /// Maps a GET request to a command.
    /// </summary>
    IRouteBuilder MapGet<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull;

    /// <summary>
    /// Maps a POST request to a command.
    /// </summary>
    IRouteBuilder MapPost<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull;

    /// <summary>
    /// Maps a PUT request to a command.
    /// </summary>
    IRouteBuilder MapPut<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull;

    /// <summary>
    /// Maps a DELETE request to a command.
    /// </summary>
    IRouteBuilder MapDelete<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull;

    /// <summary>
    /// Maps a PATCH request to a command.
    /// </summary>
    IRouteBuilder MapPatch<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull;

    /// <summary>
    /// Maps a custom HTTP method to a command.
    /// </summary>
    IRouteBuilder Map<TCommand, TResponse>(
        string method,
        string path,
        Func<RouteContext, TCommand> commandFactory,
        bool requiresAuth = true)
        where TCommand : notnull;
}

/// <summary>
/// Implementation of the route builder.
/// </summary>
public sealed partial class RouteBuilder : IRouteBuilder
{
    private readonly List<RouteDefinition> _routes = [];

    /// <summary>
    /// Gets the configured routes.
    /// </summary>
    public IReadOnlyList<RouteDefinition> Routes => _routes;

    /// <inheritdoc />
    public IRouteBuilder MapGet<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull
        => Map<TCommand, TResponse>(HttpMethod.GET, path, commandFactory);

    /// <inheritdoc />
    public IRouteBuilder MapPost<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull
        => Map<TCommand, TResponse>(HttpMethod.POST, path, commandFactory);

    /// <inheritdoc />
    public IRouteBuilder MapPut<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull
        => Map<TCommand, TResponse>(HttpMethod.PUT, path, commandFactory);

    /// <inheritdoc />
    public IRouteBuilder MapDelete<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull
        => Map<TCommand, TResponse>(HttpMethod.DELETE, path, commandFactory);

    /// <inheritdoc />
    public IRouteBuilder MapPatch<TCommand, TResponse>(
        string path,
        Func<RouteContext, TCommand> commandFactory)
        where TCommand : notnull
        => Map<TCommand, TResponse>(HttpMethod.PATCH, path, commandFactory);

    /// <inheritdoc />
    public IRouteBuilder Map<TCommand, TResponse>(
        string method,
        string path,
        Func<RouteContext, TCommand> commandFactory,
        bool requiresAuth = true)
        where TCommand : notnull
    {
        _routes.Add(new RouteDefinition
        {
            Method = method.ToUpperInvariant(),
            Path = NormalizePath(path),
            CommandFactory = ctx => commandFactory(ctx),
            ResponseType = typeof(TResponse),
            RequiresAuth = requiresAuth
        });

        return this;
    }

    private static string NormalizePath(string path)
    {
        path = path.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        if (path.EndsWith('/') && path.Length > 1)
            path = path[..^1];
        return path.ToLowerInvariant();
    }
}

/// <summary>
/// Route matcher that finds the matching route for a given request.
/// </summary>
public sealed partial class RouteMatcher
{
    private readonly List<(RouteDefinition Route, Regex Pattern)> _compiledRoutes;

    /// <summary>
    /// Creates a new route matcher with the specified routes.
    /// </summary>
    public RouteMatcher(IReadOnlyList<RouteDefinition> routes)
    {
        _compiledRoutes = [.. routes.Select(r => (r, CompilePattern(r.Path)))];
    }

    /// <summary>
    /// Finds a matching route for the given method and path.
    /// </summary>
    public RouteMatch? Match(string method, string path)
    {
        method = method.ToUpperInvariant();
        path = NormalizePath(path);

        foreach (var (route, pattern) in _compiledRoutes)
        {
            if (route.Method != method)
                continue;

            var match = pattern.Match(path);
            if (!match.Success)
                continue;

            var pathParams = new Dictionary<string, string>();
            foreach (var groupName in pattern.GetGroupNames())
            {
                if (groupName == "0") continue; // Skip the full match group
                var group = match.Groups[groupName];
                if (group.Success)
                {
                    pathParams[groupName] = group.Value;
                }
            }

            return new RouteMatch(route, pathParams);
        }

        return null;
    }

    private static Regex CompilePattern(string path)
    {
        // Replace {paramName} with named capture groups
        var pattern = PathParamPattern().Replace(path, @"(?<$1>[^/]+)");
        pattern = "^" + pattern + "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static string NormalizePath(string path)
    {
        path = path.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;
        if (path.EndsWith('/') && path.Length > 1)
            path = path[..^1];
        return path.ToLowerInvariant();
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PathParamPattern();
}

/// <summary>
/// Result of a successful route match.
/// </summary>
public sealed class RouteMatch
{
    /// <summary>
    /// The matched route definition.
    /// </summary>
    public RouteDefinition Route { get; }

    /// <summary>
    /// Path parameters extracted from the URL.
    /// </summary>
    public Dictionary<string, string> PathParameters { get; }

    /// <summary>
    /// Creates a new route match result.
    /// </summary>
    public RouteMatch(RouteDefinition route, Dictionary<string, string> pathParameters)
    {
        Route = route;
        PathParameters = pathParameters;
    }
}
