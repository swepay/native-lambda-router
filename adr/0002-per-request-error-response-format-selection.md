# ADR-0002: Per-request error format via `SelectErrorResponseFormat(ErrorResponseFormatContext)`, not a per-route builder flag

**Data:** 2026-11-25
**Status:** proposed
**Repositórios afetados:** `native-lambda-router`, `native-guard-backend`

## Contexto

[swepay/native-guard-backend#153](https://github.com/swepay/native-guard-backend/issues/153)
activated `RoutedApiGatewayFunction.ErrorResponseFormat` (opt-in since 2.4.0) on Admin, OpenId
and OpenApi, moving their error responses to RFC 9457 `application/problem+json`. Identity was
deliberately left out: `ErrorResponseFormat` is read once per request inside `FunctionHandler`
and applies to the whole function, but Identity's OAuth 2.0/OIDC endpoints (`/token`,
`/revoke`, `/introspect`, …) must keep the RFC 6749/7009/7662 error body
(`IsOAuthBoundaryRoute`, native-guard-backend#149) while every other route in that same
function should be free to move to `ProblemDetails`. Neither `FunctionHandler` nor
`CreateRouteNotFoundResponse` is `virtual`, so native-guard-backend#161 could not isolate the
two without reimplementing routing — forbidden by the shared-kernel rule
(`ECOSYSTEM_LIBRARIES.md`).

The one case that most constrains the design is route-not-found (404): by definition there is
no matched `RouteDefinition` to carry per-route metadata on, yet the issue's acceptance
criteria require it to produce the right format too (`GET /identity/v1/nao-existe` on a
non-OAuth path).

## Alternativas consideradas

**A. `protected virtual ErrorResponseFormat SelectErrorResponseFormat(ErrorResponseFormatContext context)`
on `RoutedApiGatewayFunction`** (this ADR adopts this) — a second override point, symmetric
with the existing per-request virtual hooks on the same class (`ResolveTraceId(request,
context)`, `SerializeProblemDetails(problem)`). `ErrorResponseFormatContext` carries `Method`,
`Path`, and `Route` (`RouteDefinition?`, `null` for route-not-found). All four places the
router decides a format — `CreateRouteNotFoundResponse`, the authorization 401/403 branch, and
the exception-mapping branch (which also covers the 500 fallback) — call it. Default body is
`=> ErrorResponseFormat;`, i.e. delegates to the existing function-wide property.

- Pro: one override covers **all four** call sites, including route-not-found, because the
  context is built from the request (`Method`/`Path`) regardless of whether a route matched —
  it does not depend on `RouteDefinition` existing. A consumer like Identity writes exactly one
  method: `IsOAuthBoundaryRoute(context.Path) ? LegacyEnvelope : ProblemDetails`.
- Pro: zero behavior change for every existing consumer. The default implementation reads the
  already-existing `ErrorResponseFormat` property, so a function that only overrides that
  property (the 2.4.0 contract) is byte-for-byte unchanged; the property itself is untouched.
- Pro: matches the hierarchy's "simetria entre libs" criterion — `RoutedApiGatewayFunction`
  already has two per-request virtual hooks taking the request/response as context
  (`ResolveTraceId`, `SerializeProblemDetails`); this is the same shape for the format
  decision instead of a new kind of extension point.
- Con: adds one more virtual member to `RoutedApiGatewayFunction` (surface-area cost, but a
  `protected virtual` with a non-breaking default, same cost class as `ResolveTraceId` when it
  shipped in 2.4.0).
- Con: a function that wants "format by route" still writes its own `if`/`switch` over
  `context.Path` (or `context.Route`) rather than declaring it declaratively at the route —
  judged acceptable: the predicate is one line per boundary, and it is the only shape that also
  answers the 404 case.

**B. Per-route builder flag**: `IRouteEndpointBuilder.WithErrorFormat(ErrorResponseFormat)`,
backed by a new nullable `RouteDefinition.ErrorResponseFormat`; `FunctionHandler` would use the
route's value when set, falling back to the function-wide property otherwise (mirrors the
existing `Produces(string)` → `RouteDefinition.ResponseContentType` pair, and ADR-0001's
reasoning for `Accepts<TBody>()`).

- Pro: declarative — the format lives next to the route it applies to, same ergonomics as
  `.Produces("text/html")`.
- Con (decisive): **does not cover route-not-found by itself.** There is no `RouteDefinition`
  for a path that matches no route, so `CreateRouteNotFoundResponse` would still have nothing
  to read a per-route flag from — it is exactly the 404 case the issue is about. Adopting B
  alone would still require a second, predicate-based mechanism for 404 (something shaped like
  option A, just for one of the four call sites), which means consumers who need the 404 case
  right — every function that activates problem+json on *most but not all* of its surface, not
  just Identity's exact split — end up implementing two different extension points to cover
  one decision. That duplication, not the one-time cost of a new builder method, is why this
  option is rejected as the *only* mechanism.
- Con: per-route metadata only answers "which format for this route's own errors", but three of
  the four call sites that need a format (401/403 from `RequireRole`, and exception mapping)
  already run with a matched `RouteDefinition` in scope by the time they execute — so B is
  redundant with A exactly where A already works, and silent on the one case (404) where A is
  the only option. Shipping B on top of A would be two public surfaces deciding the same thing,
  which `swepay-solid`/the architect's "menor superfície pública" criterion flags directly.

## Decisão

Option A. `RoutedApiGatewayFunction.SelectErrorResponseFormat(ErrorResponseFormatContext)`
(native-lambda-router ≥ 2.6.0) is the only new public surface. `ErrorResponseFormat` (2.4.0+)
is unchanged and remains the simplest way to opt a whole function into `ProblemDetails`;
`SelectErrorResponseFormat` is the override point for a function that needs more than one
format, and its default delegates to `ErrorResponseFormat` so the two compose for free — a
function can override only the property (today's contract) or override the method for finer
control, never both inconsistently by accident. Non-breaking, minor version bump (2.5.1 →
2.6.0).

Option B is not ruled out structurally — `RouteDefinition.ErrorResponseFormat` could be added
later as a convenience that `SelectErrorResponseFormat`'s default body also consults
(`context.Route?.ErrorResponseFormat ?? ErrorResponseFormat`) — but is not needed today: no
open issue asks for route-level declarative syntax, and option A alone already closes
native-guard-backend#153.

## Consequências

Positivas:

- `native-guard-backend`'s Identity function can activate `ProblemDetails` on every non-OAuth
  route, including 404, without touching `IsOAuthBoundaryRoute`'s existing behavior or
  reimplementing routing.
- One mental model: "the router asks `SelectErrorResponseFormat` once per error, context tells
  you what you need to decide." No separate rule for 404 versus everything else.
- Every consumer already on 2.4.0–2.5.1 that only overrides `ErrorResponseFormat` needs zero
  code changes after upgrading.

Negativas:

- `RoutedApiGatewayFunction` gains a fourth virtual extension point overall (`ErrorResponseFormat`,
  `SelectErrorResponseFormat`, `ResolveTraceId`, `SerializeProblemDetails`); acceptable, each is
  independently optional and non-breaking.
- A consumer that wants route-level declarative syntax (option B's ergonomics) has to write the
  one-line predicate itself today; revisit if more than one consumer asks for it.

Neutras:

- `ErrorResponseFormatContext` is a new public sealed class with three `init`-only members; no
  consumer implements it, only reads it, so it can grow additional members later without
  breaking existing overrides (object-creation expressions with named members, not positional).
