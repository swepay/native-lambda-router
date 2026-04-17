# native-lambda-router

**Versão:** v2.1.0  
**Tipo:** NuGet Library - Roteamento HTTP para AWS Lambda  
**AOT-Safe:** Sim  
**Linguagem:** C# 12+

## O que é

`native-lambda-router` é uma biblioteca de roteamento HTTP otimizada para AWS Lambda com suporte a AOT (Ahead-of-Time) compilation. Elimina reflection e permite definir rotas de forma fluente e type-safe.

## API Pública Principal

### RoutedApiGatewayFunction
Classe base para sua função Lambda. Herda para definir rotas.

```csharp
public abstract partial class RoutedApiGatewayFunction
{
    public RoutedApiGatewayFunction() { }
    public abstract void ConfigureRoutes(IRouteBuilder builder);
    public virtual async Task<HttpResponse> FunctionHandler(HttpRequest request, ILambdaContext context);
}
```

### IRouteBuilder
Interface fluente para registrar rotas.

```csharp
public interface IRouteBuilder
{
    IRouteBuilder MapGet(string path, RouteHandler handler);
    IRouteBuilder MapPost(string path, RouteHandler handler);
    IRouteBuilder MapPut(string path, RouteHandler handler);
    IRouteBuilder MapDelete(string path, RouteHandler handler);
    IRouteBuilder MapPatch(string path, RouteHandler handler);
    IRouteBuilder WithAuthorization(AuthorizationPolicy policy);
}
```

### RouteContext
Contexto da requisição com acesso a parâmetros, query strings, headers, body.

```csharp
public class RouteContext
{
    public string Path { get; }
    public string Method { get; }
    public IReadOnlyDictionary<string, string> PathParameters { get; }
    public IReadOnlyDictionary<string, StringValues> QueryString { get; }
    public IHeaderDictionary Headers { get; }
    public Stream Body { get; }
    public ILambdaContext LambdaContext { get; }
}
```

### AuthorizationService / AuthorizationPolicy / PolicyBuilder
Sistema fluente de autorização.

```csharp
public class AuthorizationPolicy
{
    public bool Allow { get; set; }
    public List<string> RequiredClaims { get; set; }
    public Func<RouteContext, Task<bool>>? CustomAuthorizer { get; set; }
}

public class PolicyBuilder
{
    public PolicyBuilder RequireClaim(string claimType);
    public PolicyBuilder RequireRole(string role);
    public PolicyBuilder RequireUser();
    public PolicyBuilder Custom(Func<RouteContext, Task<bool>> authorizer);
    public AuthorizationPolicy Build();
}
```

## Como Implementar

### 1. Criar JsonSerializerContext (OBRIGATÓRIO)
```csharp
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase)]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
    public AppJsonSerializerContext() : base(null) { }
}
```

### 2. Herdar de RoutedApiGatewayFunction e Definir Rotas
```csharp
public partial class MyFunction : RoutedApiGatewayFunction
{
    public override void ConfigureRoutes(IRouteBuilder builder)
    {
        builder
            .MapGet("/users/{id}", HandleGetUser)
            .MapPost("/users", HandleCreateUser)
            .MapDelete("/users/{id}", HandleDeleteUser)
            .WithAuthorization(new PolicyBuilder()
                .RequireClaim("sub")
                .Build());
    }

    private async Task<HttpResponse> HandleGetUser(RouteContext context)
    {
        var userId = context.PathParameters["id"];
        var user = new { id = userId, name = "John Doe" };
        return HttpResponse.Ok(user);
    }

    private async Task<HttpResponse> HandleCreateUser(RouteContext context)
    {
        var body = await context.Body.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<CreateUserRequest>(body, 
            AppJsonSerializerContext.Default.CreateUserRequest);
        return HttpResponse.Created(user);
    }

    private async Task<HttpResponse> HandleDeleteUser(RouteContext context)
    {
        return HttpResponse.NoContent();
    }
}
```

### 3. Publicar no Lambda
```bash
dotnet publish -c Release -o ./bin/publish
cd bin/publish
zip -r function.zip .
aws lambda update-function-code --function-name MyFunction --zip-file fileb://function.zip
```

## Premissas

- **Build sem warnings:** `dotnet build` deve resultar em 0 warnings
- **Testes passando:** `dotnet test` deve passar 100%
- **Código formatado:** `dotnet format --verify-no-changes`
- **Namespace:** `Native.LambdaRouter`
- **Target:** `net8.0`; PublishAot = true
- **JsonSerializerContext é OBRIGATÓRIO** - Sem ele, reflection ocorre no runtime

## Terminologia

- **Route Handler:** Delegado `Task<HttpResponse>` que processa requisição
- **Path Parameter:** Capturado de {param} no path da rota
- **QueryString:** Argumentos URL (`?key=value`)
- **Authorization Policy:** Define permissões usando claims JWT
- **Content-Type:** Inferido do response ou customizável via `.WithContentType()`

## Limitações & Notas

- Não suporta streaming de response (Lambda retorna completo)
- Path parameters são case-sensitive
- Authorization requer JWT válido no header `Authorization: Bearer <token>`
- Content-Type padrão é `application/json`
