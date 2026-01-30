namespace NativeLambdaRouter;

/// <summary>
/// Builder for configuring route endpoint options like authorization.
/// Provides a fluent API similar to ASP.NET Core Minimal APIs.
/// </summary>
public interface IRouteEndpointBuilder
{
    /// <summary>
    /// Requires authorization using the specified policy names.
    /// </summary>
    /// <param name="policyNames">The policy names to require.</param>
    /// <returns>The endpoint builder for further configuration.</returns>
    IRouteEndpointBuilder RequireAuthorization(params string[] policyNames);

    /// <summary>
    /// Requires the user to have at least one of the specified roles.
    /// </summary>
    /// <param name="roles">The roles to require.</param>
    /// <returns>The endpoint builder for further configuration.</returns>
    IRouteEndpointBuilder RequireRole(params string[] roles);

    /// <summary>
    /// Requires the user to have a claim with one of the specified values.
    /// </summary>
    /// <param name="claimType">The claim type.</param>
    /// <param name="allowedValues">The allowed values for the claim.</param>
    /// <returns>The endpoint builder for further configuration.</returns>
    IRouteEndpointBuilder RequireClaim(string claimType, params string[] allowedValues);

    /// <summary>
    /// Allows anonymous access to the endpoint, bypassing authorization.
    /// </summary>
    /// <returns>The endpoint builder for further configuration.</returns>
    IRouteEndpointBuilder AllowAnonymous();

    /// <summary>
    /// Sets the response content type for this endpoint.
    /// </summary>
    /// <param name="contentType">The content type (e.g., text/html).</param>
    /// <returns>The endpoint builder for further configuration.</returns>
    IRouteEndpointBuilder Produces(string contentType);

    /// <summary>
    /// Adds a response header for this endpoint.
    /// </summary>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    /// <returns>The endpoint builder for further configuration.</returns>
    IRouteEndpointBuilder WithHeader(string name, string value);
}

/// <summary>
/// Implementation of the route endpoint builder.
/// </summary>
internal sealed class RouteEndpointBuilder : IRouteEndpointBuilder
{
    private readonly RouteDefinition _route;

    internal RouteEndpointBuilder(RouteDefinition route)
    {
        _route = route;
    }

    /// <inheritdoc />
    public IRouteEndpointBuilder RequireAuthorization(params string[] policyNames)
    {
        _route.AuthorizationOptions.RequiresAuthentication = true;

        if (policyNames.Length > 0)
        {
            _route.AuthorizationOptions.PolicyNames.AddRange(policyNames);
        }

        return this;
    }

    /// <inheritdoc />
    public IRouteEndpointBuilder RequireRole(params string[] roles)
    {
        _route.AuthorizationOptions.RequiresAuthentication = true;
        _route.AuthorizationOptions.Roles.AddRange(roles);
        return this;
    }

    /// <inheritdoc />
    public IRouteEndpointBuilder RequireClaim(string claimType, params string[] allowedValues)
    {
        _route.AuthorizationOptions.RequiresAuthentication = true;
        _route.AuthorizationOptions.Claims[claimType] = allowedValues;
        return this;
    }

    /// <inheritdoc />
    public IRouteEndpointBuilder AllowAnonymous()
    {
        _route.AuthorizationOptions.AllowAnonymous = true;
        _route.AuthorizationOptions.RequiresAuthentication = false;
        return this;
    }

    /// <inheritdoc />
    public IRouteEndpointBuilder Produces(string contentType)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        _route.ResponseContentType = contentType;
        return this;
    }

    /// <inheritdoc />
    public IRouteEndpointBuilder WithHeader(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _route.ResponseHeaders[name] = value;
        return this;
    }
}
