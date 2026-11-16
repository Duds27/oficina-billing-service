# oficina-billing-service

Microsserviço de **orçamento e pagamento** da Oficina Mecânica (FIAP 15SOAT, Fase 4).

> **Estado atual:** [Etapa 3](https://github.com/Duds27/oficina-os-service/blob/main/docs/fase4/etapa-3-billing-service.md) implementada: domínio, PostgreSQL com outbox/inbox, consumers da saga, API de orçamento, Mercado Pago (Checkout Pro) com webhook e Terraform. Falta a validação no sandbox do Mercado Pago (spike S3), que precisa de uma conta de teste.

## Responsabilidades

- Gerar o orçamento a partir do comando `GerarOrcamento` e enviá-lo ao cliente.
- Registrar a aprovação ou a recusa do cliente.
- Cobrar pelo Mercado Pago (Checkout Pro) e confirmar pelo webhook.
- Estornar o pagamento quando a saga mandar (`EstornarPagamento`).

**Não é dele:**

- Cadastro de clientes, veículos e peças, e o ciclo de vida da OS: são do `os-service`.
- A fila de execução: é do `execution-service`.

Os dados que ele precisa chegam dentro das mensagens. Ver os [contratos de mensagens](https://github.com/Duds27/oficina-os-service/blob/main/docs/contratos-mensagens.md).

## Fluxo

```
GerarOrcamento ──▶ Orçamento Enviado (e-mail ao cliente) ──▶ OrcamentoGerado
                       │
     aprovação (API) ──┴─▶ Aprovado + Pagamento Pendente + preferência no Mercado Pago ──▶ OrcamentoAprovado (link)
     recusa (API)   ─────▶ Recusado ──▶ OrcamentoRecusado
                                │
  webhook do Mercado Pago ──────┴─▶ consulta GET /v1/payments/{id} ──▶ PagamentoConfirmado | PagamentoRecusado
  EstornarPagamento (C3) ────────▶ refund no Mercado Pago ──▶ PagamentoEstornado
  CancelarOrcamento (C2) ────────▶ Cancelado (e o pagamento pendente)
```

| Status | Valores |
| --- | --- |
| Orçamento | `Gerado → Enviado → Aprovado \| Recusado \| Cancelado \| Expirado` |
| Pagamento | `Pendente → Aprovado \| Recusado \| Cancelado`, e `Aprovado → Estornado` |

Regras que valem saber:

- **Total:** sempre recalculado a partir dos itens. O `ValorTotal` do `GerarOrcamento` é só informativo; divergência vira log de aviso.
- **Decisão:** só o orçamento `Enviado` e dentro da validade. Vencido vira `Expirado` (409).
- **Uma saga, um orçamento:** `GerarOrcamento` repetido é ignorado. `CancelarOrcamento` antes dele registra o orçamento já cancelado (regra 5 da seção 5 dos contratos).
- **Estorno automático** (regras 1, 2 e 3 da seção 5): pagamento aprovado depois do cancelamento do orçamento, ou depois de uma recusa, é estornado sozinho e publica `PagamentoEstornado` (a saga, já encerrada, ignora).
- **Estorno repetido** não estorna de novo, mas responde `PagamentoEstornado` de novo: a saga espera essa resposta em `AguardandoEstorno`.

## API

| Método | Rota | Quem |
| --- | --- | --- |
| GET | `/api/orcamentos/{id}` | Equipe (`Admin`, `Mecanico`) ou o cliente dono |
| GET | `/api/orcamentos?osId=` | Equipe; o cliente só recebe os dele |
| POST | `/api/orcamentos/{id}/aprovacao` | O cliente dono, ou o atendente (`Admin`) registrando a decisão dele. Corpo: `{ "aprovado": true, "motivo": "..." }` |
| GET | `/api/pagamentos/{id}` | Equipe ou o cliente dono |
| POST | `/webhooks/mercadopago` | Mercado Pago, com assinatura `x-signature` (sem JWT) |
| POST | `/dev/pagamentos/{id}/simular?resultado=aprovado\|recusado` | Só em `Development` com o gateway Fake |

- **Dono do orçamento:** o `clienteId` do token (claim da Lambda no login por CPF; tokens antigos só têm o `sub`) tem que ser o do orçamento. Senão, 403.
- **Aprovação:** cria o pagamento, a preferência no gateway (`X-Idempotency-Key` = id do pagamento) e publica `OrcamentoAprovado`, numa gravação só. Gateway fora do ar: 503 sem gravar nada.
- **Webhook:** valida o HMAC-SHA256 de `id:{data.id};request-id:{x-request-id};ts:{ts};` com o segredo, responde 200 rápido e processa pelo outbox. Assinatura inválida, ou nenhum segredo configurado: 401.

Erros: `404` não encontrado · `403` de outro cliente · `409` fora do status ou vencido · `503` gateway fora do ar · `400` corpo inválido.

## Mercado Pago

- **Adapter:** `MercadoPagoGateway`, com `HttpClient` tipado e o *standard resilience handler* (timeout por tentativa, retry com backoff em 5xx/408/429/falha de rede e circuit breaker).
  - `POST /checkout/preferences`: um item com o total (o Checkout Pro exige quantidade inteira), `external_reference` = id do pagamento, `notification_url` e expiração no prazo de pagamento.
  - `GET /v1/payments/{id}`: `approved` → confirmado; `rejected`/`cancelled` → recusado; `refunded`/`charged_back` → estornado; o resto aguarda.
  - `POST /v1/payments/{id}/refunds` com `X-Idempotency-Key`.
- **Gateway Fake** (`Pagamentos__Gateway=Fake`, o padrão): link fictício e resultado simulado pela rota `/dev`. Usado em testes, CI e compose.
- **Credenciais:** access token de **teste** e segredo do webhook no Secrets Manager (`<projeto>-<ambiente>/billing/mercadopago`, preenchido à mão). Nunca no repositório nem no state do Terraform.

## Falha definitiva de estorno

Se o estorno falhar de vez (retries esgotados, ou o pagamento não pode ser estornado):

1. A mensagem vai para `billing-estornar-pagamento_error`, e a saga fica em `AguardandoEstorno`.
2. O billing loga um **alerta crítico** (`ALERTA: estorno do pagamento ... falhou de vez`), com a OS, o pagamento e o CorrelationId. No New Relic, esse log vira alerta (Etapa 5).
3. **O que fazer:** corrigir a causa (ex.: credencial do Mercado Pago) e mover a mensagem de `billing-estornar-pagamento_error` para `billing-estornar-pagamento` na interface do RabbitMQ. O reprocessamento é seguro: o estorno usa a mesma chave de idempotência.

## Configuração

| Variável | Uso |
| --- | --- |
| `ConnectionStrings__BillingDb` | PostgreSQL (as migrations rodam na inicialização) |
| `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | Mesmo JWT do os-service e da Lambda |
| `RabbitMq__Host`, `__Port`, `__VirtualHost`, `__Username`, `__Password` | Broker |
| `Pagamentos__Gateway` | `Fake` (padrão) ou `MercadoPago` |
| `Pagamentos__PrazoPagamento` | Validade do link (padrão 48 h; 2 min no compose) |
| `Pagamentos__MercadoPago__AccessToken`, `__WebhookSecret` | Do secret do Mercado Pago |
| `Pagamentos__MercadoPago__NotificationUrl` | Rota pública do webhook no API Gateway |
| `Orcamento__Validade`, `Orcamento__UrlAprovacao` | Validade do orçamento (padrão 48 h; 2 min no compose) e link do e-mail |
| `Notificacoes__IntervaloMs` | Envio simulado dos e-mails (padrão 5 s) |

## Estrutura

```
src/
  Oficina.Billing.Domain/          Orcamento, Pagamento, Notificacao e as regras
  Oficina.Billing.Application/     casos de uso, contratos (Oficina.Contracts) e portas (IGatewayPagamento)
  Oficina.Billing.Infrastructure/  PostgreSQL (EF Core + outbox/inbox), MassTransit, Mercado Pago, gateway Fake
  Oficina.Billing.API/             controllers, webhook, health checks
infra/                             Terraform: RDS PostgreSQL, secrets e ECR
tests/
  Oficina.Billing.UnitTests/         domínio, casos de uso, assinatura, adapter do Mercado Pago, contratos
  Oficina.Billing.IntegrationTests/  PostgreSQL, RabbitMQ e WireMock do Mercado Pago (Testcontainers)
```

## Como rodar

Testes (os de integração sobem PostgreSQL e RabbitMQ no Docker):

```bash
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"
```

Serviço + PostgreSQL + RabbitMQ próprio:

```bash
docker compose up --build
```

Junto com o os-service (e o execution-service), no RabbitMQ dele (suba o compose do os-service antes):

```bash
docker compose -f docker-compose.yml -f docker-compose.integrado.yml up --build billing-api billing-db
```

Com os três serviços no ar, a saga vai do início ao fim: OS aberta → diagnóstico → orçamento → aprovação → `POST /dev/pagamentos/{id}/simular?resultado=aprovado` → execução → OS concluída.

- Health checks: http://localhost:8082/health/live e http://localhost:8082/health/ready
- Swagger: http://localhost:8082/swagger

## Infra (Terraform)

`infra/` cria o RDS PostgreSQL 16 (privado, só os nós do EKS acessam), os secrets `billing/db` e `billing/mercadopago` e o ECR. O estado fica no bucket S3 da Fase 3 (`billing-service/<ambiente>/terraform.tfstate`).

```bash
cd infra
terraform init -backend-config="bucket=$TF_STATE_BUCKET" -backend-config="key=billing-service/$ENV/terraform.tfstate" \
  -backend-config="region=$AWS_REGION" -backend-config="dynamodb_table=$TF_LOCK_TABLE"
terraform apply -var="environment=$ENV" -var="state_bucket=$TF_STATE_BUCKET"
```

Depois do `apply`, preencha o secret do Mercado Pago com o token de teste e o segredo do webhook. `terraform test` roda sem conta AWS (provider mockado).

## CI

O `.github/workflows/ci.yml` roda em PR e push na `main`:

- `build-test-quality` (*required check*): restore, build, testes com cobertura (`coverlet.runsettings`, que exclui só as migrations) e build da imagem Docker.
- `terraform`: `fmt`, `validate` e `test` do `infra/`.
- O gate de cobertura (80%) e o SonarCloud entram na [Etapa 4](https://github.com/Duds27/oficina-os-service/blob/main/docs/fase4/etapa-4-plataforma-cicd.md). Hoje a cobertura de linhas é de cerca de 96%.
