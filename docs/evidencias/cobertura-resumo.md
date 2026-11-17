# Summary
<details open><summary>Summary</summary>

|||
|:---|:---|
| Generated on: | 17/11/2026 - 07:54:52 |
| Coverage date: | 17/11/2026 - 07:08:24 |
| Parser: | Cobertura |
| Assemblies: | 4 |
| Classes: | 50 |
| Files: | 39 |
| **Line coverage:** | 94.7% (1073 of 1133) |
| Covered lines: | 1073 |
| Uncovered lines: | 60 |
| Coverable lines: | 1133 |
| Total lines: | 2559 |
| **Branch coverage:** | 87.6% (311 of 355) |
| Covered branches: | 311 |
| Total branches: | 355 |
| **Method coverage:** | [Feature is only available for sponsors](https://reportgenerator.io/pro) |

</details>

## Coverage
<details><summary>Oficina.Billing.API - 91.4%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**Oficina.Billing.API**|**91.4%**|**80%**|
|Oficina.Billing.API.Controllers.DevPagamentosController|95.4%|78.5%|
|Oficina.Billing.API.Controllers.OrcamentosController|100%|100%|
|Oficina.Billing.API.Controllers.PagamentosController|100%|100%|
|Oficina.Billing.API.Controllers.WebhookMercadoPagoController|100%|100%|
|Oficina.Billing.API.Middleware.CorrelationIdMiddleware|100%|100%|
|Oficina.Billing.API.Observabilidade.ContextoDoTrace|100%|100%|
|Oficina.Billing.API.Observabilidade.Telemetria|76.9%|75%|
|Oficina.Billing.API.Security.Autorizacao|90.4%|71.4%|
|Program|96.6%|83.3%|

</details>
<details><summary>Oficina.Billing.Application - 94.2%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**Oficina.Billing.Application**|**94.2%**|**92.2%**|
|Oficina.Billing.Application.Abstractions.ConflitoDeConcorrenciaException|33.3%||
|Oficina.Billing.Application.Abstractions.GatewayIndisponivelException|66.6%||
|Oficina.Billing.Application.Abstractions.GatewayRecusouException|33.3%||
|Oficina.Billing.Application.DependencyInjection|100%||
|Oficina.Billing.Application.DTOs.MapeamentoDtos|100%|100%|
|Oficina.Billing.Application.Mensagens.FabricaMensagens|100%|50%|
|Oficina.Billing.Application.MetricasBilling|100%||
|Oficina.Billing.Application.OrcamentoOptions|100%||
|Oficina.Billing.Application.PagamentosOptions|100%||
|Oficina.Billing.Application.UseCases.ComandosSagaUseCases|100%|95%|
|Oficina.Billing.Application.UseCases.EstornoAutomatico|100%|100%|
|Oficina.Billing.Application.UseCases.OrcamentosUseCases|97.5%|100%|
|Oficina.Billing.Application.UseCases.PagamentosUseCases|90.7%|88.4%|
|Oficina.Billing.Application.UseCases.Resultado`1|100%||
|Oficina.Billing.Application.UseCases.Solicitante|100%|100%|

</details>
<details><summary>Oficina.Billing.Domain - 95.3%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**Oficina.Billing.Domain**|**95.3%**|**100%**|
|Oficina.Billing.Domain.ClienteOrcamento|100%||
|Oficina.Billing.Domain.EstornoImpossivelException|33.3%||
|Oficina.Billing.Domain.ItemOrcamento|100%|100%|
|Oficina.Billing.Domain.Notificacao|100%|100%|
|Oficina.Billing.Domain.Orcamento|100%|100%|
|Oficina.Billing.Domain.Pagamento|100%|100%|
|Oficina.Billing.Domain.RegraDeNegocioException|33.3%||

</details>
<details><summary>Oficina.Billing.Infrastructure - 96.6%</summary>

|**Name**|**Line**|**Branch**|
|:---|---:|---:|
|**Oficina.Billing.Infrastructure**|**96.6%**|**84.7%**|
|Oficina.Billing.Infrastructure.DependencyInjection|97.5%|58.3%|
|Oficina.Billing.Infrastructure.Messaging.CancelarOrcamentoConsumer|100%||
|Oficina.Billing.Infrastructure.Messaging.ContextoDeLogConsumeFilter`1|91.6%|100%|
|Oficina.Billing.Infrastructure.Messaging.EscopoDeLogFilter`1|94.4%|50%|
|Oficina.Billing.Infrastructure.Messaging.EstornarPagamentoConsumer|100%||
|Oficina.Billing.Infrastructure.Messaging.FalhaDeEstornoConsumer|100%|50%|
|Oficina.Billing.Infrastructure.Messaging.GerarOrcamentoConsumer|100%||
|Oficina.Billing.Infrastructure.Messaging.MassTransitPublicadorEventos|100%||
|Oficina.Billing.Infrastructure.Messaging.MensageriaExtensions|100%|75%|
|Oficina.Billing.Infrastructure.Messaging.ProcessarNotificacaoPagamentoConsu<br/>mer|100%||
|Oficina.Billing.Infrastructure.Notificacoes.EnvioNotificacoes|90%|83.3%|
|Oficina.Billing.Infrastructure.Pagamentos.AssinaturaWebhook|100%|100%|
|Oficina.Billing.Infrastructure.Pagamentos.FakeGatewayPagamento|95.2%|100%|
|Oficina.Billing.Infrastructure.Pagamentos.MercadoPagoGateway|100%|94%|
|Oficina.Billing.Infrastructure.Pagamentos.MercadoPagoOptions|100%||
|Oficina.Billing.Infrastructure.Persistence.BillingDbContext|100%||
|Oficina.Billing.Infrastructure.Persistence.BillingDbContextFactory|0%||
|Oficina.Billing.Infrastructure.Persistence.BillingRepositorio|100%||
|Oficina.Billing.Infrastructure.Persistence.MigradorBanco|77.7%|100%|

</details>
