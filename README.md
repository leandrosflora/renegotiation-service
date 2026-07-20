# Renegotiation Service

Serviço em .NET 8 para orquestrar a jornada de renegociação de dívidas.

Ele expõe endpoints canônicos para consulta de cliente, contratos, dívidas, elegibilidade, simulação, confirmação de acordo e documento de formalização. Internamente, delega as chamadas para APIs especializadas: Client API, Eligibility API, Contracting API e Formalization API.

## Responsabilidades

- Consultar cliente por CPF.
- Consultar contratos de um cliente.
- Consultar dívidas de um contrato.
- Verificar elegibilidade de renegociação.
- Simular uma proposta de renegociação.
- Confirmar acordo a partir de uma simulação.
- Consultar documento/link de formalização.
- Aplicar retry nas chamadas HTTP para APIs dependentes.
- Padronizar respostas e erros para consumidores upstream.

## Arquitetura

```mermaid
flowchart LR
    C["Consumer / Channel / BFF"] --> RS["renegotiation-service"]

    RS -->|"GET /clients/:cpf"| CA["Client API"]
    RS -->|"GET /clients/:clientId/contracts"| CA
    RS -->|"GET /contracts/:contractId/debts"| CA

    RS -->|"GET /contracts/:contractId/eligibility"| EA["Eligibility API"]
    RS -->|"POST /contracts/:contractId/simulations"| COA["Contracting API"]
    RS -->|"POST /simulations/:simulationId/confirmations"| FA["Formalization API"]
    RS -->|"GET /agreements/:agreementId/document"| FA
```

## Stack

- .NET 8 / ASP.NET Core Minimal APIs
- Swagger / OpenAPI em ambiente `Development`
- `HttpClientFactory`
- `Microsoft.Extensions.Http.Resilience`
- JWT interno (HS256) obrigatório em todos os endpoints (`FallbackPolicy` exige usuário autenticado)
- PostgreSQL, usado apenas por `simular_proposta` para lease de idempotência (`ops.simulation_idempotency`)
- Logs com `TraceId`, `SpanId`, `ParentId` e `CorrelationId`

## Autenticação e autorização

Todo endpoint exige `Authorization: Bearer <jwt>` (HS256, `iss=conversational-ai-platform`, `aud=renegotiation-service`) — não há endpoint público neste serviço.

Além disso, **`POST /contracts/{contractId}/simulations`** e **`POST /simulations/{simulationId}/confirmations`** são "governed tools": só aceitam um token assinado pelo `tool-service-renegotiation` (`sub=tool-service-renegotiation`, `token_use=governed_tool`, `tool_name` batendo com a operação), com claims de `tenant_id`, `conversation_id`, `message_id`, `journey_stage` e `journey_version`. Esses dois endpoints também exigem um header `Idempotency-Key` que precisa bater exatamente com a claim `policy_id` assinada no token.

| Operação | Estágios de jornada permitidos | Exige confirmação explícita? |
|---|---|---|
| `simular_proposta` | `ContractSelected`, `EligibilityChecked`, `SimulationParametersPending` | Não |
| `confirmar_acordo` | `ProposalSelected`, `ConfirmationPending` | Sim — `confirmation_message_id` deve bater com o `message_id` da mensagem atual |

Chamada fora do estágio permitido, com `Idempotency-Key` divergente da `policy_id`, ou sem evidência de confirmação quando exigida, recebe `403 Forbidden`.

## Endpoints

Todos exigem `Authorization: Bearer <jwt>` (ver seção acima); os dois marcados como "governed" exigem também o token específico do `tool-service-renegotiation` e `Idempotency-Key`.

### `GET /clients/{cpf}`

Consulta dados básicos do cliente por CPF.

Resposta:

```json
{
  "found": true,
  "client": {
    "cpf": "12345678900",
    "name": "Cliente Exemplo"
  }
}
```

### `GET /clients/{clientId}/contracts`

Consulta contratos associados ao cliente.

Resposta:

```json
{
  "found": true,
  "contracts": [
    {
      "contractId": "contract-001",
      "productType": "loan",
      "outstandingAmount": 1500.75
    }
  ]
}
```

### `GET /contracts/{contractId}/debts`

Consulta dívidas vinculadas ao contrato.

Resposta:

```json
{
  "found": true,
  "debts": [
    {
      "debtId": "debt-001",
      "amount": 500.25,
      "dueDate": "2026-01-10",
      "daysOverdue": 30
    }
  ]
}
```

### `GET /contracts/{contractId}/eligibility`

Verifica se o contrato é elegível para renegociação.

Resposta:

```json
{
  "eligible": true,
  "reason": null
}
```

### `POST /contracts/{contractId}/simulations` (governed)

Simula uma proposta de renegociação. Exige `Idempotency-Key` e o token `governed_tool` descrito acima; retorna `400` sem `Idempotency-Key`, `403` fora de estágio ou com chave divergente da `policy_id` assinada.

Request:

```json
{
  "installments": 12,
  "discount_percentage": 10
}
```

Resposta:

```json
{
  "possible": true,
  "reason": null,
  "simulation": {
    "simulationId": "sim-001",
    "installments": 12,
    "installmentAmount": 120.5,
    "totalAmount": 1446.0
  }
}
```

### `POST /simulations/{simulationId}/confirmations` (governed)

Confirma o acordo a partir de uma simulação. Exige `Idempotency-Key`, o token `governed_tool`, e evidência de confirmação explícita (`confirmation_message_id` assinado batendo com a mensagem atual) — retorna `403` sem essa evidência.

Resposta:

```json
{
  "confirmed": true,
  "reason": null,
  "agreement": {
    "agreementId": "agr-001"
  }
}
```

### `GET /agreements/{agreementId}/document`

Consulta o documento ou link de formalização do acordo.

Resposta:

```json
{
  "available": true,
  "reason": null,
  "documentUrl": "https://example.com/document.pdf"
}
```

## Tratamento de erros

Quando uma API dependente falha após as tentativas de retry, o serviço retorna `502 Bad Gateway` com uma resposta padronizada.

Exemplo:

```json
{
  "error": "Client API unavailable"
}
```

Mapeamento atual:

| Dependência | Endpoints afetados | Erro |
|---|---|---|
| Client API | `/clients/{cpf}`, `/clients/{clientId}/contracts`, `/contracts/{contractId}/debts` | `Client API unavailable` |
| Eligibility API | `/contracts/{contractId}/eligibility` | `Eligibility API unavailable` |
| Contracting API | `/contracts/{contractId}/simulations` | `Contracting API unavailable` |
| Formalization API | `/simulations/{simulationId}/confirmations`, `/agreements/{agreementId}/document` | `Formalization API unavailable` |

## Configuração

Arquivo base: `appsettings.json`.

```json
{
  "ClientApi": {
    "BaseUrl": "http://localhost:9401",
    "RetryAttempts": 2
  },
  "EligibilityApi": {
    "BaseUrl": "http://localhost:9402",
    "RetryAttempts": 2
  },
  "ContractingApi": {
    "BaseUrl": "http://localhost:9403",
    "RetryAttempts": 2
  },
  "FormalizationApi": {
    "BaseUrl": "http://localhost:9404",
    "RetryAttempts": 2
  },
  "InternalAuth": {
    "Issuer": "conversational-ai-platform",
    "ServiceName": "renegotiation-service",
    "SigningKey": "",
    "TokenTtlSeconds": 300
  },
  "Postgres": {
    "ConnectionString": "Host=localhost;Port=5432;Database=conversational_ai;Username=postgres;Password=postgres",
    "IdempotencyLeaseSeconds": 120
  }
}
```

### Variáveis de ambiente

Exemplos:

```bash
ClientApi__BaseUrl=http://localhost:9401
ClientApi__RetryAttempts=2
EligibilityApi__BaseUrl=http://localhost:9402
EligibilityApi__RetryAttempts=2
ContractingApi__BaseUrl=http://localhost:9403
ContractingApi__RetryAttempts=2
FormalizationApi__BaseUrl=http://localhost:9404
FormalizationApi__RetryAttempts=2
InternalAuth__SigningKey=<segredo-com-pelo-menos-32-bytes>
Postgres__ConnectionString=Host=localhost;Port=5432;Database=conversational_ai;Username=postgres;Password=postgres
```

## Execução local

Pré-requisitos:

- .NET SDK 8+
- Client API, Eligibility API, Contracting API e Formalization API disponíveis
- PostgreSQL acessível (só é usado pelo lease de idempotência de `simular_proposta`; o schema é criado automaticamente no startup)
- `InternalAuth__SigningKey` com pelo menos 32 bytes, igual ao configurado nos serviços que chamam este

Restaurar dependências:

```bash
dotnet restore
```

Executar:

```bash
dotnet run
```

Rodar os testes:

```bash
dotnet test
```

URLs locais configuradas em `launchSettings.json`:

- HTTP: `http://localhost:5266`
- HTTPS: `https://localhost:7093`
- Swagger em desenvolvimento: `/swagger`

## Fluxo sugerido da jornada

1. Consultar cliente por CPF: `GET /clients/{cpf}`.
2. Consultar contratos do cliente: `GET /clients/{clientId}/contracts`.
3. Consultar dívidas do contrato: `GET /contracts/{contractId}/debts`.
4. Verificar elegibilidade: `GET /contracts/{contractId}/eligibility`.
5. Simular renegociação: `POST /contracts/{contractId}/simulations`.
6. Confirmar acordo: `POST /simulations/{simulationId}/confirmations`.
7. Buscar documento de formalização: `GET /agreements/{agreementId}/document`.

## Observabilidade

- Logs incluem `TraceId`, `SpanId` e `ParentId` via `ActivityTrackingOptions`.
- Cada requisição recebe um `CorrelationId` gerado no middleware.
- Scopes são renderizados no console.
- Logs de falha indicam a dependência afetada e o tipo da exceção original.

## Resiliência

Cada client HTTP usa `AddStandardResilienceHandler` com:

- retry configurável por dependência;
- delay inicial de 200 ms;
- configuração default de tentativas em `RetryAttempts`.

## Testes

```bash
dotnet test
```

`renegotiation-service.Tests` cobre os endpoints de simulação/formalização (incluindo a política governed-tool) e o mapeamento de erros das APIs dependentes.

## CI

`.github/workflows/ci.yml` roda `dotnet build`/`dotnet test` a cada push/PR para `master`, com um container Postgres efêmero (necessário pelo lease de idempotência de `simular_proposta`).

## Limitações atuais

- Não há mensageria neste serviço.
- Não há validação explícita de CPF, `contractId`, `simulationId` ou `agreementId` além do que os endpoints downstream já validam.
- O serviço depende da disponibilidade das quatro APIs downstream.
- A única persistência local é o lease de idempotência de `simular_proposta`; os demais endpoints são stateless.

## Comandos úteis

```bash
# Build
dotnet build

# Test
dotnet test

# Run
dotnet run

# Swagger local
open http://localhost:5266/swagger
```

## Estrutura principal

```text
.
├── Adapters
│   ├── Inbound
│   │   └── Http
│   └── Outbound
│       ├── Http
│       └── Persistence
├── Application
│   ├── Ports
│   └── UseCases
├── Configuration
├── Domain
├── Platform
├── Program.cs
├── appsettings.json
├── Dockerfile
├── renegotiation-service.csproj
└── renegotiation-service.Tests/
```
