# Kuva.ConsumerBff

## Objetivo
Backend for Frontend do App Consumidor Kuva. O serviço compõe APIs mobile para escolher loja, consultar catálogo/preço, criar pedido, gerar sessão de upload, confirmar pedido e acompanhar histórico/status.

## Arquitetura
Solução .NET 10 em camadas:

- `Kuva.ConsumerBff.Service`: Web API, controllers, middlewares, typed HTTP clients, health e metrics.
- `Kuva.ConsumerBff.Business`: facades, validações, mappers, idempotência e métricas de negócio.
- `Kuva.ConsumerBff.Repository`: EF Core, repositórios técnicos e unit of work.
- `Kuva.ConsumerBff.EFMigrations`: migrations isoladas.
- `Kuva.ConsumerBff.Entities`: contratos, options, constantes, exceções e modelos compartilhados.
- `Kuva.ConsumerBff.Tests`: testes unitários com NUnit e Moq.

## Responsabilidades
O BFF autentica o consumidor, propaga correlation id, compõe respostas dos serviços internos, protege ownership de pedidos, padroniza erros mobile, expõe health/metrics e mantém somente dados técnicos como idempotência e auditoria sanitizada.

## Fora de escopo
O BFF não é dono de preço, loja, pedido, mídia, usuários ou retenção LGPD. Também não recebe fotos binárias, não faz proxy de upload e não armazena SAS URL.

## Requisitos
- .NET SDK 10
- Docker e Docker Compose para execução local completa
- SQL Server local ou via `docker compose`

## Rodando local
```bash
dotnet restore Source/Kuva.ConsumerBff.slnx
dotnet run --project Source/Kuva.ConsumerBff.Service
```

## Docker Compose
```bash
docker compose up --build
```

Serviços esperados:

- API: `http://localhost:8082`
- Swagger: `http://localhost:8082/swagger`
- Health: `http://localhost:8082/health`
- Metrics: `http://localhost:8082/metrics`
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000`

## Variáveis de ambiente
Principais chaves:

- `ASPNETCORE_ENVIRONMENT`
- `ASPNETCORE_URLS`
- `ConnectionStrings__ConsumerBffDatabase`
- `KeyVault__Uri`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Jwt__JwksUrl`
- `Cors__AllowedOrigins__0`
- `DownstreamServices__Store__BaseUrl`
- `DownstreamServices__CatalogPricing__BaseUrl`
- `DownstreamServices__Order__BaseUrl`
- `DownstreamServices__Media__BaseUrl`
- `Resilience__TimeoutSeconds`
- `Observability__ServiceName`
- `Observability__PrometheusEnabled`

## Migrations
```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialCreate \
  --project Source/Kuva.ConsumerBff.EFMigrations \
  --startup-project Source/Kuva.ConsumerBff.EFMigrations \
  --context ConsumerBffDbContext \
  --output-dir Migrations
```

## Endpoints
- `GET /api/v1/consumer/stores`
- `GET /api/v1/consumer/stores/{storeId}`
- `GET /api/v1/consumer/stores/{storeId}/catalog`
- `GET /api/v1/consumer/stores/{storeId}/skus`
- `GET /api/v1/consumer/stores/{storeId}/skus/{skuId}`
- `POST /api/v1/consumer/orders`
- `POST /api/v1/consumer/orders/{orderId}/upload-session`
- `POST /api/v1/consumer/orders/{orderId}/confirm`
- `POST /api/v1/consumer/orders/{orderId}/cancel`
- `GET /api/v1/consumer/orders`
- `GET /api/v1/consumer/orders/{orderId}`
- `GET /api/v1/consumer/orders/{orderId}/status`
- `GET /health`, `/health/live`, `/health/ready`, `/metrics`

## Observabilidade
OpenTelemetry instrumenta ASP.NET Core, HTTP clients, runtime e métricas customizadas. O endpoint `/metrics` expõe counters Prometheus para pedidos, confirmações, upload sessions, falhas downstream, falhas de confirmação e replays de idempotência.

## Testes e cobertura
```bash
dotnet test Source/Kuva.ConsumerBff.slnx -m:1
dotnet test Source/Kuva.ConsumerBff.slnx --collect:"XPlat Code Coverage" --results-directory TestResults -m:1
```

## Segurança e LGPD
JWT é obrigatório em endpoints de pedido. O consumidor autenticado vem da claim `sub`; `consumerId` no body não é aceito. Logs e auditoria não persistem token, SAS URL, foto, e-mail ou telefone. IP e User-Agent são armazenados como hash.

## Troubleshooting
- Se o restore consultar vulnerabilidades NuGet sem rede, use `-p:NuGetAudit=false`.
- Se o VSTest falhar com `SocketException: Permission denied` no sandbox, execute os testes fora do sandbox local.
- Confirme as URLs downstream via variáveis `DownstreamServices__*__BaseUrl`.
