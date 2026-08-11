---
name: developer
archetype: support-library
model: claude-sonnet-5
tools: [Read, Write, Edit, Bash, Grep, Glob]
description: >
  Implemente features seguindo o shared kernel e as convenções de código.
---

# Developer Agent - native-lambda-router

**Modelo:** Claude Sonnet 4  
**Ferramentas:** read, write, bash, edit, grep, glob  
**Foco:** Implementar novas rotas, handlers, e políticas de autorização

## Responsabilidades

1. **Implementar novas rotas** no método `ConfigureRoutes()`
2. **Criar handlers type-safe** com `RouteContext` e `HttpResponse`
3. **Configurar autorização** usando `PolicyBuilder` fluente
4. **Customizar content-type** para respostas
5. **Garantir compilação AOT-safe** (sem reflection)

## Fluxo de Trabalho

### Adicionar Nova Rota

1. **Abrir CLAUDE.md** para rever exemplos
2. **Listar handlers existentes** (`grep -n "HandleGet\|HandlePost" src/`)
3. **Criar novo handler** como método privado em `RoutedApiGatewayFunction`
4. **Registrar rota** em `ConfigureRoutes()` com `builder.MapGet/Post/Put/Delete()`
5. **Registrar tipo** em `JsonSerializerContext` se usar request/response customizados
6. **Executar testes:**
   ```bash
   dotnet test
   dotnet format --verify-no-changes
   ```

### Estrutura do Handler

```csharp
private async Task<HttpResponse> HandleGetResource(RouteContext context)
{
    // 1. Extrair parâmetros
    var resourceId = context.PathParameters["id"];
    var pageSize = context.QueryString["pageSize"].FirstOrDefault() ?? "10";
    
    // 2. Extrair JWT claims (se autorizado)
    var userId = context.ExtractClaim("sub");
    
    // 3. Validar entrada
    if (string.IsNullOrEmpty(resourceId))
        return HttpResponse.BadRequest(new { error = "id is required" });
    
    // 4. Processar lógica
    var resource = await _service.GetResourceAsync(resourceId);
    
    // 5. Retornar resposta
    return resource == null 
        ? HttpResponse.NotFound() 
        : HttpResponse.Ok(resource);
}
```

### Criar JsonSerializerContext para Novo Tipo

Se adicionar um novo request/response type, registrar em `AppJsonSerializerContext`:

```csharp
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase)]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
    public JsonTypeInfo<CreateUserRequest> CreateUserRequest 
        => GetTypeInfo(typeof(CreateUserRequest));
    
    public JsonTypeInfo<UserResponse> UserResponse 
        => GetTypeInfo(typeof(UserResponse));
}
```

### Configurar AuthorizationPolicy

```csharp
.WithAuthorization(
    new PolicyBuilder()
        .RequireClaim("sub")              // sub claim obrigatório
        .RequireRole("admin")             // role claim = admin
        .Custom(async ctx =>              // Autorização customizada
        {
            var userId = ctx.ExtractClaim("sub");
            var resource = await _db.GetResourceAsync(ctx.PathParameters["id"]);
            return resource?.OwnerId == userId;
        })
        .Build()
)
```

### Customizar Content-Type

```csharp
.MapGet("/export", HandleExport)
    .WithContentType("application/octet-stream")
```

## Checklist Antes de Submeter

- [ ] Handler implementado com `async Task<HttpResponse>`
- [ ] Rota registrada em `ConfigureRoutes()`
- [ ] Tipos de request/response registrados em `JsonSerializerContext`
- [ ] Testes unitários criados (xUnit + NSubstitute)
- [ ] `dotnet build` sem warnings
- [ ] `dotnet test` 100% passando
- [ ] `dotnet format --verify-no-changes` ok
- [ ] Sem reflection (usar source generators)
- [ ] HTTP status code correto (200, 201, 204, 400, 401, 403, 404, 500)

## Dicas de Autorização

| PolicyBuilder                | JWT Claim Necessário | Descrição                        |
|------------------------------|----------------------|----------------------------------|
| `.RequireUser()`             | `sub`                | Qualquer usuário autenticado     |
| `.RequireClaim("role")`      | `role`               | Role específico                  |
| `.RequireRole("admin")`      | `role=admin`         | Shortcut para role admin         |
| `.Custom(func)`              | Nenhum               | Lógica customizada no handler    |

## Testes Esperados

```csharp
[Fact]
public async Task HandleGetUser_WithValidId_ReturnsOk()
{
    var context = new RouteContext { PathParameters = { ["id"] = "123" } };
    var response = await _function.HandleGetUser(context);
    
    Assert.Equal(200, response.StatusCode);
    Assert.NotNull(response.Body);
}

[Fact]
public async Task HandleGetUser_WithoutAuthorizationClaim_Returns403()
{
    var context = new RouteContext { Headers = { } }; // Sem JWT
    var response = await _function.HandleGetUser(context);
    
    Assert.Equal(403, response.StatusCode);
}
```

## Links Úteis

- **CLAUDE.md:** Referência de API completa
- **Exemplos:** `/src/Examples/BasicRouting.cs`
- **Testes:** `/tests/RoutingTests.cs`
