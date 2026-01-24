namespace NativeLambdaRouter;

/// <summary>
/// Represents an authorization policy with requirements.
/// </summary>
public sealed class AuthorizationPolicy
{
    /// <summary>
    /// The name of the policy.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Required roles (user must have at least one).
    /// </summary>
    public IReadOnlyList<string> RequiredRoles { get; }

    /// <summary>
    /// Required claims (all must be present with matching values).
    /// </summary>
    public IReadOnlyDictionary<string, string[]> RequiredClaims { get; }

    /// <summary>
    /// Custom authorization requirements.
    /// </summary>
    public IReadOnlyList<Func<RouteContext, bool>> Requirements { get; }

    internal AuthorizationPolicy(
        string name,
        IReadOnlyList<string> requiredRoles,
        IReadOnlyDictionary<string, string[]> requiredClaims,
        IReadOnlyList<Func<RouteContext, bool>> requirements)
    {
        Name = name;
        RequiredRoles = requiredRoles;
        RequiredClaims = requiredClaims;
        Requirements = requirements;
    }
}

/// <summary>
/// Builder for creating authorization policies.
/// </summary>
public sealed class AuthorizationPolicyBuilder
{
    private readonly string _name;
    private readonly List<string> _roles = [];
    private readonly Dictionary<string, List<string>> _claims = [];
    private readonly List<Func<RouteContext, bool>> _requirements = [];

    /// <summary>
    /// Creates a new policy builder with the specified name.
    /// </summary>
    public AuthorizationPolicyBuilder(string name)
    {
        _name = name;
    }

    /// <summary>
    /// Requires the user to have at least one of the specified roles.
    /// </summary>
    public AuthorizationPolicyBuilder RequireRole(params string[] roles)
    {
        _roles.AddRange(roles);
        return this;
    }

    /// <summary>
    /// Requires the user to have a claim with the specified type.
    /// </summary>
    public AuthorizationPolicyBuilder RequireClaim(string claimType)
    {
        if (!_claims.ContainsKey(claimType))
        {
            _claims[claimType] = [];
        }
        return this;
    }

    /// <summary>
    /// Requires the user to have a claim with one of the specified values.
    /// </summary>
    public AuthorizationPolicyBuilder RequireClaim(string claimType, params string[] allowedValues)
    {
        if (!_claims.TryGetValue(claimType, out var values))
        {
            values = [];
            _claims[claimType] = values;
        }
        values.AddRange(allowedValues);
        return this;
    }

    /// <summary>
    /// Adds a custom requirement function.
    /// </summary>
    public AuthorizationPolicyBuilder RequireAssertion(Func<RouteContext, bool> requirement)
    {
        _requirements.Add(requirement);
        return this;
    }

    /// <summary>
    /// Builds the authorization policy.
    /// </summary>
    public AuthorizationPolicy Build()
    {
        return new AuthorizationPolicy(
            _name,
            _roles.AsReadOnly(),
            _claims.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToArray()),
            _requirements.AsReadOnly());
    }
}

/// <summary>
/// Builder for configuring authorization policies.
/// </summary>
public sealed class AuthorizationBuilder
{
    private readonly Dictionary<string, AuthorizationPolicy> _policies = [];

    /// <summary>
    /// Adds a named authorization policy.
    /// </summary>
    public AuthorizationBuilder AddPolicy(string name, Action<AuthorizationPolicyBuilder> configure)
    {
        var builder = new AuthorizationPolicyBuilder(name);
        configure(builder);
        _policies[name] = builder.Build();
        return this;
    }

    /// <summary>
    /// Gets all configured policies.
    /// </summary>
    public IReadOnlyDictionary<string, AuthorizationPolicy> Policies => _policies;
}

/// <summary>
/// Options for route authorization.
/// </summary>
public sealed class RouteAuthorizationOptions
{
    /// <summary>
    /// Named policy requirements.
    /// </summary>
    public List<string> PolicyNames { get; } = [];

    /// <summary>
    /// Inline role requirements.
    /// </summary>
    public List<string> Roles { get; } = [];

    /// <summary>
    /// Inline claim requirements (claim type -> allowed values).
    /// </summary>
    public Dictionary<string, string[]> Claims { get; } = [];

    /// <summary>
    /// Whether the route allows anonymous access.
    /// </summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    /// Whether the route requires any authenticated user.
    /// </summary>
    public bool RequiresAuthentication { get; set; }
}

/// <summary>
/// Service for validating authorization requirements.
/// </summary>
public sealed class AuthorizationService
{
    private readonly IReadOnlyDictionary<string, AuthorizationPolicy> _policies;

    /// <summary>
    /// Creates a new authorization service with the specified policies.
    /// </summary>
    public AuthorizationService(IReadOnlyDictionary<string, AuthorizationPolicy>? policies = null)
    {
        _policies = policies ?? new Dictionary<string, AuthorizationPolicy>();
    }

    /// <summary>
    /// Validates the authorization requirements for a route.
    /// </summary>
    /// <param name="context">The route context with user claims.</param>
    /// <param name="options">The authorization options for the route.</param>
    /// <returns>An authorization result indicating success or failure.</returns>
    public AuthorizationResult Authorize(RouteContext context, RouteAuthorizationOptions options)
    {
        // Allow anonymous routes
        if (options.AllowAnonymous)
        {
            return AuthorizationResult.Success();
        }

        // Check if authentication is required
        if (options.RequiresAuthentication || options.PolicyNames.Count > 0 ||
            options.Roles.Count > 0 || options.Claims.Count > 0)
        {
            // Check if user is authenticated (has any claims)
            if (context.Claims.Count == 0)
            {
                return AuthorizationResult.Fail("User is not authenticated.");
            }
        }

        // Validate inline roles
        if (options.Roles.Count > 0)
        {
            if (!HasAnyRole(context, options.Roles))
            {
                return AuthorizationResult.Fail($"User does not have any of the required roles: {string.Join(", ", options.Roles)}");
            }
        }

        // Validate inline claims
        foreach (var claim in options.Claims)
        {
            if (!HasClaim(context, claim.Key, claim.Value))
            {
                return AuthorizationResult.Fail($"User does not have the required claim '{claim.Key}'.");
            }
        }

        // Validate named policies
        foreach (var policyName in options.PolicyNames)
        {
            if (!_policies.TryGetValue(policyName, out var policy))
            {
                return AuthorizationResult.Fail($"Authorization policy '{policyName}' not found.");
            }

            var policyResult = ValidatePolicy(context, policy);
            if (!policyResult.Succeeded)
            {
                return policyResult;
            }
        }

        return AuthorizationResult.Success();
    }

    private AuthorizationResult ValidatePolicy(RouteContext context, AuthorizationPolicy policy)
    {
        // Check required roles
        if (policy.RequiredRoles.Count > 0)
        {
            if (!HasAnyRole(context, policy.RequiredRoles))
            {
                return AuthorizationResult.Fail($"Policy '{policy.Name}' requires one of these roles: {string.Join(", ", policy.RequiredRoles)}");
            }
        }

        // Check required claims
        foreach (var claim in policy.RequiredClaims)
        {
            if (!HasClaim(context, claim.Key, claim.Value))
            {
                return AuthorizationResult.Fail($"Policy '{policy.Name}' requires claim '{claim.Key}'.");
            }
        }

        // Check custom requirements
        foreach (var requirement in policy.Requirements)
        {
            if (!requirement(context))
            {
                return AuthorizationResult.Fail($"Policy '{policy.Name}' custom requirement failed.");
            }
        }

        return AuthorizationResult.Success();
    }

    private static bool HasAnyRole(RouteContext context, IEnumerable<string> roles)
    {
        // Check common role claim types
        var roleClaims = new[] { "role", "roles", "cognito:groups", "groups" };

        foreach (var claimType in roleClaims)
        {
            if (context.Claims.TryGetValue(claimType, out var userRoles))
            {
                // Roles might be comma-separated or JSON array
                var userRoleList = ParseRoles(userRoles);
                if (roles.Any(r => userRoleList.Contains(r, StringComparer.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasClaim(RouteContext context, string claimType, string[] allowedValues)
    {
        if (!context.Claims.TryGetValue(claimType, out var claimValue))
        {
            return false;
        }

        // If no specific values required, just check presence
        if (allowedValues.Length == 0)
        {
            return true;
        }

        // Parse the claim value (might be space-separated like scopes)
        var claimValues = claimValue.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        return allowedValues.Any(v => claimValues.Contains(v, StringComparer.OrdinalIgnoreCase));
    }

    private static List<string> ParseRoles(string roles)
    {
        // Handle JSON array format
        if (roles.StartsWith('['))
        {
            roles = roles.Trim('[', ']', '"', ' ');
        }

        return [.. roles.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim('"', ' '))];
    }
}

/// <summary>
/// Result of an authorization check.
/// </summary>
public sealed class AuthorizationResult
{
    /// <summary>
    /// Whether authorization succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Failure message if authorization failed.
    /// </summary>
    public string? FailureMessage { get; }

    private AuthorizationResult(bool succeeded, string? failureMessage = null)
    {
        Succeeded = succeeded;
        FailureMessage = failureMessage;
    }

    /// <summary>
    /// Creates a successful authorization result.
    /// </summary>
    public static AuthorizationResult Success() => new(true);

    /// <summary>
    /// Creates a failed authorization result with the specified message.
    /// </summary>
    public static AuthorizationResult Fail(string message) => new(false, message);
}
