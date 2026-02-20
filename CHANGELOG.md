# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.1.0] - 2026-02-20

### Added

- **Lambda Authorizer support (REQUEST type, payload format 2.0)**: `BuildRouteContext` now reads claims from `Authorizer.Lambda` (`Dictionary<string, object>`) when `Authorizer.Jwt.Claims` is null or empty. This enables support for API Gateway Lambda Authorizers with `EnableSimpleResponses: true`, which is required for multi-realm Identity Providers where the JWT issuer varies per realm.
- Priority order: JWT Authorizer claims are preferred when both are present.
- Lambda context values (`object`) are converted to `string` via `.ToString()` — safe for simple response format (strings, numbers, booleans).
- Null values in the Lambda context are skipped.
- All existing authorization features (`RequireRole`, `RequireClaim`, `RequireAssertion`, policies) work transparently with both authorizer types.

### No Breaking Changes

- `RouteContext.Claims` remains `Dictionary<string, string>`.
- All existing JWT Authorizer behavior is unchanged.
- No new public API surface — the fallback is automatic and internal.

## [2.0.3] - 2026-02-17

### Fixed

- **Path parameter case preservation**: Path parameter values now preserve their original casing from the request URL. Previously, values were lowercased during path normalization (e.g., `/realms/Master` would yield `realmId = "master"` instead of `"Master"`).
- **Health check path matching**: `IsHealthCheckPath` now uses case-insensitive comparison instead of relying on pre-lowercased input.

### Changed

- Replaced FluentAssertions with Shouldly in all test files.

## [2.0.0] - 2026-01-24

### ⚠️ Breaking Changes

This is a major release with several breaking changes to improve Native AOT compatibility and add authorization support.

#### Constructor Changes

**Before (v1.x):**
```csharp
public Function(IMediator mediator)
    : base(mediator, JsonSerializerContext.Default.Options)
{
}
```

**After (v2.0.0):**
```csharp
// Option 1: Singleton mediator
public Function(IMediator mediator)
    : base(mediator)
{
}

// Option 2: Scoped mediator (new)
public Function(IServiceProvider serviceProvider)
    : base(serviceProvider)
{
}
```

The `JsonSerializerOptions` parameter has been removed from the constructor. JSON serialization is now handled entirely through the abstract `SerializeResponse` method.

#### SerializeResponse is now Abstract

**Before (v1.x):**
```csharp
// Optional override with reflection-based fallback
protected override string SerializeResponse(object response)
{
    return response switch
    {
        MyResponse r => JsonSerializer.Serialize(r, MyContext.Default.MyResponse),
        _ => JsonSerializer.Serialize(response) // Reflection fallback (not AOT-safe)
    };
}
```

**After (v2.0.0):**
```csharp
// Required implementation - no reflection fallback
protected override string SerializeResponse(object response)
{
    return response switch
    {
        MyResponse r => JsonSerializer.Serialize(r, AppJsonContext.Default.MyResponse),
        // Router internal types (required)
        ErrorResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.ErrorResponse),
        HealthCheckResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.HealthCheckResponse),
        RouteNotFoundResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.RouteNotFoundResponse),
        _ => throw new NotSupportedException($"No serializer for {response.GetType().Name}")
    };
}
```

#### ExecuteCommandAsync Signature Changed

**Before (v1.x):**
```csharp
protected override async Task<object> ExecuteCommandAsync(
    RouteMatch match, 
    RouteContext context)
{
    var command = match.Route.CommandFactory(context);
    return command switch
    {
        GetItemsCommand cmd => await Mediator.Send(cmd),
        // ...
    };
}
```

**After (v2.0.0):**
```csharp
protected override async Task<object> ExecuteCommandAsync(
    RouteMatch match, 
    RouteContext context,
    IMediator mediator)  // <-- New parameter
{
    var command = match.Route.CommandFactory(context);
    return command switch
    {
        GetItemsCommand cmd => await mediator.Send(cmd),  // Use parameter
        // ...
    };
}
```

The mediator is now passed as a parameter to ensure proper scoping when using `IServiceProvider` constructor.

#### Route Methods Return IRouteEndpointBuilder

**Before (v1.x):**
```csharp
// Returned IRouteBuilder for chaining multiple routes
routes.MapGet<Cmd1, Res1>("/path1", ctx => new Cmd1())
      .MapPost<Cmd2, Res2>("/path2", ctx => new Cmd2());
```

**After (v2.0.0):**
```csharp
// Returns IRouteEndpointBuilder for authorization configuration
routes.MapGet<Cmd1, Res1>("/path1", ctx => new Cmd1())
      .RequireAuthorization("policy");

routes.MapPost<Cmd2, Res2>("/path2", ctx => new Cmd2())
      .RequireRole("admin");
```

#### RequiresAuth Property Deprecated

The `RouteDefinition.RequiresAuth` property is deprecated. Use `AuthorizationOptions` instead:

```csharp
// Deprecated
route.RequiresAuth = true;

// Use instead
routes.MapGet<Cmd, Res>("/path", ctx => new Cmd())
      .RequireAuthorization();
```

---

### Added

#### Fluent Authorization API

New fluent authorization system inspired by ASP.NET Core Minimal APIs:

```csharp
// Define policies
protected override void ConfigureAuthorization(AuthorizationBuilder auth)
{
    auth.AddPolicy("admin_api", policy => policy
        .RequireRole("admin")
        .RequireClaim("scope", "api:read", "api:write"));
    
    auth.AddPolicy("premium_users", policy => policy
        .RequireClaim("subscription", "premium", "enterprise")
        .RequireAssertion(ctx => ctx.Headers.ContainsKey("X-Api-Key")));
}

// Apply to routes
protected override void ConfigureRoutes(IRouteBuilder routes)
{
    routes.MapGet<GetItemsCommand, GetItemsResponse>("/items", ctx => new GetItemsCommand())
          .RequireAuthorization("admin_api");
    
    routes.MapDelete<DeleteCommand, DeleteResponse>("/items/{id}", ctx => new DeleteCommand(ctx.PathParameters["id"]))
          .RequireRole("admin", "moderator");
    
    routes.MapGet<PublicDataCommand, PublicDataResponse>("/public", ctx => new PublicDataCommand())
          .AllowAnonymous();
}
```

**New Authorization Methods:**

| Method | Description |
|--------|-------------|
| `.RequireAuthorization(policies...)` | Requires authentication and optionally specific policies |
| `.RequireRole(roles...)` | Requires at least one of the specified roles |
| `.RequireClaim(type, values...)` | Requires a claim with specific values |
| `.AllowAnonymous()` | Bypasses all authorization |

**Policy Builder Methods:**

| Method | Description |
|--------|-------------|
| `.RequireRole(roles...)` | User must have at least one role |
| `.RequireClaim(type)` | User must have this claim (any value) |
| `.RequireClaim(type, values...)` | User must have claim with specific value |
| `.RequireAssertion(func)` | Custom authorization logic |

**Supported Role Claim Types:**
- `role`, `roles`
- `cognito:groups` (AWS Cognito)
- `groups`

#### Scoped Mediator Support

New constructor for scoped dependency injection:

```csharp
public class Function : RoutedApiGatewayFunction
{
    public Function(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
    }
}
```

Helper methods:
- `UsesScopedMediator` - Check if using scoped resolution
- `CreateScopedMediator()` - Create scope and get mediator

#### AOT-Compatible Response Types

New strongly-typed response classes with source-generated JSON context:

| Type | Description |
|------|-------------|
| `ErrorResponse` | Standard error (400, 401, 403, 404, 409, 500) |
| `HealthCheckResponse` | Health check endpoint |
| `RouteNotFoundResponse` | Route not found (404) |
| `RouterJsonContext` | Pre-configured JSON serialization |

```csharp
// Use RouterJsonContext for internal types
JsonSerializer.Serialize(error, RouterJsonContext.Default.ErrorResponse);
JsonSerializer.Serialize(health, RouterJsonContext.Default.HealthCheckResponse);
JsonSerializer.Serialize(notFound, RouterJsonContext.Default.RouteNotFoundResponse);
```

#### New Classes

- `AuthorizationPolicy` - Represents a named authorization policy
- `AuthorizationPolicyBuilder` - Fluent builder for policies
- `AuthorizationBuilder` - Configures policies for the function
- `AuthorizationService` - Validates authorization requirements
- `AuthorizationResult` - Result of authorization check
- `RouteAuthorizationOptions` - Per-route authorization settings
- `IRouteEndpointBuilder` - Fluent endpoint configuration
- `RouteEndpointBuilder` - Implementation

---

### Changed

- `GetHealthCheckResponse()` now returns `HealthCheckResponse` instead of `object`
- All anonymous type responses replaced with concrete types
- Removed `System.Text.Json` import from base class (no longer uses reflection)
- Added `Microsoft.Extensions.DependencyInjection.Abstractions` dependency

---

### Removed

- `JsonSerializerOptions` constructor parameter
- Default implementation of `SerializeResponse` (now abstract)
- Reflection-based JSON serialization fallback

---

### Fixed

- **IL2026**: No longer uses `JsonSerializer.Serialize(Object, Type, JsonSerializerOptions)`
- **IL3050**: No longer requires runtime code generation for JSON

---

## Migration Guide

### Step 1: Update Constructor

```csharp
// Before
public Function(IMediator mediator)
    : base(mediator, JsonSerializerContext.Default.Options)
{ }

// After
public Function(IMediator mediator)
    : base(mediator)
{ }
```

### Step 2: Implement SerializeResponse

```csharp
protected override string SerializeResponse(object response)
{
    return response switch
    {
        // Your types
        GetItemsResponse r => JsonSerializer.Serialize(r, AppJsonContext.Default.GetItemsResponse),
        CreateItemResponse r => JsonSerializer.Serialize(r, AppJsonContext.Default.CreateItemResponse),
        
        // Router types (always include)
        ErrorResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.ErrorResponse),
        HealthCheckResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.HealthCheckResponse),
        RouteNotFoundResponse r => JsonSerializer.Serialize(r, RouterJsonContext.Default.RouteNotFoundResponse),
        
        _ => throw new NotSupportedException($"No serializer for {response.GetType().Name}")
    };
}
```

### Step 3: Update ExecuteCommandAsync

```csharp
// Before
protected override async Task<object> ExecuteCommandAsync(
    RouteMatch match, RouteContext context)
{
    return command switch
    {
        GetItemsCommand cmd => await Mediator.Send(cmd),
        // ...
    };
}

// After
protected override async Task<object> ExecuteCommandAsync(
    RouteMatch match, RouteContext context, IMediator mediator)
{
    return command switch
    {
        GetItemsCommand cmd => await mediator.Send(cmd),  // Use parameter
        // ...
    };
}
```

### Step 4: Update Route Chaining (if applicable)

```csharp
// Before (chained routes)
routes.MapGet<C1, R1>("/a", ctx => new C1())
      .MapGet<C2, R2>("/b", ctx => new C2());

// After (separate statements)
routes.MapGet<C1, R1>("/a", ctx => new C1());
routes.MapGet<C2, R2>("/b", ctx => new C2());

// Or with authorization
routes.MapGet<C1, R1>("/a", ctx => new C1()).RequireAuthorization();
routes.MapGet<C2, R2>("/b", ctx => new C2()).AllowAnonymous();
```

### Step 5: Add Authorization (Optional)

```csharp
protected override void ConfigureAuthorization(AuthorizationBuilder auth)
{
    auth.AddPolicy("admin", policy => policy.RequireRole("admin"));
}

protected override void ConfigureRoutes(IRouteBuilder routes)
{
    routes.MapDelete<DeleteCmd, DeleteRes>("/items/{id}", ctx => new DeleteCmd(ctx.PathParameters["id"]))
          .RequireAuthorization("admin");
}
```

---

## [1.0.3] - 2026-01-20

### Fixed
- Minor bug fixes

## [1.0.2] - 2026-01-15

### Changed
- Documentation improvements

## [1.0.1] - 2026-01-10

### Fixed
- Package metadata updates

## [1.0.0] - 2026-01-05

### Added
- Initial release
- Declarative route mapping
- Path parameter extraction
- Health check endpoints
- Error handling with exceptions
- JWT claims access
- NativeMediator integration
