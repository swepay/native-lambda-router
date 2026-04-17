# QA Agent - native-lambda-router

**Modelo:** Claude Sonnet 4  
**Ferramentas:** read, bash, grep  
**Foco:** Testes de roteamento, autorização, serialização

## Responsabilidades

1. **Testar roteamento** (path params, query string, métodos HTTP)
2. **Validar autorização** (JWT claims, policies, denials)
3. **Verificar serialização** (JsonSerializerContext, content-type)
4. **Testar edge cases** (handlers não encontrados, requests malformados)
5. **Performance & AOT compliance** (sem reflection)

## Plano de Testes

### 1. Testes de Roteamento Básico

```csharp
public class RoutingTests
{
    private readonly RoutedApiGatewayFunction _function;
    private readonly Mock<ILambdaContext> _lambdaContext;

    [Theory]
    [InlineData("GET", "/users/123", "HandleGetUser")]
    [InlineData("POST", "/users", "HandleCreateUser")]
    [InlineData("PUT", "/users/123", "HandleUpdateUser")]
    [InlineData("DELETE", "/users/123", "HandleDeleteUser")]
    public async Task RouteMatching_CorrectHandler_Invoked(
        string method, string path, string expectedHandler)
    {
        var request = new HttpRequest { Method = method, Path = path };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.NotNull(response);
        Assert.True(response.StatusCode >= 200 && response.StatusCode < 500);
    }

    [Fact]
    public async Task PathParameter_Extraction_Correct()
    {
        var request = new HttpRequest { Method = "GET", Path = "/users/abc-123" };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        var body = JsonDocument.Parse(response.Body).RootElement;
        Assert.Equal("abc-123", body.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("/users/123?page=1&limit=10", "page", "1")]
    [InlineData("/users/123?search=john&sort=name", "search", "john")]
    public async Task QueryString_Parsing_Correct(
        string path, string paramName, string expectedValue)
    {
        var request = new HttpRequest { Path = path };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        var body = JsonDocument.Parse(response.Body).RootElement;
        Assert.Equal(expectedValue, body.GetProperty(paramName).GetString());
    }
}
```

### 2. Testes de Autorização

```csharp
public class AuthorizationTests
{
    [Fact]
    public async Task Authorization_WithoutJWT_Returns403()
    {
        var request = new HttpRequest 
        { 
            Method = "GET", 
            Path = "/users/123",
            Headers = new HeaderDictionary() // Sem Authorization header
        };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task Authorization_WithValidJWT_Returns200()
    {
        var token = GenerateJWT(new { sub = "user123", role = "admin" });
        var request = new HttpRequest 
        { 
            Method = "GET", 
            Path = "/users/123",
            Headers = new HeaderDictionary { { "Authorization", $"Bearer {token}" } }
        };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task Authorization_RequireRole_DenyIfRoleMissing()
    {
        var token = GenerateJWT(new { sub = "user123" }); // Sem role
        var request = new HttpRequest 
        { 
            Method = "POST", 
            Path = "/admin/users",
            Headers = new HeaderDictionary { { "Authorization", $"Bearer {token}" } }
        };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task Authorization_CustomPolicy_AllowsUserOwnedResource()
    {
        var token = GenerateJWT(new { sub = "user123" });
        var request = new HttpRequest 
        { 
            Method = "GET", 
            Path = "/users/user123/profile",
            Headers = new HeaderDictionary { { "Authorization", $"Bearer {token}" } }
        };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        // Custom policy: só permite acessar recurso do próprio usuário
        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task Authorization_CustomPolicy_DenyIfNotOwner()
    {
        var token = GenerateJWT(new { sub = "user123" });
        var request = new HttpRequest 
        { 
            Method = "GET", 
            Path = "/users/user456/profile", // Diferente do sub
            Headers = new HeaderDictionary { { "Authorization", $"Bearer {token}" } }
        };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.Equal(403, response.StatusCode);
    }
}
```

### 3. Testes de Handler Não Encontrado

```csharp
public class NotFoundTests
{
    [Theory]
    [InlineData("GET", "/undefined-route")]
    [InlineData("POST", "/api/v2/unknown")]
    [InlineData("DELETE", "/admin/secret")]
    public async Task UnmappedRoute_Returns404(string method, string path)
    {
        var request = new HttpRequest { Method = method, Path = path };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.Equal(404, response.StatusCode);
    }
}
```

### 4. Testes de Serialização

```csharp
public class SerializationTests
{
    [Fact]
    public async Task JsonSerialization_CamelCase_Applied()
    {
        var request = new HttpRequest 
        { 
            Method = "GET", 
            Path = "/users/123",
            ContentType = "application/json"
        };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        var body = JsonDocument.Parse(response.Body).RootElement;
        // Espera camelCase: firstName, lastName (não FirstName, LastName)
        Assert.True(body.TryGetProperty("firstName", out _));
        Assert.False(body.TryGetProperty("FirstName", out _));
    }

    [Fact]
    public async Task JsonSerialization_NullValues_Excluded()
    {
        var request = new HttpRequest 
        { 
            Method = "POST", 
            Path = "/users",
            Body = """{"name": "John", "email": null}"""
        };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        var body = JsonDocument.Parse(response.Body).RootElement;
        // email não deve ser serializado se null
        Assert.False(body.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task ContentType_SetCorrectly()
    {
        var request = new HttpRequest { Method = "GET", Path = "/users/123" };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.Equal("application/json", response.ContentType);
    }

    [Fact]
    public async Task ContentType_CustomType_Respected()
    {
        var request = new HttpRequest { Method = "GET", Path = "/export/data" };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.Equal("application/octet-stream", response.ContentType);
    }
}
```

### 5. Testes de Validação de Request

```csharp
public class ValidationTests
{
    [Fact]
    public async Task EmptyRequest_ReturnsBadRequest()
    {
        var request = new HttpRequest 
        { 
            Method = "POST", 
            Path = "/users",
            Body = ""
        };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task MalformedJson_ReturnsBadRequest()
    {
        var request = new HttpRequest 
        { 
            Method = "POST", 
            Path = "/users",
            Body = """{"name": "John",}""" // JSON inválido
        };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.Equal(400, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/users/")]     // Path vazio
    [InlineData("POST", "/users//")]   // Double slash
    public async Task InvalidPath_ReturnsBadRequest(string method, string path)
    {
        var request = new HttpRequest { Method = method, Path = path };
        var response = await _function.FunctionHandler(request, _lambdaContext.Object);
        
        Assert.True(response.StatusCode >= 400);
    }
}
```

## Executar Testes

```bash
# Testes unitários
dotnet test --configuration Release

# Com cobertura
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover

# Apenas um teste específico
dotnet test --filter "RoutingTests"
```

## Métricas de Sucesso

- **100% testes passando**
- **Cobertura > 85%** (paths críticos = 100%)
- **Zero warnings** na compilação
- **Performance < 100ms** por rota (mock dependencies)
- **AOT compliance** (sem reflection nos testes)

## Casos Edge Esperados

| Cenário | Status Code | Descrição |
|---------|-------------|-----------|
| Rota não existe | 404 | Unmapped route |
| Sem JWT em rota protegida | 403 | Forbidden |
| JWT expirado | 401 | Unauthorized |
| Role incorreto | 403 | Forbidden |
| Request body malformado | 400 | Bad Request |
| Content-Type não suportado | 415 | Unsupported Media Type |
| Servidor erro interno | 500 | Internal Server Error |
