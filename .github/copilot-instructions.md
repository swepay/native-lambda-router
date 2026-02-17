# Copilot Instructions — NativeLambdaRouter

## Project Overview

NuGet library providing declarative HTTP routing for AWS Lambda functions behind API Gateway (HTTP API v2). Designed for **.NET 10 Native AOT** — all code must be trim-safe and avoid reflection.

**Key dependency chain:** `APIGatewayHttpApiV2ProxyRequest` → `RoutedApiGatewayFunction` → `RouteMatcher` → `RouteDefinition` → `NativeMediator` (CQRS mediator).

## Architecture

- **`RoutedApiGatewayFunction`** — abstract base class consumers inherit. Owns the request lifecycle: route matching → authorization → command execution → serialization. Two constructor paths: singleton `IMediator` or scoped `IServiceProvider`.
- **`RouteBuilder` / `IRouteBuilder`** — fluent API for mapping HTTP method + path template to a `Func<RouteContext, TCommand>` factory. Returns `IRouteEndpointBuilder` for chaining `.RequireAuthorization()`, `.Produces()`, `.AllowAnonymous()`, etc.
- **`RouteMatcher`** — compiles path templates (e.g. `/items/{id}`) into regex at construction time; extracts path parameters into `RouteMatch.PathParameters`.
- **`Authorization*`** — policy-based auth evaluated against JWT claims from `RequestContext.Authorizer.Jwt.Claims`. Policies are registered in `ConfigureAuthorization()`.
- **`Exceptions.cs`** — domain exceptions (`ValidationException`, `NotFoundException`, `UnauthorizedException`, `ForbiddenException`, `ConflictException`) auto-mapped to HTTP status codes in `FunctionHandler`.
- **`Responses.cs`** — internal response DTOs (`ErrorResponse`, `HealthCheckResponse`, `RouteNotFoundResponse`, `ApiGatewayResponse`) plus `RouterJsonContext` for AOT serialization.

## Native AOT Constraints (Critical)

- **No reflection-based serialization.** All JSON must use source-generated `JsonSerializerContext`. The library ships `RouterJsonContext`; consumers must create their own for app types.
- `SerializeResponse()` is **abstract** — every response type (including router internals) must be explicitly handled via pattern matching. No fallback to `JsonSerializer.Serialize(obj)`.
- The `.csproj` has `<IsAotCompatible>true</IsAotCompatible>`, `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`, and `<IsTrimmable>true</IsTrimmable>`. Any new code must pass trim analysis.

## Build & Test

```powershell
dotnet build                                          # Debug build
dotnet build --configuration Release                  # Release build
dotnet test                                           # Run all xUnit tests
dotnet pack src/NativeLambdaRouter -c Release -o nupkg # Create NuGet package
```

Solution file: `NativeLambdaRouter.slnx`. SDK pinned to `10.0.x` via `global.json` (`rollForward: latestMinor`).

## Testing Conventions

- **xUnit** with **FluentAssertions** (`.Should()`) and **NSubstitute** (`Substitute.For<T>()`).
- Global usings for `Xunit`, `FluentAssertions`, `NSubstitute` are declared in the test `.csproj`.
- Tests create concrete subclasses of `RoutedApiGatewayFunction` (see `TestFunction` in `RoutedApiGatewayFunctionTests.cs`) to test the full pipeline.
- Test commands use `record TestCommand(string Value)` — simple records, not full mediator commands.
- Arrange/Act/Assert pattern with `// Arrange`, `// Act`, `// Assert` comments.

## Code Conventions

- **File-scoped namespaces** and **nullable enabled** throughout.
- Collection expressions (`[]` syntax) preferred over `new List<T>()`.
- XML doc comments (`<summary>`, `<example>`) on all public API members.
- `sealed` by default on implementation classes; interfaces for extensibility points (`IRouteBuilder`, `IRouteEndpointBuilder`).
- Path normalization: lowercase, leading `/`, no trailing `/`. Applied in both `RouteBuilder` and `RouteMatcher`.
- The `HttpMethod` class uses `const string` fields (not `System.Net.Http.HttpMethod`).

## Versioning & Release

- [Semantic Versioning](https://semver.org/). Version set in `NativeLambdaRouter.csproj` `<Version>` property.
- `CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com/) format.
- CI publishes to NuGet on version tags (`v*`). GitHub Actions workflow: `.github/workflows/dotnet.yml`.

## Adding a New Feature Checklist

1. Add/modify types in `src/NativeLambdaRouter/`.
2. Ensure AOT compatibility — no reflection, use `[JsonSerializable]` if adding response types.
3. Add tests in `tests/NativeLambdaRouter.Tests/` following existing patterns.
4. Update `CHANGELOG.md` under an `[Unreleased]` section.
5. If adding a new exception type, add the catch block in `FunctionHandler` with the appropriate HTTP status code.
