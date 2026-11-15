# oficina-billing-service

Microsserviço de **orçamento e pagamento** da Oficina Mecânica (FIAP 15SOAT, Fase 4).

> **Estado atual:** esqueleto da [Etapa 0](https://github.com/Duds27/tech-challenge-fiap/blob/main/docs/fase4/etapa-0-fundacao.md). A API tem health checks, logs estruturados, autenticação JWT e Swagger. O domínio, o PostgreSQL, a mensageria e o Mercado Pago entram na [Etapa 3](https://github.com/Duds27/tech-challenge-fiap/blob/main/docs/fase4/etapa-3-billing-service.md).

## Responsabilidades

- Gerar o orçamento a partir do comando `GerarOrcamento` e enviá-lo ao cliente.
- Registrar a aprovação ou a recusa do cliente.
- Cobrar pelo Mercado Pago (Checkout Pro) e confirmar pelo webhook.
- Estornar o pagamento quando a saga mandar (`EstornarPagamento`).

**Não é dele:**

- Cadastro de clientes, veículos e peças, e o ciclo de vida da OS: são do `os-service`.
- A fila de execução: é do `execution-service`.

Os dados que ele precisa chegam dentro das mensagens. Ver os [contratos de mensagens](https://github.com/Duds27/tech-challenge-fiap/blob/main/docs/contratos-mensagens.md).

## Estrutura

```
src/
  Oficina.Billing.Domain/          entidades e regras (Orcamento, Pagamento)
  Oficina.Billing.Application/     casos de uso e portas (ex.: IGatewayPagamento)
  Oficina.Billing.Infrastructure/  PostgreSQL, MassTransit, Mercado Pago
  Oficina.Billing.API/             controllers, webhook, health checks
tests/
  Oficina.Billing.UnitTests/
  Oficina.Billing.IntegrationTests/
```

- **Versões:** os pacotes ficam centralizados em `Directory.Packages.props`.
- **Avisos:** no código de `src/`, todo aviso é tratado como erro (`src/Directory.Build.props`).
- **SDK:** o `global.json` fixa o .NET 8.

## Como rodar

Testes:

```bash
dotnet test
```

Serviço + PostgreSQL + RabbitMQ:

```bash
docker compose up --build
```

- Health checks: http://localhost:8082/health/live e http://localhost:8082/health/ready
- Swagger: http://localhost:8082/swagger
- RabbitMQ: http://localhost:15672 (usuário `oficina`, senha `oficina_dev`)

Sem Docker: `dotnet run --project src/Oficina.Billing.API` (porta 8082).

## CI

O `.github/workflows/ci.yml` roda em PR e push na `main`: restore, build, testes com cobertura e build da imagem Docker.

- O job `build-test-quality` é o *required check* da proteção da `main`.
- Cobertura mínima de 80% e SonarCloud entram na [Etapa 4](https://github.com/Duds27/tech-challenge-fiap/blob/main/docs/fase4/etapa-4-plataforma-cicd.md).
