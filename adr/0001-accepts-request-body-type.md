# ADR-0001: `Accepts<TBody>()` lives on `IRouteEndpointBuilder`, not as a syntactic OpenAPI marker

**Data:** 2026-11-24
**Status:** proposed
**Repositórios afetados:** `native-lambda-router`, `native-open-api`

## Contexto

[swepay/native-guard-backend#97](https://github.com/swepay/native-guard-backend/issues/97)
reports that `NativeLambdaRouter.SourceGenerator.OpenApi` emits `requestBody` using the
route's `TCommand` schema, even on routes where the command factory builds `TCommand` from a
mix of path parameters, headers, claims, and the JSON body (e.g.
`EnsureExternalIdpUserCommand` carries `realmId`/`assertionToken`/`callerClientId`/
`callerIpAddress`, all server-derived, plus a couple of fields that come from the body). The
generator marks those server-derived fields `required` in the emitted schema, so a generated
SDK client is told to send caller-identity values in the JSON body that the server actually
takes from path/header/claims and never reads from the body.

The issue's own "Fix paths" section proposes Option A: give the route builder an API to
declare the real body type separate from the command, and have the generator read it. Two
shapes were considered for that API.

## Alternativas consideradas

**A. Purely syntactic marker in `native-open-api`** (mirrors the existing
`OpenApiRouteExtensions.ExcludeFromDocs<TBuilder>()`): a generic extension method that is an
identity pass-through at runtime, recognized by the generator purely by walking the Roslyn
syntax tree for a chained call named `Accepts` with one type argument. No change to
`native-lambda-router` at all.

- Pro: zero new surface on the router package; keeps `Native.OpenApi` free of a project
  reference to `NativeLambdaRouter` (the stated reason `ExcludeFromDocs` lives there).
- Con: "what type does this route actually deserialize from the body" is routing/binding
  metadata, not a documentation cosmetic like a tag or an operation summary. Nothing on
  `RouteDefinition` would carry it, so nothing other than the OpenAPI generator could ever
  observe it (e.g. a future request-validation middleware, a different doc generator, a
  test helper asserting route contracts). It would also be the one fluent-chain method whose
  only effect is a type argument with no runtime echo anywhere in the router's own model —
  every other piece of per-route state the router actually uses (`ResponseType`,
  `ResponseContentType`, `ResponseHeaders`) lives on `RouteDefinition`.

**B. Real member on `IRouteEndpointBuilder`, backed by `RouteDefinition.RequestBodyType`**
(the option this ADR adopts): `Accepts<TBody>()` is declared on `NativeLambdaRouter`'s own
`IRouteEndpointBuilder`, implemented by `RouteEndpointBuilder`, and stores `typeof(TBody)` on
`RouteDefinition`. The OpenAPI generator reads that property through the same reflection path
it already uses to read `ResponseType`.

- Pro: symmetric with the existing `Produces(string contentType)` →
  `RouteDefinition.ResponseContentType` pair — "declare metadata about this route" already
  has a home on `IRouteEndpointBuilder`/`RouteDefinition`, `Accepts<TBody>()` is the request
  side of the same shape. Any future consumer of route metadata gets `RequestBodyType` for
  free, not just the OpenAPI generator. One real compiled method also means a consumer like
  `native-guard-backend` does not need to hand-roll a local no-op extension for a *generic*
  method — the generic-marker pattern used for `WithName`/`WithSummary`/etc. only works
  because those are non-generic string/int overloads; a generic no-op extension with the
  exact right constraint shape is more to get right per consumer repo than a single
  authoritative implementation in the router itself.
- Con: adds one more public member to `IRouteEndpointBuilder` (surface-area cost); a route
  that calls `.Accepts<TBody>()` with a `TBody` unrelated to what the command factory
  actually deserializes is a self-inflicted lie the compiler cannot catch — same trust model
  as `Produces(string contentType)` already has for the response side.

## Decisão

Option B. `IRouteEndpointBuilder.Accepts<TBody>()` (native-lambda-router ≥ 2.5.0) sets
`RouteDefinition.RequestBodyType = typeof(TBody)`. `NativeLambdaRouter.SourceGenerator.OpenApi`
(native-open-api ≥ 1.9.0) emits `requestBody` from `TBody`'s schema (registering it in
`components/schemas`) when `RequestBodyType` is set, and keeps emitting the command's schema
unchanged when it is not — non-breaking, minor version bump in both packages.

## Consequências

Positivas:

- One authoritative implementation; consumers (native-guard-backend and future services) do
  not need a local extension for this one.
- `RequestBodyType` is real route metadata, available to any future consumer, not generator-specific.
- Non-breaking: routes that never call `Accepts<TBody>()` are byte-for-byte unchanged in both
  packages.

Negativas:

- `IRouteEndpointBuilder` gains a member every implementer of the interface must now provide
  (acceptable here: `RouteEndpointBuilder` is the only implementation in this repo, and the
  interface is not re-implemented downstream — consumers call it, they don't implement it).

Neutras:

- `.Accepts(string contentType)` (content-type only, already shipped as a per-consumer local
  extension in repos like `native-guard-backend`) is unrelated and unaffected; a route may use
  both `.Accepts<TBody>()` and a content-type `.Accepts(...)` together.
